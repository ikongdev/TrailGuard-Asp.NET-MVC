using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class EventJoinabilityHelper
    {


        public static bool IsJoinable(Event eventItem) =>
            eventItem.Status == "Upcoming" && eventItem.EventDate >= DateTime.Today;




        public static bool RequiresManualClosure(Event eventItem) =>
            eventItem.Status == "Upcoming" && !IsJoinable(eventItem);
    }
}
