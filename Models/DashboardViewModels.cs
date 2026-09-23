namespace TrailGuard.Models
{




    public class AdminDashboardViewModel
    {
        public int ActiveAccountsCount { get; set; }
        public int TotalTrails { get; set; }




        public int UpcomingEventsCount { get; set; }
        public List<AdminUpcomingEventData> UpcomingEvents { get; set; } = new();





        public int RegistrationsThisMonthCount { get; set; }
        public List<MonthlyTrendData> MonthlyRegistrations { get; set; } = new();






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
