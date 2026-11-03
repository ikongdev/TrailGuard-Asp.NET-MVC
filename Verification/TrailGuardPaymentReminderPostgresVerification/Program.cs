using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

TrailGuardNpgsqlConfiguration.ApplyTimestampCompatibility();
if (!args.SequenceEqual(["--run"]) && !args.SequenceEqual(["--probe-local"]))
{
    Console.WriteLine("Use --probe-local for a read-only maintenance connection check, or --run to create, migrate, verify, and drop a new disposable local database. See README.md.");
    return;
}

try
{
    await using var database = DisposableDatabase.Load();
    await database.ProbeAsync();
    if (args[0] == "--probe-local") return;
    await database.CreateAsync();
    var clock = new ControlledClock();
    var sender = new ControlledSender();
    var services = new ServiceCollection();
    services.AddLogging(); // No console provider, SQL values, credentials, or application settings.
    services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(database.TestConnectionString));
    services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
    services.AddSingleton<TimeProvider>(clock);
    services.AddSingleton<IEmailSender>(sender);
    services.AddSingleton<IWebHostEnvironment>(new VerificationEnvironment());
    services.AddSingleton(Options.Create(new PaymentReminderOptions { Enabled = true, BatchSize = 50 }));
    services.AddSingleton(Options.Create(new EmailOptions { PublicBaseUrl = "http://127.0.0.1/" }));
    services.AddScoped<IPaymentDeadlineReminderStore, EfPaymentDeadlineReminderStore>();
    services.AddScoped<PaymentDeadlineReminderProcessor>();
    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    await using (var scope = provider.CreateAsyncScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.OpenConnectionAsync();
        await DisposableDatabase.VerifyEndpointAsync((NpgsqlConnection)context.Database.GetDbConnection(), database.Name);
        await context.Database.MigrateAsync();
        Verify.That(!(await context.Database.GetPendingMigrationsAsync()).Any(), "all existing migrations applied");
        Console.WriteLine($"Applied {(await context.Database.GetAppliedMigrationsAsync()).Count()} existing migrations to the new database.");
        foreach (var role in OperationalRolePolicy.AllowedRoles)
            context.Roles.Add(new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() });
        await context.SaveChangesAsync();
    }
    var suite = new ReminderVerification(provider, clock, sender);
    await suite.PaginationAndRecoveryAsync();
    await suite.TrackingExclusionsAsync();
    await suite.CompetingClaimsAndRecoveryAsync();
    await suite.CompetingProcessorsAsync();
    Console.WriteLine($"PASS: PostgreSQL reminder verification ({Verify.Count} assertions). Production processor, candidate query, EF store, and Identity role lookup executed against PostgreSQL; email delivery was controlled in memory.");
    Console.WriteLine("This verifies the bounded scenarios, not exactly-once inbox delivery, all PostgreSQL interleavings, or historical production delivery.");
}
catch (Exception exception)
{
    // Exception messages from drivers/configuration can contain private values; print only safe metadata.
    var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
    Console.Error.WriteLine($"FAIL: {exception.GetType().Name}" + (postgres is null ? "" : $"; SQLSTATE {postgres.SqlState}") +
        (exception is VerificationFailure ? $"; {exception.Message}" : "; private exception details withheld"));
    Environment.ExitCode = 1;
}

