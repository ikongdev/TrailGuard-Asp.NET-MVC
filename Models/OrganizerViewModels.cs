namespace TrailGuard.Models
{
    public class OrganizerDashboardViewModel
    {
        public int UpcomingEventsCount { get; set; }
        public int PendingReviewCount { get; set; }
        public int PaymentsToVerifyCount { get; set; }
        public int AcceptedRegistrationsCount { get; set; }
        public List<OrganizerAttentionItem> AttentionItems { get; set; } = new();
        public List<OrganizerUpcomingEventData> UpcomingEvents { get; set; } = new();
        public List<MonthlyTrendData> TrendData { get; set; } = new();
        public List<SuitabilityData> SuitabilityBreakdown { get; set; } = new();
        public int TotalAssessments { get; set; }
    }

    public class OrganizerAttentionItem
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string Controller { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public int? Id { get; set; }
    }

    public class OrganizerUpcomingEventData
    {
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan EventTime { get; set; }
        public string? WeatherRiskLevel { get; set; }
        public string FormattedEventTime => DateTime.Today.Add(EventTime).ToString("h:mm tt");
    }

    public class MonthlyTrendData
    {
        public string Month { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class SuitabilityData
    {
        public string Result { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Percentage { get; set; }
    }

}
