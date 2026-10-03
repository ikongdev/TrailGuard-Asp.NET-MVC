using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TrailGuard.Controllers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

// Called only inside the existing --run disposable-database lifecycle. No storage HTTP client exists here.
static class StorageConcurrencyScenarios
{
    private const string FixturePassword = "Storage-fixture-only-42!";

    public static async Task RunAsync(string connectionString, string root)
    {
        await TrailCoversAsync(connectionString, root);
        await ProfilesAsync(connectionString, root);
        await ReceiptsAsync(connectionString, root);
    }

    private static async Task TrailCoversAsync(string connectionString, string root)
    {
        var memory = new MemoryUploadStorage();
        var bytes = Png();
        var historical = await memory.UploadAsync(UploadCategory.Trails, bytes, ".png");
        var seed = await SeedAsync(connectionString, "covers", historical);
        var firstProbe = new StorageTransactionProbe(holdCommit: true);
        var secondProbe = new StorageTransactionProbe();
        var command = new StorageCommandProbe("FOR UPDATE");
        await using var firstProvider = HarnessDbContextFactory.CreateProvider(connectionString, firstProbe);
        await using var secondProvider = HarnessDbContextFactory.CreateProvider(connectionString, secondProbe, command);
        await using var first = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        await using var second = secondProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        var firstStorage = new RequestUploadStorage(memory, firstProbe);
        var secondStorage = new RequestUploadStorage(memory, secondProbe);
        var winner = new TrailController(first, new TestEnvironment(root), NullLogger<TrailController>.Instance, firstStorage);
        var loser = new TrailController(second, new TestEnvironment(root), NullLogger<TrailController>.Instance, secondStorage);
        Configure(winner, seed.UserId, "Organizer");
        Configure(loser, seed.UserId, "Organizer");
        Task<IActionResult>? firstTask = null, secondTask = null;
        try
        {
            firstTask = winner.EditTrail(seed.TrailId, TrailInput("winner"), ["Rocky"], Form(bytes, "cover.png"), null);
            secondTask = loser.EditTrail(seed.TrailId, TrailInput("loser"), ["Rocky"], Form(bytes, "cover.png"), null);
            await StorageStage.WaitForGateAsync(Task.WhenAll(firstStorage.Uploaded.Task, secondStorage.Uploaded.Task), "Trail covers: both uploads",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            Require(first.ChangeTracker.Entries<Trail>().Single().Entity.ThumbnailUrl == historical
                && second.ChangeTracker.Entries<Trail>().Single().Entity.ThumbnailUrl == historical,
                "Both cover requests must capture the same original reference.");

            firstStorage.Release();
            var blocker = await StorageStage.WaitForGateAsync(firstProbe.Committing.Task, "Trail covers: winner before commit",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            secondStorage.Release();
            var waiter = await StorageStage.WaitForGateAsync(command.Executing.Task, "Trail covers: loser SQL dispatch",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            await WaitForBlockedAsync(connectionString, waiter, blocker, "Trail covers: PostgreSQL lock wait",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            firstProbe.Release();
            await StorageStage.WaitForCompletionAsync("Trail covers: action completion",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));

            Require(winner.TempData.ContainsKey("Success") && loser.TempData.ContainsKey("Error"), "Cover race did not reject exactly the stale request.");
            Require(firstProbe.Commits == 1 && secondProbe.Commits == 0 && secondProbe.Rollbacks == 1,
                "Cover loser must receive a confirmed database rollback.");
            Require(secondStorage.Deletes.SequenceEqual([new StorageDeletion(secondStorage.Reference!, 0, 1)]),
                "Cover compensation must delete only the loser's upload after confirmed rollback.");
            Require(firstStorage.Deletes.IsEmpty, "Cover replacement deleted a historical cover.");
            await using var verify = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
            var trail = await verify.Trails.SingleAsync(t => t.Id == seed.TrailId);
            var evt = await verify.Events.SingleAsync(e => e.Id == seed.EventId);
            Require(trail.ThumbnailUrl == firstStorage.Reference && trail.Name == "Storage winner", "The stale trail write overwrote the winner.");
            Require(evt.TrailThumbnailUrlSnapshot == historical && evt.Status == "Completed", "Historical event cover changed.");
            Require((await memory.ReadPublicLocalAsync(trail.ThumbnailUrl!))?.SequenceEqual(bytes) == true
                && (await memory.ReadPublicLocalAsync(historical))?.SequenceEqual(bytes) == true
                && !memory.Contains(secondStorage.Reference!), "Winning/historical cover readability or losing-upload cleanup failed.");
            Console.WriteLine("PASS: real trail row-lock contention; stale cover rollback/compensation; winning and historical covers retained.");
        }
        finally
        {
            firstStorage.Release(); secondStorage.Release(); firstProbe.Release();
            await DrainAsync("Trail covers: cleanup drain", new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
        }
    }

    private static async Task ProfilesAsync(string connectionString, string root)
    {
        var memory = new MemoryUploadStorage();
        var bytes = Png();
        var original = await memory.UploadAsync(UploadCategory.Profiles, bytes, ".png");
        var seed = await SeedAsync(connectionString, "profiles", profile: original);
        var firstProbe = new StorageTransactionProbe(holdCommit: true);
        var secondProbe = new StorageTransactionProbe();
        await using var firstProvider = HarnessDbContextFactory.CreateProvider(connectionString, firstProbe);
        await using var firstScope = firstProvider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var command = StorageCommandProbe.ForIdentityUserUpdate(first);
        await using var secondProvider = HarnessDbContextFactory.CreateProvider(connectionString, secondProbe, command);
        await using var secondScope = secondProvider.CreateAsyncScope();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstUsers = firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var secondUsers = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var firstStorage = new RequestUploadStorage(memory, firstProbe);
        var secondStorage = new RequestUploadStorage(memory, secondProbe);
        // Only cookie refresh is doubled. User/password lookup, validation, concurrency stamp and EF save are real Identity operations.
        var firstSignIn = new NoCookieSignInManager(firstUsers);
        var secondSignIn = new NoCookieSignInManager(secondUsers);
        var clock = new PhilippineClock(TimeProvider.System);
        var winner = new SettingsController(firstUsers, firstSignIn, new TestEnvironment(root), NullLogger<SettingsController>.Instance, clock, firstStorage, first);
        var loser = new SettingsController(secondUsers, secondSignIn, new TestEnvironment(root), NullLogger<SettingsController>.Instance, clock, secondStorage, second);
        Configure(winner, seed.UserId, "Participant"); Configure(loser, seed.UserId, "Participant");
        Task<IActionResult>? firstTask = null, secondTask = null;
        try
        {
            firstTask = winner.UpdateProfile(ProfileInput(seed.Email, "Winner", bytes), FixturePassword);
            secondTask = loser.UpdateProfile(ProfileInput(seed.Email, "Loser", bytes), FixturePassword);
            await StorageStage.WaitForGateAsync(Task.WhenAll(firstStorage.Uploaded.Task, secondStorage.Uploaded.Task), "Profiles: both uploads",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            var originalStamp = first.ChangeTracker.Entries<ApplicationUser>().Single().Property(u => u.ConcurrencyStamp).OriginalValue;
            Require(!string.IsNullOrEmpty(originalStamp)
                && second.ChangeTracker.Entries<ApplicationUser>().Single().Property(u => u.ConcurrencyStamp).OriginalValue == originalStamp,
                "Profile requests must load the same real Identity concurrency stamp.");
            firstStorage.Release();
            var blocker = await StorageStage.WaitForGateAsync(firstProbe.Committing.Task, "Profiles: winner before commit",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            secondStorage.Release();
            var waiter = await StorageStage.WaitForGateAsync(command.Executing.Task, "Profiles: loser SQL dispatch",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            await WaitForBlockedAsync(connectionString, waiter, blocker, "Profiles: PostgreSQL lock wait",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            firstProbe.Release();
            await StorageStage.WaitForCompletionAsync("Profiles: action completion",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));

            Require(winner.TempData.ContainsKey("Success")
                && loser.TempData["Error"]?.ToString() == new IdentityErrorDescriber().ConcurrencyFailure().Description
                && command.UsedConcurrencyPredicate, "Profile loser was not rejected by actual Identity EF concurrency persistence.");
            Require(firstProbe.Commits == 1 && secondProbe.Commits == 0 && secondProbe.Rollbacks == 1, "Profile transaction outcomes incorrect.");
            Require(secondStorage.Deletes.SequenceEqual([new StorageDeletion(secondStorage.Reference!, 0, 1)])
                && firstStorage.Deletes.SequenceEqual([new StorageDeletion(original, 1, 0)]),
                "Profile cleanup must compensate only the loser and delete the old image only after the winner commits.");
            await using var verify = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
            var user = await verify.Users.SingleAsync(u => u.Id == seed.UserId);
            Require(user.ProfilePictureUrl == firstStorage.Reference && user.FirstName == "Winner" && user.ConcurrencyStamp != originalStamp,
                "The stale profile overwrote the winning reference, details or stamp.");
            Require((await memory.ReadPublicLocalAsync(user.ProfilePictureUrl!))?.SequenceEqual(bytes) == true
                && !memory.Contains(secondStorage.Reference!) && !memory.Contains(original), "Profile storage survivor set is incorrect.");
            Require(firstSignIn.RefreshCalls == 1 && secondSignIn.RefreshCalls == 0, "Rejected profile refreshed its cookie.");
            Console.WriteLine("PASS: actual Identity EF stamp conflict under contention; confirmed rollback compensates only losing profile upload.");
        }
        finally
        {
            firstStorage.Release(); secondStorage.Release(); firstProbe.Release();
            await DrainAsync("Profiles: cleanup drain", new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
        }
    }

    private static async Task ReceiptsAsync(string connectionString, string root)
    {
        var memory = new MemoryUploadStorage();
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 isolated fixture");
        var original = await memory.UploadAsync(UploadCategory.Receipts, bytes, ".pdf");
        var seed = await SeedAsync(connectionString, "receipts");
        var firstProbe = new StorageTransactionProbe();
        var secondProbe = new StorageTransactionProbe();
        var command = new StorageCommandProbe("pg_advisory_xact_lock");
        await using var firstProvider = HarnessDbContextFactory.CreateProvider(connectionString, firstProbe);
        await using var secondProvider = HarnessDbContextFactory.CreateProvider(connectionString, secondProbe, command);
        int registrationId;
        await using (var setup = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext())
        {
            var row = new EventRegistration { EventId = seed.EventId, UserId = seed.UserId, ParticipantName = "Storage fixture",
                Status = "Awaiting Payment", PaymentDeadline = DateTime.Now.AddDays(1), PaymentReceiptUrl = original };
            setup.EventRegistrations.Add(row); await setup.SaveChangesAsync(); registrationId = row.Id;
        }
        firstProbe.Commits = 0; firstProbe.Rollbacks = 0; // Exclude fixture seeding from action outcomes.
        await using var first = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        await using var second = secondProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        var firstStorage = new RequestUploadStorage(memory, firstProbe);
        var secondStorage = new RequestUploadStorage(memory, secondProbe);
        var winner = new RegistrationController(first, new TestEnvironment(root), storage: firstStorage);
        var loser = new RegistrationController(second, new TestEnvironment(root), storage: secondStorage);
        Configure(winner, seed.UserId, "Participant"); Configure(loser, seed.UserId, "Participant");
        Task<IActionResult>? firstTask = null, secondTask = null;
        try
        {
            firstTask = winner.UpdatePaymentReceipt(registrationId, Form(bytes, "receipt.pdf"));
            await StorageStage.WaitForGateAsync(firstStorage.Uploaded.Task, "Receipts: winner upload", new StorageRequest(firstTask, winner, "winner")); // Winner owns the event lock, before its DB save.
            var blocker = ((NpgsqlConnection)first.Database.GetDbConnection()).ProcessID;
            secondTask = loser.UpdatePaymentReceipt(registrationId, Form(bytes, "receipt.pdf"));
            var waiter = await StorageStage.WaitForGateAsync(command.Executing.Task, "Receipts: loser SQL dispatch",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser")); // Loser passed the initial Awaiting Payment read.
            Require(command.LockKey == ParticipantEventWorkflowLock.DeriveEventCapacityKey(seed.EventId), "Receipt used the wrong event lock.");
            await WaitForBlockedAsync(connectionString, waiter, blocker, "Receipts: PostgreSQL lock wait",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            firstStorage.Release();
            await StorageStage.WaitForCompletionAsync("Receipts: action completion",
                new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
            Require(JsonSuccess(await firstTask) && !JsonSuccess(await secondTask), "Receipt race must allow exactly one transition.");
            Require(JsonSerializer.Serialize(((JsonResult)await secondTask).Value).Contains("Payment receipt can only be uploaded while"),
                "Receipt loser must fail the authoritative source-status reload, not an unrelated error.");
            Require(firstProbe.Commits == 1 && secondProbe.Commits == 0 && !secondStorage.Uploaded.Task.IsCompleted
                && secondStorage.Deletes.IsEmpty, "Stale receipt uploaded, committed or deleted storage.");
            Require(firstStorage.Deletes.SequenceEqual([new StorageDeletion(original, 1, 0)]), "Receipt replaced-file cleanup did not follow commit.");
            await using var verify = firstProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
            var registration = await verify.EventRegistrations.SingleAsync(r => r.Id == registrationId);
            Require(registration.Status == "For Payment Verification" && registration.PaymentReceiptUrl == firstStorage.Reference
                && registration.PaymentReceiptUploadedAt.HasValue && !registration.IsPaid, "Receipt winner was overwritten or transitioned incorrectly.");
            Require((await memory.ReadDocumentAsync(RegistrationDocumentKind.Receipt, registration.PaymentReceiptUrl))?.Bytes.SequenceEqual(bytes) == true
                && !memory.Contains(original), "Winning receipt was deleted or unreadable.");
            Require(await verify.EventRegistrations.CountAsync(r => r.EventId == seed.EventId) == 1, "Receipt race duplicated registration.");
            Console.WriteLine("PASS: real event advisory-lock contention; receipt loser rejected after locked status reload without upload/deletion.");
        }
        finally
        {
            firstStorage.Release(); secondStorage.Release();
            await DrainAsync("Receipts: cleanup drain", new StorageRequest(firstTask, winner, "winner"), new StorageRequest(secondTask, loser, "loser"));
        }
    }

    // Observe PostgreSQL's actual lock wait graph, not merely scheduling or elapsed time.
    private static async Task WaitForBlockedAsync(string connectionString, int waiter, int blocker, string stage, params StorageRequest[] requests)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task ObserveAsync()
        {
            await using var observer = new NpgsqlConnection(connectionString);
            await observer.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiter))", observer);
            command.Parameters.AddWithValue("blocker", blocker); command.Parameters.AddWithValue("waiter", waiter);
            while (await command.ExecuteScalarAsync(timeout.Token) is not true)
                await Task.Delay(20, timeout.Token);
        }
        var observation = ObserveAsync();
        try { await StorageStage.WaitForGateAsync(observation, stage, requests); }
        finally
        {
            await timeout.CancelAsync();
            // Observe cancellation/failure after an early action result without masking its diagnostic.
            try { await observation; } catch { }
        }
    }

    private static async Task DrainAsync(string stage, params StorageRequest[] requests)
    {
        try { await StorageStage.WaitForCompletionAsync(stage, requests); }
        // Successful scenarios already awaited both actions; preserve the original failing stage.
        catch (StorageStageException exception) { Console.Error.WriteLine(exception.Message); }
    }

    private static async Task<StorageSeed> SeedAsync(string connectionString, string suffix, string? cover = null, string? profile = null)
    {
        await using var provider = HarnessDbContextFactory.CreateProvider(connectionString);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Participant")) Require((await roles.CreateAsync(new IdentityRole("Participant"))).Succeeded, "Role fixture creation failed.");
        var email = $"storage-{suffix}@example.test";
        var user = new ApplicationUser { Id = "storage-" + suffix, UserName = email, Email = email, FirstName = "Storage", LastName = "Fixture",
            ProfilePictureUrl = profile, Birthday = new DateOnly(1995, 1, 1), Gender = ParticipantDemographics.PreferNotToSay };
        Require((await users.CreateAsync(user, FixturePassword)).Succeeded, "Identity fixture creation failed.");
        Require((await users.AddToRoleAsync(user, "Participant")).Succeeded, "Identity fixture role failed.");
        var trail = TrailInput(suffix); trail.ThumbnailUrl = cover;
        context.Trails.Add(trail); await context.SaveChangesAsync();
        var evt = new Event { TrailId = trail.Id, EventTitle = "Storage " + suffix, Description = "Fixture", EventDate = DateTime.Today.AddDays(7),
            EventTime = TimeSpan.FromHours(6), Location = "Local", Difficulty = "Moderate", Capacity = 20, Status = cover == null ? "Upcoming" : "Completed",
            OrganizerId = user.Id, OrganizedBy = "Storage Fixture", PickupPoints = "Main gate", TrailNameSnapshot = trail.Name,
            TrailDistanceKmSnapshot = 8, TrailDurationHoursSnapshot = 5, TrailElevationGainMetersSnapshot = 600, TrailTerrainSnapshot = "Rocky",
            TrailClassSnapshot = 3, DifficultyScoreSnapshot = 1m, EstimatedDuration = 5, TrailThumbnailUrlSnapshot = cover };
        context.Events.Add(evt); await context.SaveChangesAsync();
        return new(trail.Id, evt.Id, user.Id, email);
    }

    private static Trail TrailInput(string suffix) => new() { Name = "Storage " + suffix, Location = "Local", DistanceKm = 8,
        TypicalDurationHours = 5, ElevationGainMeters = 600, Terrain = "Rocky", TrailClass = 3, Description = "Fixture", IsActive = true };
    private static UpdateProfileViewModel ProfileInput(string email, string name, byte[] bytes) => new() { FirstName = name, LastName = "Fixture", Email = email,
        Birthday = new DateOnly(1995, 1, 1), Gender = ParticipantDemographics.PreferNotToSay, ProfileImage = Form(bytes, "profile.png") };
    private static IFormFile Form(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "upload", name);
    private static byte[] Png() { using var image = new Image<Rgba32>(2, 2); using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray(); }
    private static bool JsonSuccess(IActionResult result) => JsonSerializer.SerializeToElement(((JsonResult)result).Value).GetProperty("success").GetBoolean();
    private static void Configure(Controller controller, string userId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)], "storage-it")) };
        controller.ControllerContext = new() { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new MemoryTempDataProvider());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record StorageSeed(int TrailId, int EventId, string UserId, string Email);
}

sealed record StorageRequest(Task<IActionResult>? Action, Controller Controller, string Name);

sealed class StorageStageException(string message) : InvalidOperationException(message);

static class StorageStage
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    public static async Task WaitForGateAsync(Task gate, string stage, params StorageRequest[] requests)
    {
        try
        {
            await Task.WhenAny(requests.Select(r => r.Action).OfType<Task>().Prepend(gate)).WaitAsync(Limit);
            if (!gate.IsCompleted)
                throw new StorageStageException($"{stage}: action finished before the expected gate. {Describe(requests)}");
            await gate;
        }
        catch (StorageStageException) { throw; }
        catch (TimeoutException) { throw new StorageStageException($"{stage}: gate timed out. {Describe(requests)}"); }
        catch (OperationCanceledException) { throw new StorageStageException($"{stage}: gate canceled or its deadline expired. {Describe(requests)}"); }
        catch (Exception exception) { throw new StorageStageException($"{stage}: gate failed ({exception.GetType().Name}). {Describe(requests)}"); }
    }

    public static async Task<T> WaitForGateAsync<T>(Task<T> gate, string stage, params StorageRequest[] requests)
    {
        await WaitForGateAsync((Task)gate, stage, requests);
        return await gate;
    }

    public static async Task WaitForCompletionAsync(string stage, params StorageRequest[] requests)
    {
        try { await Task.WhenAll(requests.Select(r => r.Action).OfType<Task>()).WaitAsync(Limit); }
        catch (TimeoutException) { throw new StorageStageException($"{stage}: actions timed out. {Describe(requests)}"); }
        catch (Exception exception) { throw new StorageStageException($"{stage}: action failed ({exception.GetType().Name}). {Describe(requests)}"); }
    }

    private static string Describe(IEnumerable<StorageRequest> requests) => string.Join("; ", requests.Select(request =>
    {
        var action = request.Action;
        if (action == null) return $"{request.Name}=not started";
        if (action.IsFaulted) return $"{request.Name}=faulted ({string.Join(", ", action.Exception!.Flatten().InnerExceptions.Select(e => e.GetType().Name))})";
        if (action.IsCanceled) return $"{request.Name}=canceled";
        if (!action.IsCompleted) return $"{request.Name}=running";
        var result = action.GetAwaiter().GetResult();
        var summary = result.GetType().Name;
        if (result is JsonResult json)
        {
            // Read only the known action-result fields; never dump arbitrary JSON or redirect values.
            var success = json.Value?.GetType().GetProperty("success")?.GetValue(json.Value);
            var message = json.Value?.GetType().GetProperty("message")?.GetValue(json.Value) as string;
            summary += $" success={(success is bool value ? value.ToString() : "unspecified")} message={SafeMessage(message)}";
        }
        return $"{request.Name}={summary}, error={SafeMessage(request.Controller.TempData.Peek("Error") as string)}, success={request.Controller.TempData.ContainsKey("Success")}";
    }));

    // Only fixed, known controller/Identity messages can be emitted. Unknown validation/provider text may contain secrets.
    private static string SafeMessage(string? message) => message switch
    {
        null or "" => "none",
        "Current password is incorrect." or
        "Unable to save your profile. Please reload and try again." or
        "Unable to save the trail. It may have changed. Please reload and try again." or
        "Trail not found." or
        "This trail is deactivated and cannot be edited. Reactivate it first." or
        "Registration not found" or
        "Payment receipt can only be uploaded while your registration is awaiting payment." or
        "Payment receipt uploaded. Waiting for organizer verification." or
        "Unable to save the receipt. Please review the registration status before trying again." or
        "Payment receipt must be a JPG, PNG, WEBP, or PDF file, up to 5 MiB." or
        "No file uploaded." => message,
        _ when message == new IdentityErrorDescriber().ConcurrencyFailure().Description => message,
        _ => "unrecognized message (text withheld)"
    };

    public static async Task VerifyDatabaseFreeAsync()
    {
        using var provider = HarnessDbContextFactory.CreateProvider("Host=127.0.0.1;Port=1;Database=unused");
        using var context = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        var probe = StorageCommandProbe.ForIdentityUserUpdate(context);
        Check(context.Model.FindEntityType(typeof(ApplicationUser))!.GetTableName() == "Users", "Unexpected Identity table mapping.");
        Check(probe.Matches("UPDATE \"Users\" SET \"FirstName\" = @p0 WHERE \"Id\" = @p1 AND \"ConcurrencyStamp\" = @p2;"), "Mapped user update was not detected.");
        foreach (var sql in new[] { "UPDATE \"AspNetUsers\" SET", "UPDATE \"UsersAudit\" SET", "SELECT * FROM \"Users\"", "UPDATE \"Events\" SET" })
            Check(!probe.Matches(sql), "An unrelated command matched the Identity update probe.");

        var controller = new DiagnosticController();
        var http = new DefaultHttpContext();
        controller.TempData = new TempDataDictionary(http, new MemoryTempDataProvider());
        controller.TempData["Error"] = "Current password is incorrect.";
        var pendingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = Task.FromResult<IActionResult>(new RedirectToActionResult("Index", null, null));
        var pendingAction = new TaskCompletionSource<IActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var message = await FailureAsync(() => WaitForGateAsync(pendingGate.Task, "Profiles: winner upload", new StorageRequest(completed, controller, "winner")));
        Check(message.Contains("Profiles: winner upload") && message.Contains("RedirectToActionResult") && message.Contains("Current password is incorrect."), "Early redirect diagnostics were lost.");
        Check(!pendingGate.Task.IsCompleted && controller.TempData.Peek("Error") != null, "Diagnostics changed gate or action feedback.");
        var json = Task.FromResult<IActionResult>(new JsonResult(new { success = false, message = "Registration not found" }));
        message = await FailureAsync(() => WaitForGateAsync(pendingGate.Task, "Receipts: loser SQL dispatch", new StorageRequest(json, controller, "loser")));
        Check(message.Contains("success=False") && message.Contains("Registration not found"), "Early JSON result diagnostics were lost.");
        const string sensitive = "synthetic-sensitive-sentinel";
        controller.TempData["Error"] = sensitive;
        var faulted = Task.FromException<IActionResult>(new IOException(sensitive));
        message = await FailureAsync(() => WaitForGateAsync(pendingGate.Task, "Profiles: loser SQL dispatch", new StorageRequest(faulted, controller, "loser")));
        Check(message.Contains("IOException") && !message.Contains(sensitive), "Fault diagnostics exposed raw exception text.");
        json = Task.FromResult<IActionResult>(new JsonResult(new { success = false, message = sensitive, password = sensitive }));
        message = await FailureAsync(() => WaitForGateAsync(pendingGate.Task, "Receipts: upload", new StorageRequest(json, controller, "loser")));
        Check(message.Contains("text withheld") && !message.Contains(sensitive), "Result diagnostics exposed unapproved data.");
        message = await FailureAsync(() => WaitForGateAsync(Task.FromException(new TimeoutException(sensitive)), "Profiles: winner before commit", new StorageRequest(pendingAction.Task, controller, "winner")));
        Check(message.Contains("Profiles: winner before commit: gate timed out") && !message.Contains(sensitive), "Timeout lacks safe stage context.");
        message = await FailureAsync(() => WaitForGateAsync(Task.FromCanceled(new CancellationToken(true)), "Profiles: PostgreSQL lock wait", new StorageRequest(pendingAction.Task, controller, "loser")));
        Check(message.Contains("Profiles: PostgreSQL lock wait") && message.Contains("deadline"), "Lock deadline lacks stage context.");
        message = await FailureAsync(() => WaitForCompletionAsync("Profiles: cleanup drain", new StorageRequest(faulted, controller, "loser")));
        Check(message.Contains("Profiles: cleanup drain") && message.Contains("IOException") && !message.Contains(sensitive), "Completion diagnostics are unsafe.");
        Check(await WaitForGateAsync(Task.FromResult(7), "already reached", new StorageRequest(completed, controller, "winner")) == 7, "Reached gate lost its result.");
        Console.WriteLine("PASS: 15 database-free storage marker/diagnostic checks; no database connection opened.");
    }

    private static async Task<string> FailureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (StorageStageException exception) { return exception.Message; }
        throw new InvalidOperationException("Expected a stage-specific verification failure.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class DiagnosticController : Controller;
}

sealed class StorageTransactionProbe(bool holdCommit = false) : DbTransactionInterceptor
{
    public TaskCompletionSource<int> Committing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Commits, Rollbacks;
    public void Release() => release.TrySetResult();
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Committing.TrySetResult(((NpgsqlConnection)transaction.Connection!).ProcessID);
        if (holdCommit) await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return result;
    }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref Commits); return Task.CompletedTask; }
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref Rollbacks); return Task.CompletedTask; }
}

