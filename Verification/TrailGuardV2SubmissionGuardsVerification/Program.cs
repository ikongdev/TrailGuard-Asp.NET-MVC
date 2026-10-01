using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrailGuard.Controllers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
var assessmentOnly = args.SequenceEqual(["--assessment"]);
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var futureEvent = new Event
{
    Id = 41,
    Status = "Upcoming",
    EventDate = DateTime.Today.AddDays(7),
    Capacity = 20,
    PickupPoints = "Main gate",
    TrailDistanceKmSnapshot = 10,
    TrailElevationGainMetersSnapshot = 600,
    TrailClassSnapshot = 2,
    TrailDurationHoursSnapshot = 4
};

// Assessment POST: active registration is checked before mapping/inference and persistence.
{
    using var context = NewContext();
    var handler = new CountingHandler();
    var controller = NewAssessmentController(context, handler, futureEvent, hasActiveRegistration: true,
        demographics: ParticipantDemographicsResult.Complete(30, ParticipantDemographics.Female));
    var input = ValidAssessmentInput();
    input.Age = 18;
    input.Gender = ParticipantDemographics.Male;
    controller.ModelState.SetModelValue(nameof(input.Age), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult("18"));
    controller.ModelState.SetModelValue(nameof(input.Gender), new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(ParticipantDemographics.Male));
    var result = await controller.Form(input);

    Check(result is ViewResult, "An active registration must redisplay the submitted assessment form.");
    Check(controller.ModelState.ErrorCount > 0, "An active registration must add a model error.");
    Check(input.Age == 30 && input.Gender == ParticipantDemographics.Female, "An active-registration redisplay must hydrate authoritative demographics.");
    Check(!controller.ModelState.ContainsKey(nameof(input.Age)) && !controller.ModelState.ContainsKey(nameof(input.Gender)), "Client demographic ModelState values must be removed on an active-registration redisplay.");
    Check(handler.RequestCount == 0, "An active registration must be rejected before inference.");
    Check(context.SaveChangesAsyncCalls == 0 && !context.ChangeTracker.Entries().Any(), "An active registration must not save or attach an assessment graph.");
}

// Assessment POST: profile demographics, not client-posted values, control the redisplayed values and age policy.
{
    using var context = NewContext();
    var handler = new CountingHandler();
    var controller = NewAssessmentController(context, handler, futureEvent, hasActiveRegistration: false,
        demographics: ParticipantDemographicsResult.Complete(30, ParticipantDemographics.Female));
    var input = ValidAssessmentInput();
    input.Age = 18;
    input.Gender = ParticipantDemographics.Male;
    var result = await controller.Form(input);

    Check(result is ViewResult, "A later validation failure must redisplay the assessment form.");
    Check(input.Age == 30 && input.Gender == ParticipantDemographics.Female, "Forged posted demographics must be overwritten from the authoritative profile.");
    Check(handler.RequestCount == 0, "A mapping failure after demographic hydration must not call inference.");
}

{
    using var context = NewContext();
    var handler = new CountingHandler();
    var controller = NewAssessmentController(context, handler, futureEvent, hasActiveRegistration: false,
        demographics: ParticipantDemographicsResult.Complete(17, ParticipantDemographics.Female));
    var input = ValidAssessmentInput();
    input.Age = 30;
    var result = await controller.Form(input);

    Check(result is ViewResult && controller.ModelState.ErrorCount > 0, "An authoritative out-of-range age must reject submission.");
    Check(input.Age == 17, "A forged in-range age must not replace the authoritative out-of-range age.");
    Check(handler.RequestCount == 0, "An authoritative out-of-range age must reject before inference.");
}

// Assessment POST: malformed medical answers and missing consent are rejected before inference and persistence.
foreach (var input in new[]
{
    ValidAssessmentInput(medicalConditions: ["Unknown condition"]),
    ValidAssessmentInput(medicalConditions: ["Asthma / lung-related condition", "Asthma / lung-related condition"]),
    ValidAssessmentInput(medicalConditions: [AssessmentSubmissionGuards.NoneOfTheAbove, "Joint or knee injury"]),
    ValidAssessmentInput(consentGiven: false),
    ValidAssessmentInput(dataPrivacyConsent: false)
})
{
    using var context = NewContext();
    var handler = new CountingHandler();
    var controller = NewAssessmentController(context, handler, futureEvent, hasActiveRegistration: false);
    var result = await controller.Form(input);

    Check(result is ViewResult, "Invalid medical selections or missing consent must redisplay the assessment form.");
    Check(controller.ModelState.ErrorCount > 0, "Invalid medical selections or missing consent must add a model error.");
    Check(handler.RequestCount == 0, "Invalid medical selections or missing consent must not call inference.");
    Check(context.SaveChangesAsyncCalls == 0 && !context.ChangeTracker.Entries().Any(), "Invalid medical selections or missing consent must not save or attach an assessment graph.");
}

