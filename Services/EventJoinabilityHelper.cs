using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class EventJoinabilityHelper
    {
        // "Upcoming" alone isn't enough - an organizer can forget to mark a past-due
        // event Completed, and that shouldn't leave it open for new registrations.
        public static bool IsJoinable(Event eventItem) =>
            eventItem.Status == "Upcoming" && eventItem.EventDate >= DateTime.Today;
    }
}