sealed class StorageCommandProbe(string marker) : DbCommandInterceptor
{
    public static StorageCommandProbe ForIdentityUserUpdate(ApplicationDbContext context)
    {
        var user = context.Model.FindEntityType(typeof(ApplicationUser))
            ?? throw new InvalidOperationException("Identity user mapping is missing.");
        var table = user.GetTableName() ?? throw new InvalidOperationException("Identity user table is missing.");
        var identifier = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, user.GetSchema());
        return new StorageCommandProbe($"UPDATE {identifier} SET");
    }
    public bool Matches(string sql) => sql.Contains(marker, StringComparison.Ordinal);
    public TaskCompletionSource<int> Executing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool UsedConcurrencyPredicate { get; private set; }
    public long? LockKey { get; private set; }
    private void Observe(DbCommand command)
    {
        if (!Matches(command.CommandText)) return;
        UsedConcurrencyPredicate = command.CommandText.Contains("AND \"ConcurrencyStamp\" =", StringComparison.Ordinal);
        if (marker == "pg_advisory_xact_lock") LockKey = Convert.ToInt64(command.Parameters[0].Value);
        Executing.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Observe(command); return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Observe(command); return ValueTask.FromResult(result); }
}

sealed record StorageDeletion(string Reference, int Commits, int Rollbacks);