if (!assessmentOnly)
{
// Registration POST: plan and clearance checks run through the real controller action before uploads or SaveChanges.
{
    using var context = NewContext();
    var controller = NewRegistrationController(
        context,
        futureEvent,
        new Assessment { Id = 51, EventId = futureEvent.Id, UserId = "participant-1", IsActive = true, Result = "Not Recommended" });
    var result = await controller.Register(futureEvent.Id, 51, "Participant", "p@example.test", "1", "Emergency", "2", "Main gate", null, "  ");

    Check(IsRegisterRedirect(result), "A Not Recommended registration without a preparation plan must return to Register.");
    Check(TempDataError(controller).Contains("preparation plan", StringComparison.OrdinalIgnoreCase), "The preparation-plan rejection must explain the missing plan.");
    Check(context.SaveChangesAsyncCalls == 0 && !context.ChangeTracker.Entries().Any(), "A missing preparation plan must not save or attach a registration.");
}

{
    using var context = NewContext();
    var controller = NewRegistrationController(
        context,
        futureEvent,
        new Assessment { Id = 52, EventId = futureEvent.Id, UserId = "participant-1", IsActive = true, Result = "Good-Match", MedicalClearanceRequired = true });
    var result = await controller.Register(futureEvent.Id, 52, "Participant", "p@example.test", "1", "Emergency", "2", "Main gate", null, "A suitable preparation plan");

    Check(IsRegisterRedirect(result), "A screening-required registration without clearance must return to Register.");
    Check(TempDataError(controller).Contains("medical clearance", StringComparison.OrdinalIgnoreCase), "The clearance rejection must explain the missing document.");
    Check(context.SaveChangesAsyncCalls == 0 && !context.ChangeTracker.Entries().Any(), "A missing clearance document must not save or attach a registration.");
}

// Organizer decision POST: blank Not Recommended approval reasons and non-owner decisions are denied before SaveChanges.
{
    using var context = NewContext();
    var registration = PendingRegistration(61, "organizer-a", "Not Recommended");
    var controller = NewOrganizerController(context, new ApplicationUser { Id = "organizer-a" }, registration);
    var result = await controller.UpdateRegistrationStatus(new OrganizerController.UpdateRegistrationStatusRequest
    {
        Id = registration.Id,
        Status = "Accepted",
        Reason = " \t "
    });

    Check(JsonProperty(result, "success") == "false", "A blank reason must reject approval of a Not Recommended registration.");
    Check(JsonProperty(result, "message").Contains("reason is required", StringComparison.OrdinalIgnoreCase), "The blank-reason rejection must identify the required reason.");
    Check(context.SaveChangesAsyncCalls == 0, "A blank Not Recommended approval reason must not save a decision.");
    Check(controller.Calls.SequenceEqual(["lookup", "begin", "lock", "lookup", "dispose"]),
        "The blank-reason guard must run after the locked reload and dispose its transaction without committing.");
    Check(registration.Status == "Pending" && registration.DecisionReason == null,
        "A blank reason must not mutate the pending decision.");
}

{
    using var context = NewContext();
    var registration = PendingRegistration(62, "organizer-a", "Good-Match");
    var controller = NewOrganizerController(context, new ApplicationUser { Id = "organizer-b" }, registration);
    var result = await controller.UpdateRegistrationStatus(new OrganizerController.UpdateRegistrationStatusRequest
    {
        Id = registration.Id,
        Status = "Accepted",
        Reason = "Reviewed"
    });

    Check(JsonProperty(result, "success") == "false", "An unrelated organizer must not approve another organizer's registration.");
    Check(JsonProperty(result, "message") == "Registration not found", "Cross-organizer denial must not disclose the registration.");
    Check(context.SaveChangesAsyncCalls == 0, "An unrelated organizer must not save a decision.");
    Check(controller.Calls.SequenceEqual(["lookup"]), "An unrelated organizer must be denied before locking.");
}

// The authoritative reload must still reject changed ownership/status before reason validation.
foreach (var changedOwner in new[] { false, true })
{
    using var context = NewContext();
    var initial = PendingRegistration(63, "organizer-a", "Not Recommended");
    var locked = PendingRegistration(63, changedOwner ? "organizer-b" : "organizer-a", "Not Recommended");
    if (!changedOwner) locked.Status = "Cancelled";
    var controller = NewOrganizerController(context, new ApplicationUser { Id = "organizer-a" }, initial);
    controller.LockedRegistration = locked;
    var result = await controller.UpdateRegistrationStatus(new() { Id = initial.Id, Status = "Accepted", Reason = " \t " });
    Check(JsonProperty(result, "success") == "false", "A changed locked registration must reject the decision.");
    Check(JsonProperty(result, "message") == (changedOwner ? "Registration not found" : "This registration is no longer pending review."),
        "The locked ownership/status check must precede the blank-reason check.");
    Check(context.SaveChangesAsyncCalls == 0 && controller.Calls.SequenceEqual(["lookup", "begin", "lock", "lookup", "dispose"]),
        "A stale decision must dispose without saving or committing.");
}
}

