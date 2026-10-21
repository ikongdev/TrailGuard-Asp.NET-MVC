using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Defines the one authoritative rule for transitioning an event to Completed.
/// Calendar comparisons intentionally use Philippine dates, not elapsed time.
/// </summary>
public static class EventCompletionEligibilityPolicy
{
    public const string FutureDateMessage = "Events can only be marked as complete on or after their scheduled date.";
    public const string NonUpcomingStatusMessage = "Only upcoming events can be marked as completed";

    public static bool IsCompletedStatusSubmission(string? status) =>
        string.Equals(status, "Completed", StringComparison.Ordinal);

    public static bool CanComplete(Event eventItem, IPhilippineClock clock)
    {
        ArgumentNullException.ThrowIfNull(eventItem);
        ArgumentNullException.ThrowIfNull(clock);
        return CanComplete(eventItem.Status, eventItem.EventDate, clock.Today);
    }

    public static bool CanComplete(string? status, DateTime eventDate, DateOnly philippineToday) =>
        status == "Upcoming" && DateOnly.FromDateTime(eventDate) <= philippineToday;

    public static string IneligibilityMessage(Event eventItem, IPhilippineClock clock)
    {
        ArgumentNullException.ThrowIfNull(eventItem);
        ArgumentNullException.ThrowIfNull(clock);

        return eventItem.Status != "Upcoming"
            ? NonUpcomingStatusMessage
            : DateOnly.FromDateTime(eventItem.EventDate) > clock.Today
                ? FutureDateMessage
                : string.Empty;
    }
}
