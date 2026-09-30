using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TrailGuard.Controllers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

// Keep the harness's Npgsql model conventions aligned with Program.cs before EF builds its model.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var run = args.SequenceEqual(["--run"]);
var modelCheck = args.SequenceEqual(["--model-check"]);
string fixture;
try
{
    fixture = FixtureLoader.LoadAndVerify();
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"Fixture verification failed: {exception.Message}");
    Environment.ExitCode = 2;
    return;
}

if (!run)
{
    if (ParticipantEventWorkflowLock.DeriveUserKey("participant-a") != ParticipantEventWorkflowLock.DeriveUserKey("participant-a")
        || ParticipantEventWorkflowLock.DeriveUserKey("participant-a") == ParticipantEventWorkflowLock.DeriveUserKey("participant-b"))
        throw new InvalidOperationException("The workflow lock key derivation is not deterministic.");
    if (ParticipantEventWorkflowLock.DeriveEventCapacityKey(1) != ParticipantEventWorkflowLock.DeriveEventCapacityKey(1)
        || ParticipantEventWorkflowLock.DeriveEventCapacityKey(1) == ParticipantEventWorkflowLock.DeriveEventCapacityKey(2))
        throw new InvalidOperationException("The event-capacity lock key derivation is not deterministic.");
    if (modelCheck)
    {
        try
        {
            ModelAgreement.VerifyHarnessContext();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Harness model agreement check failed: {exception.Message}");
            Environment.ExitCode = 2;
            return;
        }
    }

    Console.WriteLine("Database-free mode: no connection was opened. Re-run with --run and the three required TRAILGUARD_IT variables to execute isolated PostgreSQL tests.");
    return;
}

var settings = IntegrationSettings.Load();
var runner = new IntegrationRunner(settings, fixture);
Environment.ExitCode = await runner.RunAsync();

sealed class IntegrationSettings
{
    public required string AdminConnectionString { get; init; }
    public required string AppConnectionString { get; init; }

