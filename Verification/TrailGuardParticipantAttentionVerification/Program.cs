using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
var now = new DateTime(2026, 10, 5, 9, 0, 0);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

Event Event(int id, string title, int capacity = 3, string status = "Upcoming", DateTime? eventDate = null) => new()
{
    Id = id,
    EventTitle = title,
    EventDate = eventDate ?? now.Date.AddDays(7),
    Status = status,
    Capacity = capacity
};

EventRegistration Registration(int id, int eventId, string status, Event eventItem, DateTime registeredAt, int? alternativeEventId = null, Event? alternativeEvent = null, DateTime? paymentDeadline = null, string? receipt = null) => new()
{
    Id = id,
    EventId = eventId,
    Status = status,
    Event = eventItem,
    AlternativeEventId = alternativeEventId,
    AlternativeEvent = alternativeEvent,
    RegisteredAt = registeredAt,
    PaymentDeadline = paymentDeadline,
    PaymentReceiptUrl = receipt,
    UserId = "participant-under-test"
};

Assessment Assessment(int id, Event eventItem, DateTime submittedAt, bool clearance = true, bool active = true, string? result = null) => new()
{
    Id = id,
    EventId = eventItem.Id,
    Event = eventItem,
    IsActive = active,
    MedicalClearanceRequired = clearance,
    Result = result,
    SubmittedAt = submittedAt,
    UserId = "participant-under-test"
};

List<ParticipantAttentionItem> Build(
    IEnumerable<EventRegistration>? registrations = null,
    IEnumerable<Assessment>? assessments = null,
    IReadOnlyDictionary<int, bool>? capacity = null,
    IReadOnlyDictionary<int, bool>? supported = null) => ParticipantAttentionBuilder.Build(
        registrations ?? [], assessments ?? [], capacity ?? new Dictionary<int, bool>(),
        supported ?? new Dictionary<int, bool>(), now);

Check(!EventJoinabilityHelper.IsJoinable(Event(100, "Past", eventDate: now.Date.AddDays(-1)), now),
    "An Upcoming event before the supplied date must not be joinable for new registration.");
Check(EventJoinabilityHelper.IsJoinable(Event(101, "Today", eventDate: now.Date), now),
    "An Upcoming event on the supplied date must remain joinable.");
Check(EventJoinabilityHelper.IsJoinable(Event(102, "Future", eventDate: now.Date.AddDays(1)), now),
    "An Upcoming event after the supplied date must remain joinable.");

var paymentEvent = Event(1, "Mt. Makiling", eventDate: now.Date.AddDays(-1));
var waitingPayment = Registration(10, 1, "Awaiting Payment", paymentEvent, now.AddDays(-2), paymentDeadline: now.AddDays(2), receipt: "/rejected-receipt.pdf");
var pending = Registration(11, 11, "Pending", Event(11, "Pending review"), now.AddDays(-1));
var verification = Registration(12, 12, "For Payment Verification", Event(12, "Payment review"), now.AddDays(-1));
var expired = Registration(13, 13, "Awaiting Payment", Event(13, "Expired payment"), now.AddDays(-3), paymentDeadline: now.AddMinutes(-1));
var paymentItems = Build([waitingPayment, pending, verification, expired]);
Check(paymentItems.Count(item => item.Kind == ParticipantAttentionKind.Payment) == 1,
    "Only a current, unexpired Awaiting Payment registration may create a payment reminder.");
Check(paymentItems.Single().RegistrationId == waitingPayment.Id,
    "A payment reminder must keep its registration link even when a rejected receipt remains on the record.");
Check(paymentItems.Single().RegistrationId == waitingPayment.Id && paymentItems.Single().Kind == ParticipantAttentionKind.Payment,
    "A valid payment reminder must remain visible for an Upcoming event whose date has passed.");

