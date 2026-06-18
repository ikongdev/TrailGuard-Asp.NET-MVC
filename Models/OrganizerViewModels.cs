namespace TrailGuard.Models
{
    public class OrganizerDashboardViewModel
    {
        // Summary Cards
        public int TotalEvents { get; set; }
        public int PendingRegistrations { get; set; }
        public int EventAlerts { get; set; }
        public int TotalParticipants { get; set; }

        // Charts
        public List<MonthlyTrendData> TrendData { get; set; } = new();
        public List<TopTrailData> TopTrails { get; set; } = new();
        public List<SuitabilityData> SuitabilityBreakdown { get; set; } = new();
        public int TotalAssessments { get; set; }

        // Top Hikers
        public List<TopHikerData> TopHikers { get; set; } = new();
    }

    public class MonthlyTrendData
    {
        public string Month { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class TopTrailData
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public int EventCount { get; set; }
    }

    public class SuitabilityData
    {
        public string Result { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Percentage { get; set; }
    }

    public class TopHikerData
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int CompletedEvents { get; set; }
    }
}