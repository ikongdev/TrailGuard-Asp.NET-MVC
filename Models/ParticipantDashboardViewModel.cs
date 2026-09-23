namespace TrailGuard.Models
{
    public class ParticipantDashboardViewModel
    {

        public int UpcomingEventsCount { get; set; }
        public int CompletedHikes { get; set; }
        public int PendingRegistrations { get; set; }
        public int TotalRegistrations { get; set; }


        public List<Event> UpcomingEvents { get; set; } = new();


        public LatestAssessmentResult? LatestAssessment { get; set; }


        public List<Event> RecommendedEvents { get; set; } = new();


        public string? PersonalBestDifficulty { get; set; }
        public double? PersonalBestDistanceKm { get; set; }
        public int? PersonalBestElevationMeters { get; set; }






        public int TrailPoints { get; set; }
        public int Rank { get; set; }
        public int TotalHikers { get; set; }
        public bool IsRanked { get; set; }
    }

    public class LatestAssessmentResult
    {
        public string Result { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }

        public double CompletionProbability { get; set; }
        public bool HasMlPrediction { get; set; }

        public int AssessmentId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string TrailName { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;
    }
}