var nullDeadlinePayment = Registration(14, 14, "Awaiting Payment", Event(14, "No recorded deadline"), now.AddDays(-1));
var nullDeadlineItems = Build([nullDeadlinePayment]);
Check(nullDeadlineItems.Count == 1 && nullDeadlineItems.Single().Kind == ParticipantAttentionKind.Payment,
    "An otherwise eligible Awaiting Payment registration without a recorded deadline must remain actionable.");
Check(nullDeadlineItems.Single().RegistrationId == nullDeadlinePayment.Id
      && nullDeadlineItems.Single().Detail == "Upload your payment receipt using the instructions in your registration.",
    "A null-deadline payment reminder must retain its registration link and use deadline-free guidance.");

var cancelledEvent = Event(2, "Cancelled hike", status: "Cancelled");
var completedEvent = Event(3, "Completed hike", status: "Completed");
Check(!Build([
        Registration(20, 2, "Awaiting Payment", cancelledEvent, now, paymentDeadline: now.AddDays(1)),
        Registration(21, 3, "Awaiting Payment", completedEvent, now, paymentDeadline: now.AddDays(1))
    ]).Any(), "Cancelled and completed events must not create payment reminders.");

var clearanceEvent = Event(4, "Mt. Arayat");
var clearanceAssessment = Assessment(30, clearanceEvent, now.AddHours(-2));
var clearanceItems = Build(assessments: [clearanceAssessment], capacity: new Dictionary<int, bool> { [4] = true }, supported: new Dictionary<int, bool> { [30] = true });
Check(clearanceItems.Single().Kind == ParticipantAttentionKind.MedicalClearance,
    "A supported, active clearance assessment for a joinable event with capacity must be actionable.");
var policyClearanceAssessment = Assessment(32, clearanceEvent, now.AddHours(-1), clearance: false, result: "Not Recommended");
Check(Build(assessments: [policyClearanceAssessment], capacity: new Dictionary<int, bool> { [4] = true }, supported: new Dictionary<int, bool> { [32] = true })
        .Single().Kind == ParticipantAttentionKind.MedicalClearance,
    "A supported Not Recommended assessment must create the same medical-clearance registration reminder without changing its other eligibility rules.");
Check(!Build(assessments: [clearanceAssessment], capacity: new Dictionary<int, bool> { [4] = true }, supported: new Dictionary<int, bool> { [30] = false }).Any(),
    "An invalid or unsupported prediction must not create a medical-clearance reminder.");
Check(!Build([Registration(31, 4, "Pending", clearanceEvent, now)], [clearanceAssessment], new Dictionary<int, bool> { [4] = true }, new Dictionary<int, bool> { [30] = true }).Any(),
    "A blocking registration must remove the medical-clearance reminder after submission.");
Check(!Build(assessments: [clearanceAssessment], capacity: new Dictionary<int, bool> { [4] = false }, supported: new Dictionary<int, bool> { [30] = true }).Any(),
    "A full event must not advertise a registration action.");

var oldClearance = Assessment(40, clearanceEvent, now.AddDays(-2));
var currentNonClearance = Assessment(41, clearanceEvent, now.AddDays(-1), clearance: false);
Check(!Build(assessments: [oldClearance, currentNonClearance], capacity: new Dictionary<int, bool> { [4] = true }, supported: new Dictionary<int, bool> { [40] = true, [41] = true }).Any(),
    "The newest active assessment per event is selected deterministically without falling back through history.");

var original = Event(5, "Original hike");
var alternative = Event(6, "Suggested hike");
var alternativeRegistration = Registration(50, 5, "Alternative Recommended", original, now.AddDays(-1), 6, alternative);
var alternativeItems = Build([alternativeRegistration], capacity: new Dictionary<int, bool> { [6] = true });
Check(alternativeItems.Single().Kind == ParticipantAttentionKind.AlternativeRecommendation,
    "A joinable suggested event with capacity must be presented as an alternative recommendation.");
Check(!Build([alternativeRegistration, Registration(51, 6, "Accepted", alternative, now)], capacity: new Dictionary<int, bool> { [6] = true }).Any(),
    "An alternative recommendation must be suppressed when the participant already has a blocking registration for the suggested event.");
