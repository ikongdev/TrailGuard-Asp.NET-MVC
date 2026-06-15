using TrailGuard.Models;

namespace TrailGuard.Models
{
    public class AdminDashboardViewModel
    {
        public int TotalTrails { get; set; }
        public int TotalEvents { get; set; }
        public int TotalParticipants { get; set; }
        public decimal TotalRevenue { get; set; }
        public List<MonthlyData> EventsPerMonth { get; set; } = new List<MonthlyData>();
        public List<PopularTrailData> PopularTrails { get; set; } = new List<PopularTrailData>();
        public List<StatusData> EventStatusDistribution { get; set; } = new List<StatusData>();
        public List<Event> UpcomingEvents { get; set; } = new List<Event>();
        public List<EventRegistration> RecentRegistrations { get; set; } = new List<EventRegistration>();
    }

    public class MonthlyData
    {
        public string Month { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PopularTrailData
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public int EventCount { get; set; }
    }

    public class StatusData
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}