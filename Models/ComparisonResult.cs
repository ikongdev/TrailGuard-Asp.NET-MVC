namespace TrailGuard.Models
{
    public class ComparisonResult
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string PreHikeAssessment { get; set; } = string.Empty;
        public string ParticipantFeedback { get; set; } = string.Empty;
        public string OrganizerAssessment { get; set; } = string.Empty;
        public string FinalResult { get; set; } = string.Empty;
        public string? FinalLabel { get; set; }
        public string Comparison { get; set; } = string.Empty;
        public string ComparisonTextClass { get; set; } = string.Empty;

        // The failure mode the system exists to prevent — a participant was told they
        // were ready and were not. Views highlight this distinctly, not as one of three
        // equally-weighted outcomes.
        public bool IsMissedRisk { get; set; }
    }
}