Check(!Build([alternativeRegistration], capacity: new Dictionary<int, bool> { [6] = false }).Any(),
    "An unavailable alternative event must be suppressed.");

var pastOriginal = Event(14, "Past original", eventDate: now.Date.AddDays(-1));
var futureAlternative = Event(15, "Future suggested", eventDate: now.Date.AddDays(1));
var pastOriginalRecommendation = Registration(52, 14, "Alternative Recommended", pastOriginal, now.AddDays(-1), 15, futureAlternative);
Check(Build([pastOriginalRecommendation], capacity: new Dictionary<int, bool> { [15] = true }).Single().RegistrationId == 52,
    "A past-dated Upcoming original event must not suppress a valid future alternative recommendation.");
Check(!Build([
        Registration(53, 16, "Alternative Recommended", Event(16, "Cancelled original", status: "Cancelled"), now, 15, futureAlternative),
        Registration(54, 17, "Alternative Recommended", Event(17, "Completed original", status: "Completed"), now, 15, futureAlternative)
    ], capacity: new Dictionary<int, bool> { [15] = true }).Any(),
    "Cancelled and completed original events must not create alternative reminders.");

var historicalPayment = Registration(60, 7, "Awaiting Payment", Event(7, "Historical"), now.AddDays(-2), paymentDeadline: now.AddDays(1));
var latestCancelled = Registration(61, 7, "Cancelled", historicalPayment.Event!, now.AddDays(-1));
Check(!Build([historicalPayment, latestCancelled]).Any(),
    "A later cancelled registration must suppress an older historical payment row for the same event.");

var ordered = Build(
    [Registration(71, 8, "Awaiting Payment", Event(8, "Payment B"), now, paymentDeadline: now.AddDays(3)),
     Registration(70, 9, "Awaiting Payment", Event(9, "Payment A"), now, paymentDeadline: now.AddDays(1)),
     alternativeRegistration],
    [Assessment(72, Event(10, "Clearance"), now.AddDays(-1))],
    new Dictionary<int, bool> { [6] = true, [10] = true },
    new Dictionary<int, bool> { [72] = true });
Check(ordered.Select(item => item.Kind).SequenceEqual([
        ParticipantAttentionKind.Payment, ParticipantAttentionKind.Payment,
        ParticipantAttentionKind.MedicalClearance, ParticipantAttentionKind.AlternativeRecommendation]),
    "Attention items must retain the documented type order and deterministic date/ID ordering.");
Check(ordered[0].RegistrationId == 70 && ordered[1].RegistrationId == 71,
    "Payment reminders must be ordered by deadline before their stable registration identifier.");

var invalidSelection = SuitabilityResultSelector.Select([new SuitabilityResult
{
    Id = 90,
    ModelVersion = TrailGuardV2ResponseValidator.ExpectedModelVersion,
    PredictedAt = now
}]);
Check(invalidSelection.IsInvalidOrUnsupported,
    "The validation path must reject a malformed current v2 prediction rather than treating it as usable.");

var root = Directory.GetCurrentDirectory();
var participantController = File.ReadAllText(Path.Combine(root, "Controllers", "ParticipantController.cs"));
Check(participantController.Contains("Where(r => r.UserId == userId)"),
    "Dashboard attention queries must remain scoped to the signed-in participant.");
Check(participantController.Contains("SuitabilityResultSelector.Select(group).IsRecognizedV2"),
    "Medical-clearance reminders must use the existing supported-prediction selector.");
var attentionBuilder = File.ReadAllText(Path.Combine(root, "Services", "ParticipantAttentionBuilder.cs"));
Check(attentionBuilder.Contains("EventJoinabilityHelper.IsJoinable(assessment.Event, now)")
      && attentionBuilder.Contains("EventJoinabilityHelper.IsJoinable(registration.AlternativeEvent, now)"),
    "New-registration actions must evaluate joinability with the supplied reminder date.");

Console.WriteLine($"TrailGuard participant attention verification passed ({assertions} assertions).");
