namespace TrailGuard.Models
{
    public class AssessmentReportViewModel
    {
        public int AssessmentId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;

        public string Result { get; set; } = string.Empty;

        public List<string> Recommendations { get; set; } = new List<string>();
        public List<Event> AlternativeEvents { get; set; } = new List<Event>();
        public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();

        public bool HasMlPrediction { get; set; }
        public double ConfidenceScore { get; set; }
        public string ModelVersion { get; set; } = string.Empty;
        public List<ShapDisplayItem> ShapFactors { get; set; } = new List<ShapDisplayItem>();

        public double NpsScore { get; set; }
        public string NpsBand { get; set; } = string.Empty;
        public bool GateApplied { get; set; }
        public string GateReason { get; set; } = string.Empty;
        public bool MedicalClearanceRequired { get; set; }
    }
}
