using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Selects one current Event Details participant row per participant without changing
/// capacity or registration workflow semantics.
/// </summary>
public static class EventParticipantDisplaySelector
{
    private static readonly HashSet<string> ExcludedStatuses = new(StringComparer.Ordinal)
    {
        "Rejected",
        "Alternative Recommended",
        "Cancelled",
        "Voided"
    };

    public static List<EventRegistration> SelectCurrentRows(IEnumerable<EventRegistration> registrations) =>
        registrations
            .GroupBy(registration => registration.UserId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(registration => registration.RegisteredAt)
                .ThenByDescending(registration => registration.Id)
                .First())
            .Where(registration => !ExcludedStatuses.Contains(registration.Status))
            .OrderBy(registration => registration.RegisteredAt)
            .ThenBy(registration => registration.Id)
            .ToList();
}