Console.WriteLine($"PASS: {assertions} assertions{(assessmentOnly ? " (assessment guards only)" : string.Empty)}. Controller actions used query/transaction/lock doubles and a fake HTTP handler. A connection interceptor forbids database access; no MVC host or real persistence transaction was run.");

static TrailGuardV2AssessmentFormInput ValidAssessmentInput(
    string[]? medicalConditions = null,
    bool consentGiven = true,
    bool dataPrivacyConsent = true) => new()
{
    EventId = 41,
    MedicalConditions = medicalConditions ?? [AssessmentSubmissionGuards.NoneOfTheAbove],
    ConsentGiven = consentGiven,
    DataPrivacyConsent = dataPrivacyConsent
};

static CountingDbContext NewContext()
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=trailguard_submission_guard_verification;Username=unused;Password=unused;Timeout=1;Command Timeout=1")
        .AddInterceptors(new ForbidConnectionInterceptor())
        .Options;
    return new CountingDbContext(options);
}

static TestAssessmentController NewAssessmentController(CountingDbContext context, CountingHandler handler, Event eventItem, bool hasActiveRegistration, ParticipantDemographicsResult? demographics = null)
{
    var controller = new TestAssessmentController(
        context,
        new SuitabilityApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1/") }, NullLogger<SuitabilityApiClient>.Instance),
        new TrailGuardV2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1/") }, NullLogger<TrailGuardV2ApiClient>.Instance),
        new TrailGuardV2AssessmentRequestMapper(),
        NullLogger<AssessmentController>.Instance)
    {
        TestEvent = eventItem,
        ActiveRegistration = hasActiveRegistration,
        TestDemographics = demographics ?? ParticipantDemographicsResult.Complete(30, ParticipantDemographics.Female)
    };
    ConfigureController(controller, "participant-1");
    return controller;
}

static TestRegistrationController NewRegistrationController(CountingDbContext context, Event eventItem, Assessment assessment)
{
    var controller = new TestRegistrationController(context, new TestWebHostEnvironment())
    {
        TestEvent = eventItem,
        TestAssessment = assessment,
        TestUser = new ApplicationUser { Id = "participant-1", FirstName = "Participant", LastName = "One" }
    };
    ConfigureController(controller, "participant-1");
    return controller;
}

static TestOrganizerController NewOrganizerController(CountingDbContext context, ApplicationUser currentUser, EventRegistration registration)
{
    var userManager = NewUserManager();
    var controller = new TestOrganizerController(
        context,
        userManager,
        new TestWebHostEnvironment(),
        NullLogger<OrganizerController>.Instance,
        new ProfileAccessService(userManager, context))
    {
        TestCurrentUser = currentUser,
        TestRegistration = registration
    };
    ConfigureController(controller, currentUser.Id);
    return controller;
}

static UserManager<ApplicationUser> NewUserManager() => new(
    new NoopUserStore(),
    Options.Create(new IdentityOptions()),
    new PasswordHasher<ApplicationUser>(),
    Array.Empty<IUserValidator<ApplicationUser>>(),
    Array.Empty<IPasswordValidator<ApplicationUser>>(),
    new UpperInvariantLookupNormalizer(),
    new IdentityErrorDescriber(),
    new EmptyServiceProvider(),
    NullLogger<UserManager<ApplicationUser>>.Instance);

static void ConfigureController(Controller controller, string userId)
{
    var httpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "verification"))
    };
    controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    controller.TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider());
}

static EventRegistration PendingRegistration(int id, string organizerId, string assessmentResult) => new()
{
    Id = id,
    Status = "Pending",
    Event = new Event { Id = 71, OrganizerId = organizerId, EventDate = DateTime.Today.AddDays(7), Status = "Upcoming" },
    Assessment = new Assessment { Result = assessmentResult }
};

static bool IsRegisterRedirect(IActionResult result) => result is RedirectToActionResult redirect
    && redirect.ActionName == "Register";

static string TempDataError(Controller controller) => controller.TempData["Error"]?.ToString() ?? string.Empty;

static string JsonProperty(IActionResult result, string name)
{
    var json = result as JsonResult ?? throw new InvalidOperationException("Expected a JSON response.");
    using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
    var property = document.RootElement.GetProperty(name);
    return property.ValueKind == JsonValueKind.String ? property.GetString()! : property.GetRawText();
}

