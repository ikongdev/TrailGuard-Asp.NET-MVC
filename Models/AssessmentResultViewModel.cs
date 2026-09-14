namespace TrailGuard.Models
{
    public class AssessmentResultViewModel
    {
        public int AssessmentId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;
        
        public string Result { get; set; } = string.Empty;
        public bool HasMlPrediction { get; set; }
        public double CompletionProbability { get; set; }
        public string ModelVersion { get; set; } = string.Empty;
        public List<ShapDisplayItem> ShapFactors { get; set; } = new List<ShapDisplayItem>();
    }

    public class ShapDisplayItem
    {
        public string FeatureName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RawValue { get; set; } = string.Empty;
        public double Impact { get; set; }
        public bool IsPositive => Impact > 0;
        public double BarWidth { get; set; }
    }

}
