namespace TrailGuard.Models
{



    public class EventManagementViewModel
    {
        public bool IsAdmin { get; set; }
        public string CurrentUserId { get; set; } = string.Empty;
        public string Visibility { get; set; } = "my";

        public bool CanManageEvent(Event eventItem) => IsAdmin ||
            (!string.IsNullOrEmpty(CurrentUserId) && eventItem.OrganizerId == CurrentUserId);

        public List<EventStatusGroupViewModel> StatusGroups { get; set; } = new List<EventStatusGroupViewModel>();





        public List<string> AvailableStatuses { get; set; } = new List<string>();

        public int UpcomingEventsCount { get; set; }




        public bool HasAnyResults { get; set; }
    }
}
