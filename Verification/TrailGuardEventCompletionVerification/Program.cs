using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TrailGuard.Controllers;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
var root = Directory.GetCurrentDirectory();

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
Event Scheduled(string status, DateOnly date) => new() { Status = status, EventDate = date.ToDateTime(TimeOnly.MinValue) };

var today = new DateOnly(2026, 10, 21);
var clock = new FixedPhilippineClock(today);

Check(EventCompletionEligibilityPolicy.CanComplete(Scheduled("Upcoming", today), clock), "An Upcoming event scheduled today must be completable.");
Check(EventCompletionEligibilityPolicy.CanComplete(Scheduled("Upcoming", today.AddDays(-1)), clock), "An Upcoming event scheduled in the past must be completable.");
Check(!EventCompletionEligibilityPolicy.CanComplete(Scheduled("Upcoming", today.AddDays(1)), clock), "A future Upcoming event must be rejected.");
Check(!EventCompletionEligibilityPolicy.CanComplete(Scheduled("Completed", today), clock), "A Completed event must be rejected.");
Check(!EventCompletionEligibilityPolicy.CanComplete(Scheduled("Cancelled", today), clock), "A Cancelled event must be rejected.");
Check(!EventCompletionEligibilityPolicy.CanComplete(Scheduled("Postponed", today), clock), "A non-Upcoming event must be rejected.");
Check(EventCompletionEligibilityPolicy.IneligibilityMessage(Scheduled("Upcoming", today.AddDays(1)), clock) == EventCompletionEligibilityPolicy.FutureDateMessage,
    "Future events must return the actionable completion-date message.");

var midnightBoundaryClock = new PhilippineClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 16, 30, 0, TimeSpan.Zero)));
Check(midnightBoundaryClock.Today == today, "The Philippine calendar date must advance independently of the UTC host date.");
Check(EventCompletionEligibilityPolicy.CanComplete(Scheduled("Upcoming", today), midnightBoundaryClock), "The Philippine scheduled date must be allowed at the UTC/Philippine midnight boundary.");
Check(!EventCompletionEligibilityPolicy.CanComplete(Scheduled("Upcoming", today.AddDays(1)), midnightBoundaryClock), "The next Philippine calendar date must remain ineligible at the midnight boundary.");

Event Upcoming(DateOnly date, string organizerId = "organizer") => new()
{
    Id = 42,
    Status = "Upcoming",
    EventDate = date.ToDateTime(TimeOnly.MinValue),
    OrganizerId = organizerId,
    OrganizedBy = "Organizer Owner"
};

bool IsSuccess(JsonResult result) => JsonSerializer.Serialize(result.Value).Contains("\"success\":true", StringComparison.Ordinal);

var initiallyFutureStore = new FakeEventLifecycleStore(Upcoming(today.AddDays(1)), Upcoming(today.AddDays(1)));
var initiallyFutureResult = await new FakeEventController(initiallyFutureStore, clock).CompleteEvent(new EventController.CompleteEventRequest { Id = 42 });
Check(!IsSuccess(initiallyFutureResult) && initiallyFutureStore.BeginCount == 0 && initiallyFutureStore.SaveCount == 0
      && initiallyFutureStore.Found!.Status == "Upcoming",
    "An initially future event must reject before transaction, mutation, or save.");

var futureAfterReload = new FakeEventLifecycleStore(Upcoming(today), Upcoming(today.AddDays(1)));
futureAfterReload.Registrations.Add(new EventRegistration { EventId = 42, Status = "Pending" });
var futureAfterReloadResult = await new FakeEventController(futureAfterReload, clock).CompleteEvent(new EventController.CompleteEventRequest { Id = 42 });
Check(!IsSuccess(futureAfterReloadResult) && futureAfterReload.SaveCount == 0 && futureAfterReload.Reloaded!.Status == "Upcoming"
      && futureAfterReload.Registrations.Single().Status == "Pending" && futureAfterReload.LastTransaction is { Disposed: true, Committed: false },
    "An event becoming future under the lock must reject without mutation, voiding, save, or commit.");

var completedAfterReload = new FakeEventLifecycleStore(Upcoming(today), new Event { Id = 42, Status = "Completed", EventDate = today.ToDateTime(TimeOnly.MinValue), OrganizerId = "organizer" });
var completedAfterReloadResult = await new FakeEventController(completedAfterReload, clock).CompleteEvent(new EventController.CompleteEventRequest { Id = 42 });
Check(!IsSuccess(completedAfterReloadResult) && completedAfterReload.SaveCount == 0 && completedAfterReload.Reloaded!.CompletedAt is null
      && completedAfterReload.LastTransaction is { Disposed: true, Committed: false },
    "An event becoming Completed under the lock must reject without altering completion metadata or saving.");