sealed class ReminderVerification(ServiceProvider provider, ControlledClock clock, ControlledSender sender)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);
    private readonly PaymentReminderOptions _options = provider.GetRequiredService<IOptions<PaymentReminderOptions>>().Value;
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    private DateTime Now => PhilippineWallClock.Now(clock);

    public async Task PaginationAndRecoveryAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentDeadlineReminderStore>();
        var registrations = await SeedAsync(context, 51);
        var rejected = registrations.Take(50).ToArray();
        var roles = await context.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
        for (var index = 0; index < rejected.Length; index++)
        {
            var user = rejected[index].User!;
            switch (index % 5)
            {
                case 0: user.IsActive = false; break;
                case 1: user.EmailConfirmed = false; break;
                case 2: user.Email = "invalid-email"; user.NormalizedEmail = "INVALID-EMAIL"; break;
                case 3:
                    context.UserRoles.Remove(await context.UserRoles.SingleAsync(r => r.UserId == user.Id));
                    break;
                case 4:
                    context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roles["Organizer"] });
                    break;
            }
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        for (var index = 0; index < 5; index++)
        {
            var row = await store.FindRegistrationAsync(rejected[index].Id, default);
            Verify.That(!await store.HasVerifiedParticipantAsync(row!.User, default), $"persisted rejection variant {index}");
        }
        var firstPage = await store.FindCandidatesAsync(Now, UtcNow, null, default);
        Verify.That(firstPage.Select(c => c.RegistrationId).SequenceEqual(rejected.Select(r => r.Id)), "first database page contains exactly the 50 rejected accounts");
        var secondPage = await store.FindCandidatesAsync(Now, UtcNow, firstPage[^1], default);
        Verify.That(secondPage.Count == 1 && secondPage[0].RegistrationId == registrations[^1].Id, "same-deadline cursor reaches eligible registration 51");
        await PollAsync();
        Verify.That(sender.Messages.Count == 1 && sender.Messages.Single().To == registrations[^1].User!.Email, "same poll reaches eligible recipient after full rejected page");
        var tracking = await context.PaymentDeadlineReminders.AsNoTracking().ToListAsync();
        Verify.That(tracking.Count == 1 && tracking[0].EventRegistrationId == registrations[^1].Id && tracking[0].State == PaymentDeadlineReminder.Sent,
            "all 50 rejected accounts leave no tracking or terminal suppression; successful reminder persisted");
        Console.WriteLine("PASS 1: pagination through 50 persisted account rejections (10 each: inactive, unconfirmed, invalid email, missing role, conflicting roles).");

        // Repair one of each rejection type using persisted Identity data, then use a fresh processor scope.
        foreach (var registration in rejected.Take(5))
        {
            var user = await context.Users.SingleAsync(u => u.Id == registration.UserId);
            user.IsActive = true; user.EmailConfirmed = true;
            user.Email = $"{user.Id}@example.com"; user.NormalizedEmail = user.Email.ToUpperInvariant();
            context.UserRoles.RemoveRange(await context.UserRoles.Where(r => r.UserId == user.Id).ToListAsync());
            await context.SaveChangesAsync();
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roles["Participant"] });
            await context.SaveChangesAsync();
        }
        await PollAsync();
        var repairedIds = rejected.Take(5).Select(r => r.Id).ToArray();
        Verify.That(await context.PaymentDeadlineReminders.CountAsync(r => repairedIds.Contains(r.EventRegistrationId) && r.State == PaymentDeadlineReminder.Sent) == 5,
            "all five repaired account variants process on later poll");
        Verify.That(sender.Messages.Count == 6 && sender.Messages.Count(m => m.To == registrations[^1].User!.Email) == 1, "original successful reminder is not redispatched during recovery");
        await PollAsync();
        Verify.That(sender.Messages.Count == 6 && await context.PaymentDeadlineReminders.CountAsync() == 6, "subsequent poll suppresses all successful reminders");
        Console.WriteLine("PASS 2: repaired accounts reconsidered; successful reminders suppressed on subsequent polls.");
    }

    public async Task TrackingExclusionsAsync()
    {
        clock.Advance(TimeSpan.FromDays(3)); // Prior synthetic deadlines are outside this scenario's window.
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentDeadlineReminderStore>();
        var registrations = await SeedAsync(context, 51);
        for (var index = 0; index < 50; index++)
        {
            var reminder = NewReminder(registrations[index]);
            reminder.AttemptCount = 1;
            if (index % 2 == 0) reminder.NextAttemptAtUtc = UtcNow.AddMinutes(5);
            else
            {
                reminder.State = PaymentDeadlineReminder.Sending;
                reminder.LeaseId = Guid.NewGuid(); reminder.LeaseExpiresAtUtc = UtcNow.Add(_options.LeaseDuration);
            }
            context.PaymentDeadlineReminders.Add(reminder);
        }
        await context.SaveChangesAsync();
        var page = await store.FindCandidatesAsync(Now, UtcNow, null, default);
        Verify.That(page.Count == 1 && page[0].RegistrationId == registrations[^1].Id, "25 future retries and 25 active leases excluded before database LIMIT 50");
        var before = sender.Messages.Count;
        await PollAsync();
        Verify.That(sender.Messages.Count == before + 1 && sender.Messages.Last().To == registrations[^1].User!.Email, "due recipient dispatched past non-due tracking prefix");
        var excludedIds = registrations.Take(50).Select(r => r.Id).ToArray();
        Verify.That(await context.PaymentDeadlineReminders.CountAsync(r => r.LeaseId != null) == 25 &&
            await context.PaymentDeadlineReminders.CountAsync(r => r.NextAttemptAtUtc > UtcNow) == 25 &&
            await context.PaymentDeadlineReminders.Where(r => excludedIds.Contains(r.EventRegistrationId)).AllAsync(r => r.AttemptCount == 1),
            "excluded tracking retains leases and retries without extra attempts");
        Console.WriteLine("PASS 3: actual database query excludes future retries and active leases before page limit.");
    }

    public async Task CompetingClaimsAndRecoveryAsync()
    {
        clock.Advance(TimeSpan.FromDays(3));
        long id;
        await using (var setup = provider.CreateAsyncScope())
        {
            var context = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = (await SeedAsync(context, 1)).Single();
            var reminder = NewReminder(row); context.Add(reminder); await context.SaveChangesAsync(); id = reminder.Id;
        }
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var timeout = new CancellationTokenSource(Bound);
        var token = timeout.Token;
        await firstContext.Database.OpenConnectionAsync(token);
        await secondContext.Database.OpenConnectionAsync(token);
        Verify.That(await BackendPidAsync(firstContext, token) != await BackendPidAsync(secondContext, token), "claim contenders use distinct PostgreSQL backend connections");
        var firstStore = firstScope.ServiceProvider.GetRequiredService<IPaymentDeadlineReminderStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<IPaymentDeadlineReminderStore>();
        var owners = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<bool> ContendAsync(IPaymentDeadlineReminderStore store, Guid owner)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await start.Task.WaitAsync(token);
            return await store.TryClaimAsync(id, owner, UtcNow, token);
        }
        var first = ContendAsync(firstStore, owners[0]); var second = ContendAsync(secondStore, owners[1]);
        await ready.Task.WaitAsync(token); start.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(token);
        Verify.That(results.Count(result => result) == 1, "exactly one simultaneous TryClaimAsync succeeds");
        var winner = results[0] ? owners[0] : owners[1];
        var claimed = await firstContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.Id == id, token);
        Verify.That(claimed.LeaseId == winner && claimed.AttemptCount == 1 && claimed.State == PaymentDeadlineReminder.Sending &&
            claimed.LeaseExpiresAtUtc?.ToUniversalTime() == UtcNow.Add(_options.LeaseDuration),
            $"winning ownership, attempt count, and lease expiry persisted (owner matches: {claimed.LeaseId == winner}; attempts: {claimed.AttemptCount}; state: {claimed.State}; expiry UTC: {claimed.LeaseExpiresAtUtc?.ToUniversalTime():O})");
        Verify.That(!await secondStore.TryClaimAsync(id, Guid.NewGuid(), UtcNow, token), "valid lease rejects another claim");
        Console.WriteLine("PASS 4a: synchronized independent connections contend; exactly one claim owns the valid lease.");

        clock.Advance(_options.LeaseDuration.Add(TimeSpan.FromSeconds(1)));
        var newOwner = Guid.NewGuid();
        Verify.That(await secondStore.TryClaimAsync(id, newOwner, UtcNow, token), "expired lease can be reclaimed by new owner");
        var recovered = await firstContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.Id == id, token);
        Verify.That(recovered.LeaseId == newOwner && recovered.AttemptCount == 2 && recovered.LeaseExpiresAtUtc?.ToUniversalTime() == UtcNow.Add(_options.LeaseDuration), "recovery ownership and incremented attempt count persisted");
        await firstStore.FinishAsync(id, winner, PaymentDeadlineReminder.Failed, UtcNow, "stale-owner", token);
        var staleWrite = await firstContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.Id == id, token);
        Verify.That(staleWrite.LeaseId == newOwner && staleWrite.State == PaymentDeadlineReminder.Sending && staleWrite.LastOutcome == null && staleWrite.UpdatedAtUtc == recovered.UpdatedAtUtc,
            "old owner cannot overwrite new active owner's result");
        await secondStore.FinishAsync(id, newOwner, PaymentDeadlineReminder.Sent, UtcNow, "accepted", token);
        await firstStore.FinishAsync(id, winner, PaymentDeadlineReminder.Failed, UtcNow.AddSeconds(1), "stale-owner", token);
        var finished = await firstContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.Id == id, token);
        Verify.That(finished.State == PaymentDeadlineReminder.Sent && finished.SentAtUtc?.ToUniversalTime() == UtcNow && finished.LastOutcome == "accepted" && finished.LeaseId == null && finished.AttemptCount == 2,
            "old owner cannot overwrite new owner's completed result");
        Console.WriteLine("PASS 5: controlled-time lease recovery; stale owner cannot overwrite active or completed new-owner results.");
    }

    public async Task CompetingProcessorsAsync()
    {
        clock.Advance(TimeSpan.FromDays(3));
        int registrationId;
        await using (var setup = provider.CreateAsyncScope())
            registrationId = (await SeedAsync(setup.ServiceProvider.GetRequiredService<ApplicationDbContext>(), 1)).Single().Id;
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var timeout = new CancellationTokenSource(Bound);
        var token = timeout.Token;
        await firstContext.Database.OpenConnectionAsync(token); await secondContext.Database.OpenConnectionAsync(token);
        Verify.That(await BackendPidAsync(firstContext, token) != await BackendPidAsync(secondContext, token), "processors use independent scopes, contexts, and backend connections");
        var gate = sender.BlockNext();
        var before = sender.Messages.Count;
        var first = firstScope.ServiceProvider.GetRequiredService<PaymentDeadlineReminderProcessor>().ProcessOnceAsync(token);
        try
        {
            await gate.Entered.Task.WaitAsync(token); // Sender entry proves the first claim has committed.
            var during = await secondContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.EventRegistrationId == registrationId, token);
            Verify.That(during.State == PaymentDeadlineReminder.Sending && during.LeaseId != null && during.LeaseExpiresAtUtc?.ToUniversalTime() > UtcNow && during.AttemptCount == 1,
                "first processor holds committed active lease while controlled sender blocks");
            await secondScope.ServiceProvider.GetRequiredService<PaymentDeadlineReminderProcessor>().ProcessOnceAsync(token).WaitAsync(token);
            Verify.That(sender.Messages.Count == before + 1, "second processor does not dispatch during active lease");
            var after = await secondContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.EventRegistrationId == registrationId, token);
            Verify.That(after.LeaseId == during.LeaseId && after.AttemptCount == 1 && after.State == PaymentDeadlineReminder.Sending,
                "second processor leaves first processor's lease unchanged");
        }
        finally
        {
            gate.Release.TrySetResult();
            await first.WaitAsync(token);
        }
        var sent = await secondContext.PaymentDeadlineReminders.AsNoTracking().SingleAsync(r => r.EventRegistrationId == registrationId, token);
        Verify.That(sent.State == PaymentDeadlineReminder.Sent && sent.AttemptCount == 1 && sent.LeaseId == null && sent.SentAtUtc?.ToUniversalTime() == UtcNow,
            "first processor finishes its reminder once sender is released");
        await PollAsync();
        Verify.That(sender.Messages.Count == before + 1, "completed competing-processor reminder remains suppressed");
        Console.WriteLine("PASS 4b: two independently scoped production processors; active lease prevents second dispatch.");
    }

    private async Task PollAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        using var timeout = new CancellationTokenSource(Bound);
        await scope.ServiceProvider.GetRequiredService<PaymentDeadlineReminderProcessor>().ProcessOnceAsync(timeout.Token).WaitAsync(timeout.Token);
    }

    private async Task<EventRegistration[]> SeedAsync(ApplicationDbContext context, int count)
    {
        var trail = new Trail { Name = "Synthetic verifier trail", Location = "Synthetic", TypicalDurationHours = 2, DistanceKm = 5, ElevationGainMeters = 100, TrailClass = 1 };
        context.Trails.Add(trail); await context.SaveChangesAsync();
        var hikingEvent = new Event { EventTitle = "Synthetic verifier event", TrailId = trail.Id, EventDate = Now.AddDays(2), Status = "Upcoming" };
        EventTrailSnapshotHelper.CaptureSnapshot(hikingEvent, trail);
        context.Events.Add(hikingEvent); await context.SaveChangesAsync();
        var participantRole = await context.Roles.Where(r => r.Name == "Participant").Select(r => r.Id).SingleAsync();
        var rows = new List<EventRegistration>();
        for (var index = 0; index < count; index++)
        {
            var key = "it-" + Guid.NewGuid().ToString("N");
            var user = new ApplicationUser { Id = key, UserName = key, NormalizedUserName = key.ToUpperInvariant(), Email = $"{key}@example.com",
                NormalizedEmail = $"{key}@example.com".ToUpperInvariant(), FirstName = "Synthetic", LastName = "Verifier", IsActive = true, EmailConfirmed = true };
            context.Users.Add(user);
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = key, RoleId = participantRole });
            var registration = new EventRegistration { User = user, EventId = hikingEvent.Id, ParticipantName = "Synthetic Verifier", Status = "Awaiting Payment",
                PaymentDeadline = Now.AddHours(12), RegisteredAt = Now.AddDays(-1) };
            context.EventRegistrations.Add(registration);
            // Persist in order: the eligible final account must follow all rejected/non-due accounts by ID.
            await context.SaveChangesAsync(); rows.Add(registration);
        }
        return rows.ToArray();
    }

    private PaymentDeadlineReminder NewReminder(EventRegistration registration) => new()
    {
        EventRegistrationId = registration.Id, PaymentDeadline = registration.PaymentDeadline!.Value, CreatedAtUtc = UtcNow, UpdatedAtUtc = UtcNow
    };

    private static async Task<int> BackendPidAsync(ApplicationDbContext context, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync(token))!;
    }
}

