using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class EventJoinabilityHelper
    {


        public static bool IsJoinable(Event eventItem) => IsJoinable(eventItem, DateTime.Today);

        public static bool IsJoinable(Event eventItem, DateTime currentDate) =>
            eventItem.Status == "Upcoming" && eventItem.EventDate.Date >= currentDate.Date;




        public static bool RequiresManualClosure(Event eventItem) =>
            eventItem.Status == "Upcoming" && !IsJoinable(eventItem);
    }
}
