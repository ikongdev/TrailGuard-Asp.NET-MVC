using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Builds dashboard-only participant reminders from records already read by the caller.
/// It deliberately mirrors the registration eligibility checks without changing any state.
/// </summary>
public static class ParticipantAttentionBuilder
{
    public static List<ParticipantAttentionItem> Build(
        IEnumerable<EventRegistration> registrations,
        IEnumerable<Assessment> assessments,
        IReadOnlyDictionary<int, bool> eventHasCapacity,
        IReadOnlyDictionary<int, bool> assessmentHasSupportedPrediction,
        DateTime now)
    {
        var registrationRows = registrations.ToList();
        var currentRegistrations = registrationRows
            .GroupBy(registration => registration.EventId)
            .Select(group => group.OrderByDescending(registration => registration.RegisteredAt)
                .ThenByDescending(registration => registration.Id)
                .First())
            .ToList();
        var blockingEventIds = registrationRows
            .Where(registration => RegistrationStatusHelper.ActiveStatuses.Contains(registration.Status)
                || registration.Status == "Alternative Recommended")
            .Select(registration => registration.EventId)
            .ToHashSet();
        var items = new List<ParticipantAttentionItem>();

        foreach (var registration in currentRegistrations.Where(registration =>
                     registration.Status == "Awaiting Payment"
                     && registration.Event is not null
                     && SupportsExistingWorkflow(registration.Event)
                     && (!registration.PaymentDeadline.HasValue || registration.PaymentDeadline >= now)))
        {
            var deadline = registration.PaymentDeadline;
            items.Add(new ParticipantAttentionItem
            {
                Kind = ParticipantAttentionKind.Payment,
                Title = $"Payment needed for {registration.Event!.EventTitle}",
                Detail = deadline.HasValue
                    ? $"Upload your payment receipt by {deadline.Value:MMM dd, h:mm tt}."
                    : "Upload your payment receipt using the instructions in your registration.",
                ActionLabel = "View registration",
                RegistrationId = registration.Id,
                SortDate = deadline ?? registration.RegisteredAt,
                StableId = registration.Id
            });
        }

        var currentAssessments = assessments
            .Where(assessment => assessment.IsActive)
            .GroupBy(assessment => assessment.EventId)
            .Select(group => group.OrderByDescending(assessment => assessment.SubmittedAt)
                .ThenByDescending(assessment => assessment.Id)
                .First());
        foreach (var assessment in currentAssessments.Where(assessment =>
                     assessment.Event is not null
                     && EventJoinabilityHelper.IsJoinable(assessment.Event, now)
                     && RegistrationRulesHelper.RequiresMedicalClearance(assessment)
                     && assessmentHasSupportedPrediction.GetValueOrDefault(assessment.Id)
                     && eventHasCapacity.GetValueOrDefault(assessment.EventId)
                     && !blockingEventIds.Contains(assessment.EventId)))
        {
            items.Add(new ParticipantAttentionItem
            {
                Kind = ParticipantAttentionKind.MedicalClearance,
                Title = $"Medical clearance needed for {assessment.Event!.EventTitle}",
                Detail = "Provide your required medical clearance while completing registration.",
                ActionLabel = "Complete registration",
                EventId = assessment.EventId,
                AssessmentId = assessment.Id,
                SortDate = assessment.SubmittedAt,
                StableId = assessment.Id
            });
        }

        foreach (var registration in currentRegistrations.Where(registration =>
                     registration.Status == "Alternative Recommended"
                     && registration.Event is not null
                     && registration.AlternativeEvent is not null
                     && SupportsExistingWorkflow(registration.Event)
                     && EventJoinabilityHelper.IsJoinable(registration.AlternativeEvent, now)
                     && eventHasCapacity.GetValueOrDefault(registration.AlternativeEventId ?? 0)
                     && !blockingEventIds.Contains(registration.AlternativeEventId ?? 0)))
        {
            items.Add(new ParticipantAttentionItem
            {
                Kind = ParticipantAttentionKind.AlternativeRecommendation,
                Title = $"Alternative recommended: {registration.AlternativeEvent!.EventTitle}",
                Detail = $"Your registration for {registration.Event!.EventTitle} has a suggested alternative.",
                ActionLabel = "View recommendation",
                RegistrationId = registration.Id,
                SortDate = registration.RegisteredAt,
                StableId = registration.Id
            });
        }

        return items
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.SortDate)
            .ThenBy(item => item.StableId)
            .ToList();
    }

    private static bool SupportsExistingWorkflow(Event eventItem) =>
        !RegistrationStatusHelper.IsEventCancelled(eventItem)
        && eventItem.Status != "Completed";
}