sealed class DisposableDatabase : IAsyncDisposable
{
    private const string Prefix = "trailguard_reminder_it_";
    public string Name { get; } = Prefix + Guid.NewGuid().ToString("N");
    private readonly NpgsqlConnection _maintenance;
    public string TestConnectionString { get; }
    private bool _created;

    private DisposableDatabase(NpgsqlConnectionStringBuilder source)
    {
        Verify.That(source.Host is "127.0.0.1" or "::1" or "localhost", "credential source must name an explicit loopback host");
        // Copy only local endpoint and credentials. Never open the source's application database,
        // inherit connection options/search paths, or load appsettings/Database:Target/Supabase settings.
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = source.Host == "localhost" ? "127.0.0.1" : source.Host,
            Port = source.Port, Username = source.Username, Password = source.Password,
            Database = "postgres", Pooling = false, Timeout = 5, CommandTimeout = 15,
            ApplicationName = Name
        };
        _maintenance = new NpgsqlConnection(builder.ConnectionString);
        builder.Database = Name;
        TestConnectionString = builder.ConnectionString;
    }

    public static DisposableDatabase Load()
    {
        var privateConnection = Environment.GetEnvironmentVariable("TRAILGUARD_REMINDER_IT_LOCAL_CONNECTION");
        if (string.IsNullOrWhiteSpace(privateConnection))
        {
            var secrets = new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true).Build();
            privateConnection = secrets.GetConnectionString("DefaultConnection");
        }
        Verify.That(!string.IsNullOrWhiteSpace(privateConnection), "existing local credentials required in User Secrets or private verifier environment variable");
        return new(new NpgsqlConnectionStringBuilder(privateConnection));
    }

    public async Task ProbeAsync()
    {
        await _maintenance.OpenAsync();
        await VerifyEndpointAsync(_maintenance, "postgres");
        Console.WriteLine("Verified loopback maintenance endpoint; no application database opened.");
    }

    public async Task CreateAsync()
    {
        Verify.That(!_created && Name.StartsWith(Prefix, StringComparison.Ordinal) && Name.Length == Prefix.Length + 32 &&
            Guid.TryParseExact(Name[Prefix.Length..], "N", out _), "database name is unique and generated by this invocation");
        await VerifyEndpointAsync(_maintenance, "postgres");
        Console.WriteLine($"Creating new disposable test database: {Name}");
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{Name}\"", _maintenance);
        await command.ExecuteNonQueryAsync(); // No IF NOT EXISTS: never reuse an existing database.
        _created = true;
    }

    public static async Task VerifyEndpointAsync(NpgsqlConnection connection, string expectedDatabase)
    {
        Verify.That(connection.Host is "127.0.0.1" or "::1", "connection host is a literal loopback address");
        Verify.That(connection.Database == expectedDatabase, "connection database equals the explicitly verified target");
        await using var command = new NpgsqlCommand("SELECT current_database(), host(inet_server_addr()), host(inet_client_addr()), current_setting('server_version'), inet_server_port()", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Verify.That(await reader.ReadAsync(), "server endpoint identity query returned a row");
        Verify.That(reader.GetString(0) == expectedDatabase && IsLoopback(reader.GetString(1)) && IsLoopback(reader.GetString(2)) && reader.GetInt32(4) == connection.Port,
            "actual database, server/client loopback addresses, and server port verified before schema or data changes");
        Console.WriteLine($"PostgreSQL {reader.GetString(3)}; verified local database {expectedDatabase}.");
    }

    private static bool IsLoopback(string value) => IPAddress.TryParse(value, out var address) &&
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_created)
            {
                // Pooling is off and all test scopes have been disposed. Drop only this invocation's database.
                await VerifyEndpointAsync(_maintenance, "postgres");
                await using var command = new NpgsqlCommand($"DROP DATABASE \"{Name}\"", _maintenance);
                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"CLEANUP PASS: dropped task-created database {Name}.");
            }
        }
        catch
        {
            Console.Error.WriteLine($"CLEANUP FAIL: task-created database {Name} may remain; no other resources were removed.");
            throw;
        }
        finally { await _maintenance.DisposeAsync(); }
    }
}

static class Verify
{
    public static int Count { get; private set; }
    public static void That(bool condition, string description)
    {
        if (!condition) throw new VerificationFailure(description);
        Count++;
    }
}
sealed class VerificationFailure(string message) : Exception(message);

sealed class ControlledClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
}

sealed class SendGate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

sealed class ControlledSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Messages { get; } = new();
    private SendGate? _next;
    public bool IsAvailable => true;
    public SendGate BlockNext() => _next = new();
    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Enqueue(message);
        var gate = Interlocked.Exchange(ref _next, null);
        if (gate is not null)
        {
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
        }
        return EmailDeliveryResult.Delivered(); // Memory only; no SMTP implementation is registered.
    }
}

sealed class VerificationEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "ReminderPostgresVerification";
    public string WebRootPath { get; set; } = ".";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