sealed class RequestUploadStorage(MemoryUploadStorage storage, StorageTransactionProbe transaction) : IUploadStorage
{
    public TaskCompletionSource Uploaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Reference { get; private set; }
    public ConcurrentQueue<StorageDeletion> Deletes { get; } = new();
    public void Release() => release.TrySetResult();
    public async Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default)
    {
        Reference = await storage.UploadAsync(category, bytes, extension, cancellationToken);
        Uploaded.TrySetResult();
        await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return Reference;
    }
    public Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default)
    {
        Deletes.Enqueue(new(reference, transaction.Commits, transaction.Rollbacks));
        return storage.DeleteOwnedAsync(category, reference, cancellationToken);
    }
    public Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default)
        => storage.ReadDocumentAsync(kind, reference, cancellationToken);
    public Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default)
        => storage.ReadPublicLocalAsync(reference, cancellationToken);
}

sealed class MemoryUploadStorage : IUploadStorage
{
    private readonly ConcurrentDictionary<string, (UploadCategory Category, byte[] Bytes)> objects = new();
    public bool Contains(string reference) => objects.ContainsKey(reference);
    public Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default)
    {
        var reference = $"it-storage/{category}/{Guid.NewGuid():N}{extension}";
        if (!objects.TryAdd(reference, (category, bytes.ToArray()))) throw new InvalidOperationException("Fixture reference collision.");
        return Task.FromResult(reference);
    }
    public Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default)
    {
        if (objects.TryGetValue(reference, out var item) && item.Category == category) objects.TryRemove(reference, out _);
        return Task.CompletedTask;
    }
    public Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default)
        => Task.FromResult(objects.TryGetValue(reference, out var item) && item.Category is UploadCategory.Trails or UploadCategory.Profiles ? item.Bytes.ToArray() : null);
    public Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default)
        => Task.FromResult<StoredDocument?>(reference != null && objects.TryGetValue(reference, out var item)
            && item.Category == (kind == RegistrationDocumentKind.Receipt ? UploadCategory.Receipts : UploadCategory.MedicalClearances)
                ? new(item.Bytes.ToArray(), VerifiedFileType.Pdf) : null);
}

sealed class NoCookieSignInManager(UserManager<ApplicationUser> users) : SignInManager<ApplicationUser>(users,
    new HttpContextAccessor(), new UserClaimsPrincipalFactory<ApplicationUser>(users, OptionsFactory.Create(new IdentityOptions())),
    OptionsFactory.Create(new IdentityOptions()), NullLogger<SignInManager<ApplicationUser>>.Instance,
    new AuthenticationSchemeProvider(OptionsFactory.Create(new AuthenticationOptions())), new DefaultUserConfirmation<ApplicationUser>())
{
    public int RefreshCalls { get; private set; }
    public override Task RefreshSignInAsync(ApplicationUser user) { RefreshCalls++; return Task.CompletedTask; }
}