    public static IntegrationSettings Load()
    {
        var admin = Environment.GetEnvironmentVariable("TRAILGUARD_IT_ADMIN_CONNECTION");
        var app = Environment.GetEnvironmentVariable("TRAILGUARD_IT_APP_CONNECTION");
        if (Environment.GetEnvironmentVariable(IntegrationSafety.DestructiveOptIn) != IntegrationSafety.TestDatabase)
            throw new InvalidOperationException($"Set {IntegrationSafety.DestructiveOptIn} exactly to '{IntegrationSafety.TestDatabase}'.");
        if (string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(app))
            throw new InvalidOperationException("Both TRAILGUARD_IT_ADMIN_CONNECTION and TRAILGUARD_IT_APP_CONNECTION are required.");

        var adminBuilder = new NpgsqlConnectionStringBuilder(admin);
        var appBuilder = new NpgsqlConnectionStringBuilder(app);
        ValidateLoopback(adminBuilder, "maintenance");
        ValidateLoopback(appBuilder, "application");
        if (!string.Equals(adminBuilder.Database, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException("The maintenance connection must target database 'postgres'.");
        if (!string.Equals(appBuilder.Database, IntegrationSafety.TestDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException($"The application connection must target '{IntegrationSafety.TestDatabase}'.");
        if (!string.Equals(adminBuilder.Host, appBuilder.Host, StringComparison.OrdinalIgnoreCase)
            || adminBuilder.Port != appBuilder.Port)
            throw new InvalidOperationException("Maintenance and application connections must use the same local PostgreSQL server.");

        // Bound blocked advisory-lock/command waits in the opt-in runner; never print either connection string.
        appBuilder.Timeout = 5;
        appBuilder.CommandTimeout = 10;
        appBuilder.Pooling = false;
        return new() { AdminConnectionString = adminBuilder.ConnectionString, AppConnectionString = appBuilder.ConnectionString };
    }

    private static void ValidateLoopback(NpgsqlConnectionStringBuilder builder, string role)
    {
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException($"The {role} connection host must be an explicit loopback host.");
        if (string.Equals(builder.Database, "trailguard_db", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The integration harness never permits trailguard_db.");
    }
}

static class IntegrationSafety
{
    public const string TestDatabase = "trailguard_assessment_it";
    public const string DestructiveOptIn = "TRAILGUARD_IT_ALLOW_DESTRUCTIVE";
}

static class FixtureLoader
{
    private const string FixtureName = "adapter-recorded-example.json";

    public static string LoadAndVerify()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"Required recorded adapter fixture is missing from the build output at '{outputPath}'. Rebuild the verification project.");

        byte[] outputBytes;
        try
        {
            outputBytes = File.ReadAllBytes(outputPath);
            using var document = JsonDocument.Parse(outputBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The recorded adapter fixture must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The recorded adapter fixture in the build output is not valid JSON.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("The recorded adapter fixture in the build output could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("The recorded adapter fixture in the build output could not be read.", exception);
        }

        var canonicalPath = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Verification", "TrailGuardV2AdapterVerification", "Fixtures", FixtureName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TrailGuardV2AdapterVerification", "Fixtures", FixtureName)
            }
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
        if (canonicalPath == null)
            throw new InvalidOperationException("The canonical recorded adapter fixture could not be located from the workspace or verification output.");

        byte[] canonicalBytes;
        try
        {
            canonicalBytes = File.ReadAllBytes(canonicalPath);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("The canonical recorded adapter fixture could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("The canonical recorded adapter fixture could not be read.", exception);
        }
        if (!outputBytes.AsSpan().SequenceEqual(canonicalBytes))
            throw new InvalidOperationException("The build-output adapter fixture does not match the canonical fixture byte-for-byte. Rebuild the verification project.");

        return Encoding.UTF8.GetString(outputBytes);
    }
}

static class ModelAgreement
{
    public static void VerifyHarnessContext()
    {
        using var services = HarnessDbContextFactory.CreateProvider(
            "Host=127.0.0.1;Port=5432;Database=trailguard_assessment_it;Username=not-used;Password=not-used");
        using var context = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
        var migrations = context.GetService<IMigrationsAssembly>();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var snapshot = migrations.ModelSnapshot
            ?? throw new InvalidOperationException("ApplicationDbContext has no model snapshot.");
        var latest = migrations.Migrations
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .LastOrDefault();
        if (latest.Value is null)
            throw new InvalidOperationException("ApplicationDbContext has no migrations.");

        var snapshotModel = initializer.Initialize(snapshot.Model, designTime: true);
        var targetMigration = (Migration)Activator.CreateInstance(latest.Value)!;
        var targetModel = initializer.Initialize(targetMigration.TargetModel, designTime: true);
        var snapshotDifferences = differ.GetDifferences(
            snapshotModel.GetRelationalModel(), designTimeModel.GetRelationalModel());
        var targetDifferences = differ.GetDifferences(
            snapshotModel.GetRelationalModel(), targetModel.GetRelationalModel());
        if (snapshotDifferences.Count != 0 || targetDifferences.Count != 0)
        {
            var operations = snapshotDifferences.Concat(targetDifferences)
                .Select(DescribeOperation)
                .Distinct(StringComparer.Ordinal);
            throw new InvalidOperationException($"Harness model differs from migration metadata: {string.Join(", ", operations)}.");
        }

        Console.WriteLine($"Harness model agreement: snapshot and {latest.Key} target model have zero differences.");
    }

    private static string DescribeOperation(MigrationOperation operation) => operation switch
    {
        AlterColumnOperation alter => $"AlterColumn {alter.Table}.{alter.Name} ({alter.OldColumn.ColumnType} -> {alter.ColumnType})",
        _ => operation.GetType().Name
    };
}

static class HarnessDbContextFactory
{
    public static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }
}

sealed class IntegrationRunner(IntegrationSettings settings, string fixture)
{
    private readonly string _fixture = fixture;
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "TrailGuardAssessmentPersistenceIntegrationVerification", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services = HarnessDbContextFactory.CreateProvider(settings.AppConnectionString);
    private bool _createdDatabase;

    public async Task<int> RunAsync()
    {
        var exitCode = 0;
        try
        {
            await CreateFreshDatabaseAsync();
            await using (var context = NewContext()) await context.Database.MigrateAsync();

            await SuccessfulPersistenceAsync();
            await ForcedRollbackAsync();
            await ConcurrentRetakesAsync();
            await RegistrationFirstAsync();
            await RetakeFirstAsync();
            await CapacityOneAllowsOneAsync();
            await CapacityTwoAllowsTwoAsync();
            await CapacityReductionPolicyAsync();
            await RegistrationBeforeCapacityReductionAsync();
            await CapacityReductionBeforeRegistrationAsync();
            Console.WriteLine("PASS: isolated PostgreSQL assessment persistence scenarios completed.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Integration scenario failure: {exception.GetType().Name}: {exception.Message}");
            exitCode = 1;
        }
        finally
        {
            try
            {
                Directory.Delete(_uploads, recursive: true);
                Console.WriteLine("Integration upload cleanup succeeded.");
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine("Integration upload cleanup not required.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Integration upload cleanup failed: {exception.GetType().Name}: {exception.Message}");
                exitCode = 1;
            }

            try
            {
                NpgsqlConnection.ClearAllPools();
                Console.WriteLine("Integration connection-pool cleanup succeeded.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Integration connection-pool cleanup failed: {exception.GetType().Name}: {exception.Message}");
                exitCode = 1;
            }
            if (_createdDatabase)
            {
                try
                {
                    await DropCreatedDatabaseAsync();
                    Console.WriteLine("Integration database cleanup succeeded.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Integration database cleanup failed: {exception.GetType().Name}: {exception.Message}");
                    exitCode = 1;
                }
            }
            else
            {
                Console.WriteLine("Integration database cleanup not required: this run did not create the test database.");
            }

            try
            {
                await _services.DisposeAsync();
                Console.WriteLine("Integration service cleanup succeeded.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Integration service cleanup failed: {exception.GetType().Name}: {exception.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private async Task CreateFreshDatabaseAsync()
    {
        await using var admin = new NpgsqlConnection(settings.AdminConnectionString);
        await admin.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", admin);
        exists.Parameters.AddWithValue("name", IntegrationSafety.TestDatabase);
        if (await exists.ExecuteScalarAsync() is not null)
            throw new InvalidOperationException($"Refusing to run: '{IntegrationSafety.TestDatabase}' already exists. It was not created or dropped.");
        await using var create = new NpgsqlCommand("CREATE DATABASE \"trailguard_assessment_it\"", admin);
        await create.ExecuteNonQueryAsync();
        _createdDatabase = true;
    }

    private async Task DropCreatedDatabaseAsync()
    {
        await using var admin = new NpgsqlConnection(settings.AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP DATABASE \"trailguard_assessment_it\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        _createdDatabase = false;
    }

    private ApplicationDbContext NewContext() => _services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    private async Task SuccessfulPersistenceAsync()
    {
        var seed = await SeedAsync("success", withPriorAssessment: false);
        await using var context = NewContext();
        var result = await NewAssessmentController(context, seed.UserId).Form(seed.Input);
        Require(result is RedirectToActionResult, "Successful assessment did not redirect.");
        await AssertSingleCompleteActiveGraphAsync(seed);
    }

    private async Task ForcedRollbackAsync()
    {
        var seed = await SeedAsync("rollback");
        try
        {
            await using (var context = NewContext())
            {
                await context.Database.ExecuteSqlRawAsync("CREATE FUNCTION tg_it_fail_shap_insert() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced shap failure'; END; $$;");
                await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER tg_it_fail_shap_insert_trigger BEFORE INSERT ON \"ShapValues\" FOR EACH ROW EXECUTE FUNCTION tg_it_fail_shap_insert();");
            }
            await using (var context = NewContext())
            {
                var result = await NewAssessmentController(context, seed.UserId).Form(seed.Input);
                Require(result is ViewResult, "Forced save failure did not return the safe form result.");
            }
            await using var verify = NewContext();
            var assessments = await verify.Assessments.Where(a => a.EventId == seed.EventId && a.UserId == seed.UserId).ToListAsync();
            Require(assessments.Count == 1 && assessments.Single().IsActive, "Rollback changed the prior active assessment.");
            Require(await verify.SuitabilityResults.CountAsync(r => r.AssessmentId == seed.PriorAssessmentId) == 1, "Rollback left a partial suitability result.");
            Require(await verify.ShapValues.CountAsync(v => v.SuitabilityResult!.AssessmentId == seed.PriorAssessmentId) == 11, "Rollback changed prior SHAP values.");
        }
        finally
        {
            await using var cleanup = NewContext();
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS tg_it_fail_shap_insert_trigger ON \"ShapValues\";");
            await cleanup.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS tg_it_fail_shap_insert();");
        }
    }

    private async Task ConcurrentRetakesAsync()
    {
        var seed = await SeedAsync("retakes");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstContext = NewContext();
        await using var secondContext = NewContext();
        var first = new LockHoldingAssessmentController(firstContext, entered, release, _fixture);
        ConfigureController(first, seed.UserId);
        var second = NewAssessmentController(secondContext, seed.UserId);
        var firstTask = first.Form(seed.Input);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = second.Form(seed.Input);
        release.SetResult();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));
        await AssertSingleCompleteActiveGraphAsync(seed);
    }

    private async Task RegistrationFirstAsync()
    {
        var seed = await SeedAsync("registration-first");
        var checkedSecondTime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var assessmentContext = NewContext();
        var retake = new RegistrationGateAssessmentController(assessmentContext, checkedSecondTime, release, _fixture);
        ConfigureController(retake, seed.UserId);
        var retakeTask = retake.Form(seed.Input);
        await checkedSecondTime.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var registrationContext = NewContext())
        {
            var registration = await NewRegistrationController(registrationContext, seed.UserId).Register(seed.EventId, seed.PriorAssessmentId, "Participant", "p@example.test", "1", "Emergency", "2", "Main gate", null, null);
            Require(registration is RedirectToActionResult, "Registration-first setup did not commit.");
        }
        release.SetResult();
        Require(await retakeTask.WaitAsync(TimeSpan.FromSeconds(30)) is ViewResult, "Retake was not blocked after registration committed.");
        await using var verify = NewContext();
        Require(await verify.EventRegistrations.CountAsync(r => r.EventId == seed.EventId && r.UserId == seed.UserId) == 1, "Registration-first state changed unexpectedly.");
        Require((await verify.Assessments.SingleAsync(a => a.Id == seed.PriorAssessmentId)).IsActive, "Registration-first retake replaced the active assessment.");
    }

    private async Task RetakeFirstAsync()
    {
        var seed = await SeedAsync("retake-first");
        await using (var assessmentContext = NewContext())
            Require(await NewAssessmentController(assessmentContext, seed.UserId).Form(seed.Input) is RedirectToActionResult, "Retake-first setup did not persist the retake.");
        await using (var registrationContext = NewContext())
        {
            var result = await NewRegistrationController(registrationContext, seed.UserId).Register(seed.EventId, seed.PriorAssessmentId, "Participant", "p@example.test", "1", "Emergency", "2", "Main gate", new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "clearance", "clearance.png"), null);
            Require(result is RedirectToActionResult, "Old-assessment registration did not redirect safely.");
        }
        await using var verify = NewContext();
        Require(await verify.EventRegistrations.CountAsync(r => r.EventId == seed.EventId && r.UserId == seed.UserId) == 0, "Old-assessment registration persisted.");
        Require(!Directory.Exists(Path.Combine(_uploads, "uploads", "medical-clearances")), "Old-assessment registration left an uploaded document.");
    }

    // The first request pauses only after it owns the event-wide lock and has read the
    // current count. The competing requests therefore prove their decisions use the
    // post-lock count rather than a stale concurrent read.
    private async Task CapacityOneAllowsOneAsync() => await AssertCapacityLimitAsync(1, 2);
    private async Task CapacityTwoAllowsTwoAsync() => await AssertCapacityLimitAsync(2, 3);

    private async Task AssertCapacityLimitAsync(int capacity, int participantCount)
    {
        var seed = await SeedAsync($"capacity-{capacity}");
        await SetCapacityAsync(seed.EventId, capacity);
        var participants = new List<ParticipantAssessment> { new(seed.UserId, seed.PriorAssessmentId) };
        for (var index = 2; index <= participantCount; index++)
            participants.Add(await AddParticipantAssessmentAsync(seed.EventId, $"capacity-{capacity}-{index}"));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstContext = NewContext();
        var first = new CapacityGateRegistrationController(firstContext, new TestEnvironment(_uploads), entered, release);
        ConfigureController(first, participants[0].UserId);
        var firstTask = RegisterAsync(first, seed.EventId, participants[0].AssessmentId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var competing = participants.Skip(1).Select(async participant =>
        {
            await using var context = NewContext();
            return await RegisterAsync(NewRegistrationController(context, participant.UserId), seed.EventId, participant.AssessmentId);
        }).ToArray();
        release.TrySetResult();
        await Task.WhenAll(competing.Prepend(firstTask)).WaitAsync(TimeSpan.FromSeconds(30));

        await using var verify = NewContext();
        var occupied = await verify.EventRegistrations.CountAsync(r => r.EventId == seed.EventId && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));
        Require(occupied == capacity, $"Capacity {capacity} admitted {occupied} capacity-consuming registrations.");
    }

    private async Task SetCapacityAsync(int eventId, int capacity)
    {
        await using var context = NewContext();
        var evt = await context.Events.SingleAsync(e => e.Id == eventId);
        evt.Capacity = capacity;
        await context.SaveChangesAsync();
    }

    private async Task CapacityReductionPolicyAsync()
    {
        var seed = await SeedAsync("capacity-edit");
        await SetCapacityAsync(seed.EventId, 2);
        await using (var registrationContext = NewContext())
            Require(await RegisterAsync(NewRegistrationController(registrationContext, seed.UserId), seed.EventId, seed.PriorAssessmentId) is RedirectToActionResult, "Capacity-edit setup registration did not persist.");

        await using (var rejectContext = NewContext())
        {
            var rejected = await NewEventController(rejectContext, seed.UserId).EditEvent(await EditInputAsync(rejectContext, seed.EventId, 0));
            Require(IsFailure(rejected), "Capacity reduction below occupied slots was accepted.");
        }
        await using (var acceptContext = NewContext())
        {
            var accepted = await NewEventController(acceptContext, seed.UserId).EditEvent(await EditInputAsync(acceptContext, seed.EventId, 1));
            Require(!IsFailure(accepted), "Capacity equal to occupied slots was rejected.");
        }
    }

    private async Task RegistrationBeforeCapacityReductionAsync()
    {
        var seed = await SeedAsync("registration-before-capacity-edit");
        await SetCapacityAsync(seed.EventId, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registrationContext = NewContext();
        var registration = new CapacityGateRegistrationController(registrationContext, new TestEnvironment(_uploads), entered, release);
        ConfigureController(registration, seed.UserId);
        var registrationTask = RegisterAsync(registration, seed.EventId, seed.PriorAssessmentId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var eventContext = NewContext();
        var editTask = NewEventController(eventContext, seed.UserId).EditEvent(await EditInputAsync(eventContext, seed.EventId, 0));
        release.TrySetResult();
        Require(await registrationTask.WaitAsync(TimeSpan.FromSeconds(30)) is RedirectToActionResult, "Registration did not commit before capacity edit.");
        Require(IsFailure(await editTask.WaitAsync(TimeSpan.FromSeconds(30))), "Capacity edit below the now-occupied count was accepted.");
        await AssertOccupiedNotAboveCapacityAsync(seed.EventId);
    }

    private async Task CapacityReductionBeforeRegistrationAsync()
    {
        var seed = await SeedAsync("capacity-edit-before-registration");
        await SetCapacityAsync(seed.EventId, 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var eventContext = NewContext();
        var edit = NewCapacityGateEventController(eventContext, seed.UserId, entered, release);
        var editTask = edit.EditEvent(await EditInputAsync(eventContext, seed.EventId, 0));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var registrationContext = NewContext();
        var registrationTask = RegisterAsync(NewRegistrationController(registrationContext, seed.UserId), seed.EventId, seed.PriorAssessmentId);
        release.TrySetResult();
        Require(!IsFailure(await editTask.WaitAsync(TimeSpan.FromSeconds(30))), "Capacity reduction before registration was rejected.");
        Require(await registrationTask.WaitAsync(TimeSpan.FromSeconds(30)) is RedirectToActionResult, "Full-capacity registration did not redirect safely.");
        await AssertOccupiedNotAboveCapacityAsync(seed.EventId);
    }

    private async Task AssertOccupiedNotAboveCapacityAsync(int eventId)
    {
        await using var context = NewContext();
        var capacity = await context.Events.Where(e => e.Id == eventId).Select(e => e.Capacity).SingleAsync();
        var occupied = await context.EventRegistrations.CountAsync(r => r.EventId == eventId && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));
        Require(occupied <= capacity, "Final occupied count exceeds event capacity.");
    }

    private EventController NewEventController(ApplicationDbContext context, string userId)
    {
        var users = _services.GetRequiredService<UserManager<ApplicationUser>>();
        var controller = new EventController(context, new TestEnvironment(_uploads), users,
            new WeatherService(new HttpClient(), NullLogger<WeatherService>.Instance), NullLogger<EventController>.Instance,
            new RoleAssignmentService(users, context, NullLogger<RoleAssignmentService>.Instance));
        ConfigureController(controller, userId);
        return controller;
    }

    private CapacityGateEventController NewCapacityGateEventController(ApplicationDbContext context, string userId, TaskCompletionSource entered, TaskCompletionSource release)
    {
        var users = _services.GetRequiredService<UserManager<ApplicationUser>>();
        var controller = new CapacityGateEventController(context, new TestEnvironment(_uploads), users,
            new WeatherService(new HttpClient(), NullLogger<WeatherService>.Instance), NullLogger<EventController>.Instance,
            new RoleAssignmentService(users, context, NullLogger<RoleAssignmentService>.Instance), entered, release);
        ConfigureController(controller, userId);
        return controller;
    }

    private static bool IsFailure(JsonResult result) => JsonSerializer.Serialize(result.Value).Contains("\"success\":false", StringComparison.Ordinal);

    private async Task<EventEditModel> EditInputAsync(ApplicationDbContext context, int eventId, int capacity)
    {
        var evt = await context.Events.SingleAsync(e => e.Id == eventId);
        return new EventEditModel { Id = evt.Id, EventTitle = evt.EventTitle, Description = evt.Description, EventDate = evt.EventDate, EventTime = evt.EventTime, TrailId = evt.TrailId, EstimatedDuration = evt.EstimatedDuration, Capacity = capacity, OrganizerId = evt.OrganizerId, Status = evt.Status, PickupSchedules = [new PickupScheduleInputModel { Location = "Main gate", Time = "06:00" }] };
    }

    private async Task<ParticipantAssessment> AddParticipantAssessmentAsync(int eventId, string suffix)
    {
        var userId = "it-" + suffix;
        await using var context = NewContext();
        context.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant(), FirstName = "Integration", LastName = "Participant", Email = userId + "@example.test", NormalizedEmail = (userId + "@example.test").ToUpperInvariant(), Birthday = new DateOnly(1995, 1, 1), Gender = ParticipantDemographics.PreferNotToSay });
        var request = Request();
        var response = await PredictionAsync(request);
        var assessment = new Assessment { EventId = eventId, UserId = userId, IsActive = true, Result = "Good-Match", ConsentGiven = true };
        TrailGuardV2SuitabilityResultFactory.Create(assessment, DateTimeOffset.UtcNow, request, response);
        context.Assessments.Add(assessment);
        await context.SaveChangesAsync();
        return new(userId, assessment.Id);
    }

    private static Task<IActionResult> RegisterAsync(RegistrationController controller, int eventId, int assessmentId) =>
        controller.Register(eventId, assessmentId, "Participant", "p@example.test", "1", "Emergency", "2", "Main gate", null, null);

    private async Task<Seed> SeedAsync(string suffix, bool withPriorAssessment = true)
    {
        var userId = "it-" + suffix;
        await using var context = NewContext();
        var user = new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant(), FirstName = "Integration", LastName = "Participant", Email = userId + "@example.test", NormalizedEmail = (userId + "@example.test").ToUpperInvariant(), Birthday = new DateOnly(1995, 1, 1), Gender = ParticipantDemographics.PreferNotToSay };
        var trail = new Trail { Name = "Integration trail " + suffix, Location = "Local", DistanceKm = 8, TypicalDurationHours = 5, ElevationGainMeters = 600, Terrain = "Trail", TrailClass = 3, Description = "Integration" };
        context.AddRange(user, trail); await context.SaveChangesAsync();
        var evt = new Event { TrailId = trail.Id, EventTitle = "Integration event " + suffix, Description = "Integration", EventDate = DateTime.Today.AddDays(7), EventTime = TimeSpan.FromHours(6), Location = "Local", Difficulty = "Moderate", Capacity = 20, Status = "Upcoming", OrganizerId = userId, OrganizedBy = "Integration Participant", PickupPoints = "Main gate", TrailNameSnapshot = trail.Name, TrailDistanceKmSnapshot = 8, TrailDurationHoursSnapshot = 5, TrailElevationGainMetersSnapshot = 600, TrailTerrainSnapshot = "Trail", TrailClassSnapshot = 3, TrailAdjustedRatingSnapshot = 1, EstimatedDuration = 5 };
        context.Events.Add(evt); await context.SaveChangesAsync();
        var request = Request();
        var response = await PredictionAsync(request);
        var priorAssessmentId = 0;
        if (withPriorAssessment)
        {
            var prior = new Assessment { EventId = evt.Id, UserId = userId, IsActive = true, Result = "Good-Match", ConsentGiven = true };
            TrailGuardV2SuitabilityResultFactory.Create(prior, DateTimeOffset.UtcNow, request, response);
            context.Assessments.Add(prior); await context.SaveChangesAsync();
            priorAssessmentId = prior.Id;
        }
        return new(evt.Id, userId, priorAssessmentId, Input(evt.Id));
    }

    private AssessmentController NewAssessmentController(ApplicationDbContext context, string userId) { var controller = new AssessmentController(context, HistoricalClient(), V2Client(), new TrailGuardV2AssessmentRequestMapper(), NullLogger<AssessmentController>.Instance); ConfigureController(controller, userId); return controller; }
    private RegistrationController NewRegistrationController(ApplicationDbContext context, string userId) { var controller = new RegistrationController(context, new TestEnvironment(_uploads)); ConfigureController(controller, userId); return controller; }
    private TrailGuardV2ApiClient V2Client() => new(new HttpClient(new FixtureHandler(_fixture)) { BaseAddress = new Uri("http://fixture.local") }, NullLogger<TrailGuardV2ApiClient>.Instance);
    private SuitabilityApiClient HistoricalClient() => new(new HttpClient(new FixtureHandler(_fixture)) { BaseAddress = new Uri("http://fixture.local") }, NullLogger<SuitabilityApiClient>.Instance);
    private async Task<TrailGuardV2PredictionResponse> PredictionAsync(TrailGuardV2PredictionRequest request) => (await V2Client().PredictAsync(request)).Prediction!;
    private static void ConfigureController(Controller controller, string userId) { var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "it")) }; controller.ControllerContext = new ControllerContext { HttpContext = http }; controller.TempData = new TempDataDictionary(http, new MemoryTempDataProvider()); }
    private static TrailGuardV2PredictionRequest Request() => new() { ExerciseFrequency = "3–4", CardioDuration = "15–29", ExerciseConsistency = "3+ months", HikingExperience = "4–10", HikingRecency = "Within 3 months", HardestTrailCompleted = "Class 3", GearScore = 6, DistanceKm = 8, ElevationGainM = 600, TrailClass = 3, TypicalDurationHours = 5 };
    private static TrailGuardV2AssessmentFormInput Input(int eventId) => new() { EventId = eventId, Age = 30, HeightCm = 170, WeightKg = 70, MedicalConditions = [AssessmentSubmissionGuards.NoneOfTheAbove], ExerciseFrequency = "3–4", CardioEndurance = "15–29", ExerciseConsistency = "3+ months", MountainsClimbed = "4–10", RecencyOfHike = "Within 3 months", TrailDifficultyCompleted = "Class 3", GearItems = ["Enough water", "Trail food", "First aid kit", "Flashlight", "Whistle", "Raincoat"], ConsentGiven = true, DataPrivacyConsent = true };
    private async Task AssertSingleCompleteActiveGraphAsync(Seed seed) { await using var context = NewContext(); var active = await context.Assessments.Where(a => a.EventId == seed.EventId && a.UserId == seed.UserId && a.IsActive).Include(a => a.SuitabilityResults).ThenInclude(r => r.ShapValues).ToListAsync(); Require(active.Count == 1 && active.Single().SuitabilityResults.Count == 1 && active.Single().SuitabilityResults.Single().ShapValues.Count == 11, "Expected one complete active prediction graph."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record Seed(int EventId, string UserId, int PriorAssessmentId, TrailGuardV2AssessmentFormInput Input);
    private sealed record ParticipantAssessment(string UserId, int AssessmentId);
}

sealed class FixtureHandler(string body) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); }
sealed class MemoryTempDataProvider : ITempDataProvider { public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(); public void SaveTempData(HttpContext context, IDictionary<string, object> values) { } }
sealed class TestEnvironment(string root) : IWebHostEnvironment { public string ApplicationName { get; set; } = "Integration"; public string EnvironmentName { get; set; } = "Integration"; public string WebRootPath { get; set; } = root; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider(); public string ContentRootPath { get; set; } = root; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }

