using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class EventJoinabilityHelper
    {
        // "Upcoming" alone isn't enough - an organizer can forget to mark a past-due
        // event Completed, and that shouldn't leave it open for new registrations.
        public static bool IsJoinable(Event eventItem) =>
            eventItem.Status == "Upcoming" && eventItem.EventDate >= DateTime.Today;

        // Past-due events stay Upcoming until the organizer explicitly completes,
        // cancels, or reschedules them. The dashboard uses this same boundary when
        // surfacing manual-closure work, so an event is not flagged on its own date.
        public static bool RequiresManualClosure(Event eventItem) =>
            eventItem.Status == "Upcoming" && !IsJoinable(eventItem);
    }
}