var ownershipChangedAfterReload = new FakeEventLifecycleStore(Upcoming(today), Upcoming(today, "different-organizer"));
var ownershipChangedResult = await new FakeEventController(ownershipChangedAfterReload, clock).CompleteEvent(new EventController.CompleteEventRequest { Id = 42 });
Check(!IsSuccess(ownershipChangedResult) && ownershipChangedAfterReload.SaveCount == 0 && ownershipChangedAfterReload.AuthorizationChecks == 2
      && ownershipChangedAfterReload.LastTransaction is { Disposed: true, Committed: false },
    "Ownership must be rechecked against the authoritative locked reload.");

var validCompletion = new FakeEventLifecycleStore(Upcoming(today), Upcoming(today));
validCompletion.Registrations.AddRange([
    new EventRegistration { EventId = 42, Status = "Pending" },
    new EventRegistration { EventId = 42, Status = "Awaiting Payment" },
    new EventRegistration { EventId = 42, Status = "For Payment Verification" },
    new EventRegistration { EventId = 42, Status = "Accepted" },
    new EventRegistration { EventId = 42, Status = "Rejected" }
]);
var validCompletionResult = await new FakeEventController(validCompletion, clock).CompleteEvent(new EventController.CompleteEventRequest { Id = 42 });
Check(IsSuccess(validCompletionResult) && validCompletion.SaveCount == 1 && validCompletion.LastTransaction is { Disposed: true, Committed: true }
      && validCompletion.Reloaded is { Status: "Completed", CompletedBy: "Organizer Owner", CompletedAt: not null }
      && validCompletion.Reloaded.DateUpdated != default
      && validCompletion.Registrations.Select(registration => registration.Status).SequenceEqual(["Voided", "Voided", "Voided", "Accepted", "Rejected"]),
    "Valid completion must save metadata atomically and void exactly Pending, Awaiting Payment, and For Payment Verification registrations.");

var staleReschedule = new FakeEventLifecycleStore(Upcoming(today), new Event { Id = 42, Status = "Completed", EventDate = today.ToDateTime(TimeOnly.MinValue), OrganizerId = "organizer" });
var staleRescheduleResult = await new FakeEventController(staleReschedule, clock).RescheduleEvent(new EventController.RescheduleEventRequest { Id = 42, NewDate = today.AddDays(4).ToDateTime(TimeOnly.MinValue), NewTime = TimeSpan.FromHours(7) });
Check(!IsSuccess(staleRescheduleResult) && staleReschedule.SaveCount == 0 && staleReschedule.Reloaded!.EventDate.Date == today.ToDateTime(TimeOnly.MinValue).Date
      && staleReschedule.LastTransaction is { Disposed: true, Committed: false },
    "A stale reschedule must be rejected after completion without changing the completed event.");

var directEditStore = new FakeEventLifecycleStore(Upcoming(today), Upcoming(today));
var directEditResult = await new FakeEventController(directEditStore, clock).EditEvent(new EventEditModel { Status = "Completed" });
Check(!IsSuccess(directEditResult) && directEditStore.FindCount == 1 && directEditStore.AuthorizationChecks == 1 && directEditStore.SaveCount == 0,
    "A direct EditEvent Completed submission must reject after ownership validation but before mutation.");

var controller = Read("Controllers", "EventController.cs");
var details = Read("Views", "Event", "Details.cshtml");
var index = Read("Views", "Event", "Index.cshtml");
var policySource = Read("Services", "EventCompletionEligibilityPolicy.cs");
var lifecycleStoreSource = Read("Services", "EventLifecycleStore.cs");
var rescheduleStart = controller.IndexOf("public async Task<JsonResult> RescheduleEvent", StringComparison.Ordinal);
var rescheduleEnd = controller.IndexOf("public class RescheduleEventRequest", rescheduleStart, StringComparison.Ordinal);
var reschedule = controller[rescheduleStart..rescheduleEnd];
var editStart = controller.IndexOf("public async Task<JsonResult> EditEvent", StringComparison.Ordinal);
var edit = controller[editStart..];

var firstCompletionPolicy = controller.IndexOf("EventCompletionEligibilityPolicy.CanComplete(eventItem, _philippineClock)", StringComparison.Ordinal);
var completionLock = controller.IndexOf("_eventLifecycleStore.AcquireEventLockAsync(request.Id)", StringComparison.Ordinal);
var secondCompletionPolicy = controller.IndexOf("EventCompletionEligibilityPolicy.CanComplete(eventItem, _philippineClock)", firstCompletionPolicy + 1, StringComparison.Ordinal);
var completionMutation = controller.IndexOf("eventItem.Status = \"Completed\"", StringComparison.Ordinal);
Check(firstCompletionPolicy >= 0 && firstCompletionPolicy < completionLock && secondCompletionPolicy > completionLock && secondCompletionPolicy < completionMutation,
    "CompleteEvent must apply the shared policy before work starts and again after the locked authoritative reload, before mutation.");
Check(policySource.Contains(EventCompletionEligibilityPolicy.FutureDateMessage, StringComparison.Ordinal)
      && lifecycleStoreSource.Contains("registration.Status == \"Pending\" || registration.Status == \"Awaiting Payment\" || registration.Status == \"For Payment Verification\"", StringComparison.Ordinal)
      && controller.Contains("eventItem.CompletedAt = DateTime.Now", StringComparison.Ordinal)
      && controller.Contains("eventItem.CompletedBy = organizerName", StringComparison.Ordinal),
    "Completion source must retain future-date feedback, permitted-registration voiding, and completion metadata writes.");