class LockHoldingAssessmentController : AssessmentController
{
    private readonly TaskCompletionSource _entered;
    private readonly TaskCompletionSource _release;
    public LockHoldingAssessmentController(ApplicationDbContext context, TaskCompletionSource entered, TaskCompletionSource release, string fixture)
        : base(context, TestClients.Historical(fixture), TestClients.V2(fixture), new TrailGuardV2AssessmentRequestMapper(), NullLogger<AssessmentController>.Instance)
    { _entered = entered; _release = release; }
    protected override async Task OnActiveAssessmentLoadedForPersistenceAsync(Assessment? oldAssessment) { _entered.TrySetResult(); await _release.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
}

sealed class RegistrationGateAssessmentController : AssessmentController
{
    private readonly TaskCompletionSource _checkedSecondTime;
    private readonly TaskCompletionSource _release;
    private int _checks;
    public RegistrationGateAssessmentController(ApplicationDbContext context, TaskCompletionSource checkedSecondTime, TaskCompletionSource release, string fixture)
        : base(context, TestClients.Historical(fixture), TestClients.V2(fixture), new TrailGuardV2AssessmentRequestMapper(), NullLogger<AssessmentController>.Instance)
    { _checkedSecondTime = checkedSecondTime; _release = release; }
    protected override async Task<bool> HasActiveRegistrationAsync(int eventId, string userId)
    {
        var value = await base.HasActiveRegistrationAsync(eventId, userId);
        if (Interlocked.Increment(ref _checks) == 2) { _checkedSecondTime.TrySetResult(); await _release.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        return value;
    }
}

sealed class CapacityGateRegistrationController : RegistrationController
{
    private readonly TaskCompletionSource _entered;
    private readonly TaskCompletionSource _release;
    public CapacityGateRegistrationController(ApplicationDbContext context, IWebHostEnvironment environment, TaskCompletionSource entered, TaskCompletionSource release)
        : base(context, environment) { _entered = entered; _release = release; }

    protected override async Task OnCapacityCheckedForRegistrationAsync(int eventId, string userId)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

sealed class CapacityGateEventController : EventController
{
    private readonly TaskCompletionSource _entered;
    private readonly TaskCompletionSource _release;
    public CapacityGateEventController(ApplicationDbContext context, IWebHostEnvironment environment, UserManager<ApplicationUser> users, WeatherService weather, ILogger<EventController> logger, RoleAssignmentService roles, TaskCompletionSource entered, TaskCompletionSource release)
        : base(context, environment, users, weather, logger, roles) { _entered = entered; _release = release; }

    protected override async Task OnCapacityStateLoadedForEditAsync(int eventId)
    {
        _entered.TrySetResult();
        await _release.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

static class TestClients
{
    public static TrailGuardV2ApiClient V2(string fixture) => new(new HttpClient(new FixtureHandler(fixture)) { BaseAddress = new Uri("http://fixture.local") }, NullLogger<TrailGuardV2ApiClient>.Instance);
    public static SuitabilityApiClient Historical(string fixture) => new(new HttpClient(new FixtureHandler(fixture)) { BaseAddress = new Uri("http://fixture.local") }, NullLogger<SuitabilityApiClient>.Instance);
}
