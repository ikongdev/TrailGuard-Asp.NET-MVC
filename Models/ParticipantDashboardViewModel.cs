namespace TrailGuard.Models
{
    public class ParticipantDashboardViewModel
    {

        public int UpcomingEventsCount { get; set; }
        public int CompletedHikes { get; set; }
        public int PendingRegistrations { get; set; }
        public int TotalRegistrations { get; set; }

        public List<ParticipantAttentionItem> AttentionItems { get; set; } = new();


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

    public enum ParticipantAttentionKind
    {
        Payment,
        MedicalClearance,
        AlternativeRecommendation
    }

    public sealed class ParticipantAttentionItem
    {
        public ParticipantAttentionKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string ActionLabel { get; init; } = string.Empty;
        public int? RegistrationId { get; init; }
        public int? EventId { get; init; }
        public int? AssessmentId { get; init; }
        internal DateTime SortDate { get; init; }
        internal int StableId { get; init; }
    }

    public class LatestAssessmentResult
    {
        public string Result { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }

        public double ModelScore { get; set; }
        public bool HasMlPrediction { get; set; }

        public int AssessmentId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string TrailName { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;
    }
}
