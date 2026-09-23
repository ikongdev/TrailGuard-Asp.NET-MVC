namespace TrailGuard.Models
{



    public class EventManagementViewModel
    {
        public List<EventStatusGroupViewModel> StatusGroups { get; set; } = new List<EventStatusGroupViewModel>();





        public List<string> AvailableStatuses { get; set; } = new List<string>();

        public int UpcomingEventsCount { get; set; }




        public bool HasAnyResults { get; set; }
    }
}
