using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TrailGuard.Controllers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

// Only runs in the existing explicitly guarded fresh local synthetic database.
sealed partial class IntegrationRunner
{
    private const string PolicyAdmin = "it-policy-admin";
    private const string OtherOrganizer = "it-policy-other-organizer";

    private async Task MlPolicyScenariosAsync()
    {
        await PolicyRolesAsync();
        await ReplacementHistoryAsync();
        await OrdinaryEditsAsync();
        await CompletedAssessmentAsync();
        await ReplacementBeforeInferencePersistenceAsync();
        await AssessmentBeforeReplacementAsync();
        await RegistrationBeforeReplacementAsync();
        await OwnershipChangedDuringEditAsync();
        Console.WriteLine("PASS ML policy: real controller/EF queries, all participation statuses, Admin/owner boundaries, stale requests, exact snapshots and observed PostgreSQL lock waits.");
    }

    private ApplicationDbContext PolicyContext(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(settings.AppConnectionString).AddInterceptors(interceptors).Options);

    private async Task PolicyRolesAsync()
    {
        await using var context = NewContext();
        foreach (var role in new[] { "Admin", "Organizer" })
            if (!await context.Roles.AnyAsync(item => item.Name == role))
                context.Roles.Add(new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() });
        context.Users.AddRange(
            new ApplicationUser { Id = PolicyAdmin, UserName = PolicyAdmin, FirstName = "Policy", LastName = "Admin" },
            new ApplicationUser { Id = OtherOrganizer, UserName = OtherOrganizer, FirstName = "Other", LastName = "Organizer" });
        await context.SaveChangesAsync();
        foreach (var (userId, role) in new[] { (PolicyAdmin, "Admin"), (OtherOrganizer, "Organizer") })
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = await context.Roles.Where(item => item.Name == role).Select(item => item.Id).SingleAsync() });
        await context.SaveChangesAsync();
    }

    private async Task AssignPolicyOwnerAsync(string userId)
    {
        await using var context = NewContext();
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = await context.Roles.Where(item => item.Name == "Organizer").Select(item => item.Id).SingleAsync() });
        await context.SaveChangesAsync();
    }

    private async Task<Trail> ReplacementTrailAsync(string suffix, bool identicalInputs = false)
    {
        await using var context = NewContext();
        var trail = new Trail { Name = "Replacement " + suffix, Description = "Synthetic policy fixture", Location = "Replacement location", Terrain = "Rocky",
            DistanceKm = identicalInputs ? 8 : 12, ElevationGainMeters = identicalInputs ? 600 : 900,
            TrailClass = identicalInputs ? 3 : 2, TypicalDurationHours = identicalInputs ? 5 : 7, IsActive = true,
            ThumbnailUrl = "/images/policy-fixture.jpg" };
        context.Trails.Add(trail); await context.SaveChangesAsync(); return trail;
    }

    private static JsonElement Json(JsonResult result) => JsonSerializer.SerializeToElement(result.Value);
    private static bool Succeeded(JsonResult result) => Json(result).GetProperty("success").GetBoolean();
    private static string Message(JsonResult result) => Json(result).GetProperty("message").GetString()!;
    private static string TrailSnapshot(Event item) => JsonSerializer.Serialize(new
    {
        item.TrailId, item.TrailNameSnapshot, item.TrailDistanceKmSnapshot, item.TrailElevationGainMetersSnapshot,
        item.TrailClassSnapshot, item.TrailDurationHoursSnapshot, item.TrailTerrainSnapshot, item.TrailThumbnailUrlSnapshot,
        item.Location, item.Difficulty, item.DifficultyScoreSnapshot
    });

    private async Task ReplacementHistoryAsync()
    {
        var histories = new[] { "active assessment", "inactive assessment", "Pending", "Awaiting Payment", "For Payment Verification", "Accepted",
            "Rejected", "Cancelled", "Voided", "Alternative Recommended", "Other retained status" };
        foreach (var history in histories)
        {
            var seed = await SeedAsync("policy-" + histories.ToList().IndexOf(history), withPriorAssessment: false);
            await AssignPolicyOwnerAsync(seed.UserId);
            var replacement = await ReplacementTrailAsync(history);
            await using (var setup = NewContext())
            {
                Require(Json(await NewEventController(setup, seed.UserId).GetEvent(seed.EventId)).GetProperty("canChangeTrail").GetBoolean(), "No-record Event must expose an editable trail.");
                if (history.EndsWith("assessment", StringComparison.Ordinal))
                    setup.Assessments.Add(new Assessment { EventId = seed.EventId, UserId = seed.UserId, IsActive = history == "active assessment", Result = "Good-Match", ConsentGiven = true });
                else
                    setup.EventRegistrations.Add(new EventRegistration { EventId = seed.EventId, UserId = seed.UserId, ParticipantName = "Synthetic", Status = history });
                await setup.SaveChangesAsync();
            }
            foreach (var caller in new[] { seed.UserId, PolicyAdmin })
            {
                await using var edit = NewContext();
                var controller = NewEventController(edit, caller);
                var input = await EditInputAsync(edit, seed.EventId, 20);
                var before = JsonSerializer.Serialize(await edit.Events.AsNoTracking().SingleAsync(item => item.Id == seed.EventId));
                input.TrailId = replacement.Id; input.Description = "Must not be saved";
                var response = await controller.EditEvent(input);
                Require(!Succeeded(response) && Message(response) == EventTrailReplacementPolicy.LockedMessage, $"{history} must freeze trail replacement for owner and Admin, including a stale/forged request.");
                await using var verify = NewContext();
                Require(before == JsonSerializer.Serialize(await verify.Events.AsNoTracking().SingleAsync(item => item.Id == seed.EventId)), "Denied replacement mutated Event fields or snapshots.");
                var state = Json(await NewEventController(verify, caller).GetEvent(seed.EventId));
                Require(!state.GetProperty("canChangeTrail").GetBoolean() && !state.TryGetProperty("assessmentCount", out _), "Edit state must expose only trail-change availability, without participation counts.");
            }
        }
        Console.WriteLine("PASS ML history: active/inactive assessments and all eight registration statuses plus an unknown historical status freeze replacement for owner/Admin; no history rewritten.");
    }

    private async Task OrdinaryEditsAsync()
    {
        foreach (var asAdmin in new[] { false, true })
        {
            var seed = await SeedAsync("policy-edit-" + asAdmin, withPriorAssessment: false);
            await AssignPolicyOwnerAsync(seed.UserId);
            var replacement = await ReplacementTrailAsync("allowed-" + asAdmin);
            await using (var edit = NewContext())
            {
                var input = await EditInputAsync(edit, seed.EventId, 20); input.TrailId = replacement.Id;
                Require(Succeeded(await NewEventController(edit, asAdmin ? PolicyAdmin : seed.UserId).EditEvent(input)), "No-record trail replacement must remain permitted.");
            }
            await using var setup = NewContext();
            var evt = await setup.Events.SingleAsync(item => item.Id == seed.EventId);
            Require(evt.TrailId == replacement.Id && evt.TrailNameSnapshot == replacement.Name && evt.Location == replacement.Location
                && evt.TrailDistanceKmSnapshot == replacement.DistanceKm && evt.TrailElevationGainMetersSnapshot == replacement.ElevationGainMeters
                && evt.TrailDurationHoursSnapshot == replacement.TypicalDurationHours && evt.TrailClassSnapshot == replacement.TrailClass
                && evt.TrailTerrainSnapshot == replacement.Terrain && evt.TrailThumbnailUrlSnapshot == replacement.ThumbnailUrl,
                "Allowed replacement must capture the complete fresh persisted trail snapshot.");
            var snapshot = TrailSnapshot(evt);
            setup.Assessments.Add(new Assessment { EventId = seed.EventId, UserId = seed.UserId, IsActive = false, ConsentGiven = true });
            var live = await setup.Trails.SingleAsync(item => item.Id == replacement.Id);
            live.IsActive = false; live.DistanceKm = 99; live.Name = "Edited live trail";
            await setup.SaveChangesAsync();
            await using (var edit = NewContext())
            {
                var input = await EditInputAsync(edit, seed.EventId, 18);
                input.Description = "Allowed description"; input.NotesAndReminders = "Allowed notes"; input.PaymentDetails = "Allowed payment instructions";
                input.EventDate = input.EventDate.AddDays(1); input.EstimatedDuration = 11;
                Require(Succeeded(await NewEventController(edit, asAdmin ? PolicyAdmin : seed.UserId).EditEvent(input)), "Same-trail edits must remain allowed with participation and an inactive trail.");
            }
            await using var verify = NewContext();
            var saved = await verify.Events.SingleAsync(item => item.Id == seed.EventId);
            Require(TrailSnapshot(saved) == snapshot && saved.Capacity == 18 && saved.EstimatedDuration == 11 && saved.Description == "Allowed description"
                && saved.NotesAndReminders == "Allowed notes" && saved.PaymentDetails == "Allowed payment instructions" && saved.EventDate == evt.EventDate.AddDays(1),
                "Same-trail edit must preserve every snapshot while saving other permitted fields.");
            var denied = await NewEventController(verify, OtherOrganizer).EditEvent(await EditInputAsync(verify, seed.EventId, 18));
            Require(!Succeeded(denied) && Message(denied) == "Event not found" && !Succeeded(await NewEventController(verify, OtherOrganizer).GetEvent(seed.EventId)), "Unrelated Organizer must not read edit state or edit the Event.");
            verify.EventRegistrations.Add(new EventRegistration { EventId = seed.EventId, UserId = seed.UserId, ParticipantName = "Synthetic occupied slot", Status = "Accepted" });
            await verify.SaveChangesAsync();
            var capacityRejected = await NewEventController(verify, asAdmin ? PolicyAdmin : seed.UserId).EditEvent(await EditInputAsync(verify, seed.EventId, 0));
            Require(!Succeeded(capacityRejected) && Message(capacityRejected).Contains("currently occupied slot", StringComparison.Ordinal), "Same-trail edits must retain the occupied-capacity safeguard.");
            await using var final = NewContext();
            Require((await final.Events.SingleAsync(item => item.Id == seed.EventId)).Capacity == 18, "Denied capacity edit must leave the saved capacity unchanged.");
        }
        Console.WriteLine("PASS ML edits: no-record replacement, complete snapshots, same-trail inactive history edits, schedule/notes/payment/capacity preservation, unrelated Organizer denial.");
    }

    private async Task CompletedAssessmentAsync()
    {
        var seed = await SeedAsync("policy-completion");
        await using (var setup = NewContext()) { var evt = await setup.Events.SingleAsync(item => item.Id == seed.EventId); evt.EventDate = DateTime.Today; await setup.SaveChangesAsync(); }
        var gate = new PolicyInferenceGate(_fixture);
        await using var assessmentContext = NewContext();
        var controller = PolicyAssessmentController(assessmentContext, seed.UserId, gate);
        var assessmentTask = controller.Form(seed.Input);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var edit = NewContext();
            Require(Succeeded(await NewEventController(edit, seed.UserId).CompleteEvent(new EventController.CompleteEventRequest { Id = seed.EventId })), "Completion during inference must commit first.");
        }
        finally { gate.Release.TrySetResult(); }
        Require(await assessmentTask.WaitAsync(TimeSpan.FromSeconds(20)) is RedirectToActionResult { ActionName: "Details" }
            && controller.TempData["Error"]?.ToString() == AssessmentSubmissionGuards.CompletedEventMessage, "Completed locked reload must reject inference output.");
        await using var verify = NewContext();
        var reader = NewAssessmentController(verify, seed.UserId);
        Require(await reader.Form(seed.EventId) is RedirectToActionResult { ActionName: "Details" }
            && await reader.Form(seed.Input) is RedirectToActionResult { ActionName: "Details" }, "Completed GET/POST must reject.");
        Require(await reader.Report(seed.PriorAssessmentId) is ViewResult, "Permitted historical assessment report must remain readable after completion.");
        Require(await verify.Assessments.CountAsync(item => item.EventId == seed.EventId) == 1 && (await verify.Assessments.SingleAsync(item => item.Id == seed.PriorAssessmentId)).IsActive,
            "Completed submissions must preserve the prior active assessment.");
        await AssertSingleCompleteActiveGraphAsync(seed);
        Console.WriteLine("PASS ML completion: actual Event completion during gated inference rejects persistence; completed entry closed; historical report and prior full graph retained.");
    }

    private AssessmentController PolicyAssessmentController(ApplicationDbContext context, string userId, HttpMessageHandler handler)
    {
        var client = new TrailGuardV2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://controlled.fixture") }, NullLogger<TrailGuardV2ApiClient>.Instance);
        var controller = new AssessmentController(context, HistoricalClient(), client, new TrailGuardV2AssessmentRequestMapper(), NullLogger<AssessmentController>.Instance);
        ConfigureController(controller, userId); return controller;
    }

    private async Task ReplacementBeforeInferencePersistenceAsync()
    {
        var seed = await SeedAsync("policy-replacement-first", withPriorAssessment: false);
        var replacement = await ReplacementTrailAsync("identity-only-race", identicalInputs: true);
        var gate = new PolicyInferenceGate(_fixture);
        await using var assessmentContext = NewContext();
        var controller = PolicyAssessmentController(assessmentContext, seed.UserId, gate);
        var task = controller.Form(seed.Input);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var edit = NewContext(); var input = await EditInputAsync(edit, seed.EventId, 20); input.TrailId = replacement.Id;
            Require(Succeeded(await NewEventController(edit, seed.UserId).EditEvent(input)), "Trail replacement with no participation must commit while inference is pending (no lock held during HTTP).");
        }
        finally { gate.Release.TrySetResult(); }
        Require(await task.WaitAsync(TimeSpan.FromSeconds(20)) is RedirectToActionResult { ActionName: "Details" }
            && controller.TempData["Error"]?.ToString() == AssessmentSubmissionGuards.TrailChangedMessage && gate.Calls == 1,
            "A changed TrailId with identical numeric inputs must reject without automatic rescoring.");
        await using var verify = NewContext();
        Require(!await verify.Assessments.AnyAsync(item => item.EventId == seed.EventId) && !await verify.SuitabilityResults.AnyAsync(item => item.Assessment!.EventId == seed.EventId), "Stale inference must leave no graph.");
        Console.WriteLine("PASS ML race 1: replacement commits during HTTP inference; stale TrailId-only result rejected, no partial graph or automatic rescore.");
    }

    private async Task AssessmentBeforeReplacementAsync()
    {
        var seed = await SeedAsync("policy-assessment-first", withPriorAssessment: false);
        var replacement = await ReplacementTrailAsync("after-assessment");
        var commit = new StorageTransactionProbe(holdCommit: true);
        var command = new StorageCommandProbe("pg_advisory_xact_lock");
        await using var assessmentContext = PolicyContext(commit);
        await using var edit = PolicyContext(command);
        var task = NewAssessmentController(assessmentContext, seed.UserId).Form(seed.Input);
        Task<JsonResult>? editTask = null;
        try
        {
            var blocker = await commit.Committing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var input = await EditInputAsync(edit, seed.EventId, 20); input.TrailId = replacement.Id;
            editTask = NewEventController(edit, seed.UserId).EditEvent(input);
            var waiter = await command.Executing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ObservePolicyLockWaitAsync(waiter, blocker);
        }
        finally { commit.Release(); }
        Require(await task.WaitAsync(TimeSpan.FromSeconds(20)) is RedirectToActionResult { ActionName: "Report" }, "Assessment must commit first.");
        Require(editTask is not null && !Succeeded(await editTask.WaitAsync(TimeSpan.FromSeconds(20)))
            && Message(await editTask) == EventTrailReplacementPolicy.LockedMessage, "Waiting trail replacement must reload and see the new assessment.");
        await AssertSingleCompleteActiveGraphAsync(seed);
        await using var verify = NewContext();
        Require((await verify.Events.SingleAsync(item => item.Id == seed.EventId)).TrailId != replacement.Id, "Rejected replacement must retain the scored trail.");
        Console.WriteLine("PASS ML race 2: PostgreSQL pg_blocking_pids confirmed replacement waits behind assessment; committed graph freezes trail replacement.");
    }

    private async Task RegistrationBeforeReplacementAsync()
    {
        var seed = await SeedAsync("policy-registration-first");
        var replacement = await ReplacementTrailAsync("after-registration");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new StorageCommandProbe("pg_advisory_xact_lock");
        await using var registrationContext = NewContext();
        await using var edit = PolicyContext(probe);
        var registration = new CapacityGateRegistrationController(registrationContext, new TestEnvironment(_uploads), entered, release, _storage);
        ConfigureController(registration, seed.UserId);
        var task = registration.Register(seed.EventId, seed.PriorAssessmentId, "Synthetic", "p@example.test", "9123456789", "Emergency", "9234567890", "Main gate", null, null);
        Task<JsonResult>? editTask = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var input = await EditInputAsync(edit, seed.EventId, 20); input.TrailId = replacement.Id;
            editTask = NewEventController(edit, seed.UserId).EditEvent(input);
            var waiter = await probe.Executing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var blocker = ((NpgsqlConnection)registrationContext.Database.GetDbConnection()).ProcessID;
            await ObservePolicyLockWaitAsync(waiter, blocker);
        }
        finally { release.TrySetResult(); }
        Require(await task.WaitAsync(TimeSpan.FromSeconds(20)) is RedirectToActionResult && editTask is not null
            && Message(await editTask.WaitAsync(TimeSpan.FromSeconds(20))) == EventTrailReplacementPolicy.LockedMessage, "Competing registration creation must serialize against replacement.");
        await using var verify = NewContext();
        Require(await verify.EventRegistrations.CountAsync(item => item.EventId == seed.EventId) == 1 && (await verify.Events.SingleAsync(item => item.Id == seed.EventId)).TrailId != replacement.Id,
            "Registration-first race must preserve registration and the original trail.");
        Console.WriteLine("PASS ML registration race: observed PostgreSQL lock wait; registration commit retained and replacement denied.");
    }

    private async Task OwnershipChangedDuringEditAsync()
    {
        var seed = await SeedAsync("policy-owner-change", withPriorAssessment: false);
        var probe = new StorageCommandProbe("pg_advisory_xact_lock");
        await using var ownerChange = NewContext();
        await using var transaction = await ownerChange.Database.BeginTransactionAsync();
        await ParticipantEventWorkflowLock.AcquireEventCapacityAsync(ownerChange, seed.EventId);
        await using var edit = PolicyContext(probe);
        var input = await EditInputAsync(edit, seed.EventId, 20);
        var task = NewEventController(edit, seed.UserId).EditEvent(input);
        var waiter = await probe.Executing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ObservePolicyLockWaitAsync(waiter, ((NpgsqlConnection)ownerChange.Database.GetDbConnection()).ProcessID);
        var evt = await ownerChange.Events.SingleAsync(item => item.Id == seed.EventId); evt.OrganizerId = OtherOrganizer;
        await ownerChange.SaveChangesAsync(); await transaction.CommitAsync();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(20));
        Require(!Succeeded(result) && Message(result) == "Event not found", "Stale ownership must be denied on the locked reload.");
        Console.WriteLine("PASS ML stale ownership: actual waiting edit denied after ownership changed under the event lock.");
    }

    private async Task ObservePolicyLockWaitAsync(int waiter, int blocker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var connection = new NpgsqlConnection(settings.AppConnectionString); await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiter))", connection);
        command.Parameters.AddWithValue("blocker", blocker); command.Parameters.AddWithValue("waiter", waiter);
        while (await command.ExecuteScalarAsync(timeout.Token) is not true) await Task.Delay(20, timeout.Token);
    }
}

sealed class PolicyInferenceGate(string fixture) : HttpMessageHandler
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent(fixture, Encoding.UTF8, "application/json") };
    }
}
