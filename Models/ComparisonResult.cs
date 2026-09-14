namespace TrailGuard.Models
{
    public class ComparisonResult
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string PreHikeAssessment { get; set; } = string.Empty;
        public string? ParticipantDifficultyExperience { get; set; }
        public string ParticipantCompletion { get; set; } = string.Empty;
        public string? OrganizerDifficultyExperience { get; set; }
        public string OrganizerCompletion { get; set; } = string.Empty;
        public string? ConservativeDifficultyExperience { get; set; }
        public bool? Completed { get; set; }
        public string? NonCompletionReason { get; set; }
        public string ComparisonNotice { get; set; } = "Unavailable pending Stage 5";
    }
}