sealed class TestAssessmentController : AssessmentController
{
    public TestAssessmentController(ApplicationDbContext context, SuitabilityApiClient historicalApi, TrailGuardV2ApiClient v2Api, TrailGuardV2AssessmentRequestMapper mapper, ILogger<AssessmentController> logger)
        : base(context, historicalApi, v2Api, mapper, logger) { }

    public Event? TestEvent { get; init; }
    public bool ActiveRegistration { get; init; }
    public ParticipantDemographicsResult TestDemographics { get; init; } = ParticipantDemographicsResult.Complete(30, ParticipantDemographics.Female);

    protected override Task<Event?> FindEventAsync(int eventId) => Task.FromResult(TestEvent);
    protected override Task<bool> HasActiveRegistrationAsync(int eventId, string userId) => Task.FromResult(ActiveRegistration);
    protected override Task<bool> HasActiveAssessmentAsync(int eventId, string? userId) => Task.FromResult(false);
    protected override Task<ParticipantDemographicsResult> HydrateAuthoritativeDemographicsAsync(TrailGuardV2AssessmentFormInput input, string userId)
    {
        input.Age = TestDemographics.Age;
        input.Gender = TestDemographics.Gender;
        ModelState.Remove(nameof(input.Age));
        ModelState.Remove(nameof(input.Gender));
        return Task.FromResult(TestDemographics);
    }
    protected override Task<Event?> PopulateAssessmentFormViewBagAsync(int eventId, string? userId)
    {
        ViewBag.Event = TestEvent;
        ViewBag.RetakeMode = false;
        return Task.FromResult(TestEvent);
    }
}

sealed class TestRegistrationController : RegistrationController
{
    public TestRegistrationController(ApplicationDbContext context, IWebHostEnvironment environment) : base(context, environment) { }

    public Event? TestEvent { get; init; }
    public Assessment? TestAssessment { get; init; }
    public ApplicationUser? TestUser { get; init; }

    protected override Task<Event?> FindEventAsync(int eventId) => Task.FromResult(TestEvent);
    protected override Task<ApplicationUser?> FindUserAsync(string? userId) => Task.FromResult(TestUser);
    protected override Task<Assessment?> FindActiveAssessmentAsync(int assessmentId, int eventId, string? userId) => Task.FromResult(TestAssessment);
    protected override Task<EventRegistration?> FindExistingRegistrationAsync(int eventId, string? userId) => Task.FromResult<EventRegistration?>(null);
    protected override Task<int> CountActiveRegistrationsAsync(int eventId) => Task.FromResult(0);
}

sealed class TestOrganizerController : OrganizerController
{
    public TestOrganizerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment environment, ILogger<OrganizerController> logger, ProfileAccessService profileAccessService)
        : base(context, userManager, environment, logger, profileAccessService) { }

    public ApplicationUser? TestCurrentUser { get; init; }
    public EventRegistration? TestRegistration { get; init; }
    public EventRegistration? LockedRegistration { get; set; }
    public List<string> Calls { get; } = [];
    private int lookups;

    protected override Task<ApplicationUser?> GetCurrentUserForDecisionAsync() => Task.FromResult(TestCurrentUser);
    protected override Task<EventRegistration?> FindRegistrationForDecisionAsync(int registrationId)
    {
        Calls.Add("lookup");
        return Task.FromResult(++lookups == 1 ? TestRegistration : LockedRegistration ?? TestRegistration);
    }
    protected override Task<IDbContextTransaction> BeginDecisionTransactionAsync()
    {
        Calls.Add("begin");
        return Task.FromResult<IDbContextTransaction>(new GuardTransaction(Calls));
    }
    protected override Task AcquireDecisionEventLockAsync(int eventId)
    {
        if (eventId != TestRegistration!.Event!.Id) throw new InvalidOperationException("Wrong event lock.");
        Calls.Add("lock");
        return Task.CompletedTask;
    }
}

sealed class GuardTransaction(List<string> calls) : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();
    public void Commit() => throw new InvalidOperationException("A denied decision must not commit.");
    public Task CommitAsync(CancellationToken cancellationToken = default) { Commit(); return Task.CompletedTask; }
    public void Rollback() => calls.Add("rollback");
    public Task RollbackAsync(CancellationToken cancellationToken = default) { Rollback(); return Task.CompletedTask; }
    public void Dispose() => calls.Add("dispose");
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

sealed class ForbidConnectionInterceptor : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => throw new InvalidOperationException("Database access is forbidden in the guard fixture.");
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Database access is forbidden in the guard fixture.");
}

sealed class CountingDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
{
    public int SaveChangesAsyncCalls { get; private set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesAsyncCalls++;
        return Task.FromResult(0);
    }
}

sealed class CountingHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}

sealed class MemoryTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

sealed class NoopUserStore : IUserStore<ApplicationUser>
{
    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}

sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "TrailGuardVerification";
    public string EnvironmentName { get; set; } = "Verification";
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