Check(controller.Contains("eventItem == null || !await CanManageEventAsync(eventItem, currentUser)", StringComparison.Ordinal),
    "Completion source must retain authorization before lifecycle mutation.");
Check(controller.Contains("Events must be marked as completed using Mark as Complete.", StringComparison.Ordinal)
      && !index.Contains("editEventStatus", StringComparison.Ordinal)
      && !index.Contains("Status: document.getElementById", StringComparison.Ordinal)
      && !controller.Contains("existingEvent.Status = model.Status", StringComparison.Ordinal),
    "Edit Event must neither render/send status nor allow its persisted status to be overwritten by a forged request.");
Check(reschedule.Contains("_eventLifecycleStore.AcquireEventLockAsync(request.Id)", StringComparison.Ordinal)
      && reschedule.Contains("_eventLifecycleStore.ClearTracking()", StringComparison.Ordinal)
      && reschedule.Contains("eventItem = await _eventLifecycleStore.ReloadEventAsync", StringComparison.Ordinal)
      && reschedule.Contains("eventItem.Status != \"Upcoming\"", StringComparison.Ordinal)
      && edit.Contains("existingEvent.Status == \"Completed\"", StringComparison.Ordinal),
    "Reschedule and Edit must coordinate with completion through the event-wide lock and recheck completed state.");
Check(details.Contains("EventCompletionEligibilityPolicy.CanComplete(Model, PhilippineClock)", StringComparison.Ordinal)
      && details.Contains("id=\"completeEventBtn\" data-tg-submit-action=\"true\" disabled", StringComparison.Ordinal)
      && !details.Contains("completeEventAvailability", StringComparison.Ordinal)
      && !details.Contains("Available on the scheduled event date.", StringComparison.Ordinal),
    "Event Details must derive disabled completion UI from the shared server policy without an orphaned helper description.");

Console.WriteLine($"PASS: {assertions} event-completion policy and source-contract assertions.");

sealed class FixedPhilippineClock(DateOnly today) : IPhilippineClock
{
    public DateOnly Today => today;
}

sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

sealed class FakeEventController : EventController
{
    private readonly ApplicationUser _user = new() { Id = "organizer", FirstName = "Organizer", LastName = "Owner" };
    private readonly FakeEventLifecycleStore _store;

    public FakeEventController(FakeEventLifecycleStore store, IPhilippineClock clock)
        : base(null!, null!, null!, null!, NullLogger<EventController>.Instance, null!, clock, store)
    {
        _store = store;
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _user.Id)], "verification"))
        };
        ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    protected override Task<ApplicationUser?> GetCurrentUserAsync() => Task.FromResult<ApplicationUser?>(_user);
    protected override Task<ApplicationUser?> FindUserByIdAsync(string userId) => Task.FromResult<ApplicationUser?>(_user);
    protected override Task<bool> CanManageEventAsync(Event eventItem, ApplicationUser currentUser)
    {
        _store.AuthorizationChecks++;
        return Task.FromResult(eventItem.OrganizerId == currentUser.Id);
    }
}

sealed class FakeEventLifecycleStore(Event? found, Event? reloaded) : IEventLifecycleStore
{
    public Event? Found { get; } = found;
    public Event? Reloaded { get; } = reloaded;
    public List<EventRegistration> Registrations { get; } = [];
    public int FindCount { get; private set; }
    public int BeginCount { get; private set; }
    public int LockCount { get; private set; }
    public int ClearCount { get; private set; }
    public int SaveCount { get; private set; }
    public int AuthorizationChecks { get; set; }
    public FakeEventLifecycleTransaction? LastTransaction { get; private set; }

    public Task<Event?> FindEventAsync(int eventId)
    {
        FindCount++;
        return Task.FromResult(Found);
    }

    public Task<IEventLifecycleTransaction> BeginTransactionAsync()
    {
        BeginCount++;
        LastTransaction = new FakeEventLifecycleTransaction();
        return Task.FromResult<IEventLifecycleTransaction>(LastTransaction);
    }

    public Task AcquireEventLockAsync(int eventId)
    {
        LockCount++;
        return Task.CompletedTask;
    }

    public void ClearTracking() => ClearCount++;
    public Task<Event?> ReloadEventAsync(int eventId) => Task.FromResult(Reloaded);
    public Task<List<EventRegistration>> GetRegistrationsToVoidAsync(int eventId) =>
        Task.FromResult(Registrations.Where(registration => registration.EventId == eventId &&
            (registration.Status == "Pending" || registration.Status == "Awaiting Payment" || registration.Status == "For Payment Verification")).ToList());
    public Task SaveChangesAsync()
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

sealed class FakeEventLifecycleTransaction : IEventLifecycleTransaction
{
    public bool Committed { get; private set; }
    public bool Disposed { get; private set; }
    public Task CommitAsync()
    {
        Committed = true;
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
