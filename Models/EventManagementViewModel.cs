namespace TrailGuard.Models
{
    // Page-level shape for Event/Index - one status-grouped, search/trail/difficulty/
    // sort-filtered view of the organizer's event catalog, plus the filter-independent
    // Upcoming count shown in the header summary.
    public class EventManagementViewModel
    {
        public List<EventStatusGroupViewModel> StatusGroups { get; set; } = new List<EventStatusGroupViewModel>();

        // Every status actually present in the catalog (Upcoming/Completed always
        // included even when currently empty; anything else only if real) - drives
        // the status filter's own <option> list, in the same fixed-then-actual order
        // used for StatusGroups.
        public List<string> AvailableStatuses { get; set; } = new List<string>();

        public int UpcomingEventsCount { get; set; }

        // True if any event survived search/trail/difficulty/status filtering - false
        // distinguishes "no results for these filters" from "no status has anything
        // to show", which the view renders as different empty states.
        public bool HasAnyResults { get; set; }
    }
}
