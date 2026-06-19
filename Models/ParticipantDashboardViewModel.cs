namespace TrailGuard.Models
{
    public class ParticipantDashboardViewModel
    {
        // Summary Cards
        public int UpcomingEventsCount { get; set; }
        public int CompletedHikes { get; set; }
        public int PendingRegistrations { get; set; }
        public int TotalRegistrations { get; set; }

        // Upcoming Events
        public List<Event> UpcomingEvents { get; set; } = new();

        // Latest Assessment
        public LatestAssessmentResult? LatestAssessment { get; set; }

        // Recommended Events
        public List<Event> RecommendedEvents { get; set; } = new();

        // Weather Forecast
        public WeatherForecast? WeatherForecast { get; set; }

        // Progress & Achievements
        public string HikerRank { get; set; } = "Not Ranked";
        public int BadgesEarned { get; set; }
        public string NextMilestone { get; set; } = "Keep hiking to climb higher!";
    }

    public class LatestAssessmentResult
    {
        public string Result { get; set; } = string.Empty; // Good-Match, Borderline, Not Recommended
        public int TotalScore { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
    }

    public class WeatherForecast
    {
        public DateTime Date { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Temperature { get; set; } = string.Empty;
        public int RainChance { get; set; }
        public string WindSpeed { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
    }
}