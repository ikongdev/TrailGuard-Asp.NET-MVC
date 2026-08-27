namespace TrailGuard.Models
{
    // Replaces the old per-Trail grouping (EventGroupViewModel) - the listing page
    // now groups by Event.Status instead. See EventController.Index for the fixed
    // Upcoming/Completed-first, then-actual-remaining-statuses ordering.
    public class EventStatusGroupViewModel
    {
        public string Status { get; set; } = string.Empty;
        public List<Event> Events { get; set; } = new List<Event>();
    }
}
