using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var cancelledEvent = new Event
{
    Status = RegistrationStatusHelper.CancelledEventStatus,
    CancelledAt = new DateTime(2026, 10, 4, 9, 30, 0),
    CancellationReason = "Weather advisory"
};
var historicalRegistration = new EventRegistration
{
    Event = cancelledEvent,
    Status = "Awaiting Payment",
    IsPaid = false,
    PaymentReceiptUrl = "receipts/original.pdf",
    PaymentDeadline = new DateTime(2026, 10, 5)
};
var originalStatus = historicalRegistration.Status;
var originalPaid = historicalRegistration.IsPaid;
var originalReceipt = historicalRegistration.PaymentReceiptUrl;
var originalDeadline = historicalRegistration.PaymentDeadline;

Check(RegistrationStatusHelper.IsEventCancelled(cancelledEvent), "Cancelled Events must be identified independently of registration status.");
Check(!RegistrationStatusHelper.CountsAsActiveRegistration(historicalRegistration), "A cancelled Event's registration must not count as active.");
Check(!RegistrationStatusHelper.NeedsParticipantAction(historicalRegistration), "A cancelled Event's Awaiting Payment registration must not need participant action.");
Check(RegistrationStatusHelper.IsClosedForParticipantSummary(historicalRegistration), "A cancelled Event's registration must count as closed in My Registrations.");
Check(historicalRegistration.Status == originalStatus && historicalRegistration.IsPaid == originalPaid
    && historicalRegistration.PaymentReceiptUrl == originalReceipt && historicalRegistration.PaymentDeadline == originalDeadline,
    "Cancellation categorization must not rewrite the historical registration, payment, receipt, or deadline fields.");

var upcomingRegistration = new EventRegistration
{
    Event = new Event { Status = "Upcoming" },
    Status = "Awaiting Payment"
};
Check(RegistrationStatusHelper.CountsAsActiveRegistration(upcomingRegistration), "An Upcoming Event's Awaiting Payment registration must remain active.");
Check(RegistrationStatusHelper.NeedsParticipantAction(upcomingRegistration), "An Upcoming Event's Awaiting Payment registration must still need participant action.");
Check(!RegistrationStatusHelper.IsClosedForParticipantSummary(upcomingRegistration), "An Upcoming Event's Awaiting Payment registration must not be categorized as closed.");

var root = FindRepositoryRoot();
var registrationController = File.ReadAllText(Path.Combine(root, "Controllers", "RegistrationController.cs"));
var organizerController = File.ReadAllText(Path.Combine(root, "Controllers", "OrganizerController.cs"));
var eventController = File.ReadAllText(Path.Combine(root, "Controllers", "EventController.cs"));
var statusHelper = File.ReadAllText(Path.Combine(root, "Services", "RegistrationStatusHelper.cs"));
var participantView = File.ReadAllText(Path.Combine(root, "Views", "Registration", "MyRegistrations.cshtml"));

Check(Count(registrationController, "RegistrationStatusHelper.IsEventCancelled") >= 3,
    "Participant cancellation and receipt processing must check the Event status before and after the event lock.");
Check(Count(organizerController, "RegistrationStatusHelper.IsEventCancelled") >= 6,
    "Organizer decision, payment, and alternative endpoints must recheck cancellation after their locked reloads.");
Check(statusHelper.Contains("r.Event.Status != CancelledEventStatus", StringComparison.Ordinal),
    "Overdue-payment expiry must exclude cancelled Events in both its candidate and post-lock queries.");
var cancelEventAction = eventController[eventController.IndexOf("public async Task<JsonResult> CancelEvent", StringComparison.Ordinal)..];
cancelEventAction = cancelEventAction[..cancelEventAction.IndexOf("public class CancelEventRequest", StringComparison.Ordinal)];
Check(!cancelEventAction.Contains("EventRegistrations", StringComparison.Ordinal),
    "Cancelling an Event must not rewrite any registration row.");
Check(participantView.Contains("Event Cancelled", StringComparison.Ordinal)
    && participantView.Contains("escapeHtml(reg.cancellationReason)", StringComparison.Ordinal),
    "My Registrations and its generated details modal must render the cancellation notice and encode its reason.");

Console.WriteLine($"PASS: {assertions} cancellation-policy assertions. This verifier uses in-memory models and static source checks only; it does not open a database connection or upload a file.");

static int Count(string text, string value)
{
    var count = 0;
    var start = 0;
    while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
    {
        count++;
        start += value.Length;
    }
    return count;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrailGuard.csproj"))) return directory.FullName;
        }
    }

    throw new InvalidOperationException("Repository root was not found.");
}
