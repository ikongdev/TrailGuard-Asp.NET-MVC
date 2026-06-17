using TrailGuard.Models;

namespace TrailGuard.Models
{
    public class OrganizerDashboardViewModel
    {
        // Summary Cards
        public int TotalEvents { get; set; }
        public int PendingRegistrations { get; set; }
        public int EventAlerts { get; set; }
        public int TotalParticipants { get; set; }
        public int ParticipantsLast30Days { get; set; }

        // Upcoming Events
        public List<Event> UpcomingEvents { get; set; } = new List<Event>();

        // Top Trails
        public List<TopTrailData> TopTrails { get; set; } = new List<TopTrailData>();

        // Suitability Breakdown
        public List<SuitabilityData> SuitabilityBreakdown { get; set; } = new List<SuitabilityData>();

        // Top Hikers
        public List<TopHikerData> TopHikers { get; set; } = new List<TopHikerData>();
    }

    public class TopTrailData
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public int ParticipantCount { get; set; }
    }

    public class SuitabilityData
    {
        public string Result { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class TopHikerData
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public double AverageScore { get; set; }
        public int TotalAssessments { get; set; }
    }

    public class RegistrationWithAssessmentViewModel
    {
        public EventRegistration Registration { get; set; } = new EventRegistration();
        public string AssessmentResult { get; set; } = string.Empty;
        public int AssessmentScore { get; set; }
        public int AssessmentId { get; set; }
    }
}