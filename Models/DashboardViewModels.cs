namespace TrailGuard.Models
{
    // System-wide, operations-first Admin Dashboard - see AdminController.Index.
    // No financial data: the Total Revenue card and its supporting queries were
    // removed outright (see CLAUDE.md/PLAN notes on the Admin Dashboard redesign),
    // not just hidden from this view.
    public class AdminDashboardViewModel
    {
        public int ActiveAccountsCount { get; set; }
        public int TotalTrails { get; set; }

        // Both derived from the exact same EventJoinabilityHelper.IsJoinable
        // predicate over the exact same query result, so the summary count and
        // the rendered list can never drift apart - see AdminController.Index.
        public int UpcomingEventsCount { get; set; }
        public List<AdminUpcomingEventData> UpcomingEvents { get; set; } = new();

        // Both derived from the same year-safe [monthStart, nextMonthStart)
        // registration query - MonthlyRegistrations is 6 months (oldest to
        // newest, zero-filled), and RegistrationsThisMonthCount is just that
        // series' last element, not a separate query.
        public int RegistrationsThisMonthCount { get; set; }
        public List<MonthlyTrendData> MonthlyRegistrations { get; set; } = new();

        // Reuses OrganizerAttentionItem (Models/OrganizerViewModels.cs) rather
        // than defining a near-identical Admin-only shape - the same
        // Title/Detail/ActionLabel/Controller/Action/Id fields already cover
        // every category here (see AdminController.Index for how each
        // category is built and concatenated in severity order).
        public List<OrganizerAttentionItem> AttentionItems { get; set; } = new();

        public List<AdminRecentRegistrationData> RecentRegistrations { get; set; } = new();
    }

    public class AdminUpcomingEventData
    {
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string TrailName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan EventTime { get; set; }
        public string Difficulty { get; set; } = string.Empty;

        // OrganizerName is null only when OrganizerId itself is null
        // (Unassigned); it's "Organizer unavailable" (set by the controller,
        // not here) when OrganizerId is set but couldn't be resolved to an
        // active account row - the view never has to re-derive that
        // distinction from a raw id or email.
        public string? OrganizerName { get; set; }
        public string? OrganizerProfilePictureUrl { get; set; }
        public string OrganizerInitials { get; set; } = string.Empty;

        public string FormattedEventTime => DateTime.Today.Add(EventTime).ToString("h:mm tt");
    }

    public class AdminRecentRegistrationData
    {
        public int RegistrationId { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string EventTitle { get; set; } = string.Empty;
        public string OrganizerName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
    }
}
