namespace TrailGuard.Models
{
    public class AssessmentResultViewModel
    {
        public int AssessmentId { get; set; }
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string EventDifficulty { get; set; } = string.Empty;
        
        public string Result { get; set; } = string.Empty;
        public int TotalScore { get; set; }
        public int MaxScore { get; set; } = 44;
        public int Threshold { get; set; } = 32; // ✅ BAGONG PROPERTY
        
        // Category Scores
        public int FitnessScore { get; set; }
        public int FitnessMax { get; set; } = 12;
        public int ExperienceScore { get; set; }
        public int ExperienceMax { get; set; } = 12;
        public int HealthScore { get; set; }
        public int HealthMax { get; set; } = 12;
        public int GearScore { get; set; }
        public int GearMax { get; set; } = 8;
        
        public List<string> RiskFlags { get; set; } = new List<string>();
        public List<string> Recommendations { get; set; } = new List<string>();
        public List<Event> AlternativeEvents { get; set; } = new List<Event>();
        public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();

        public bool HasMlPrediction { get; set; }
        public double ConfidenceScore { get; set; }
        public string ModelVersion { get; set; } = string.Empty;
        public List<ShapDisplayItem> ShapFactors { get; set; } = new List<ShapDisplayItem>();
    }

    public class ShapDisplayItem
    {
        public string FeatureName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string RawValue { get; set; } = string.Empty;
        public double Impact { get; set; }
        public bool IsPositive => Impact > 0;
        public double BarWidth { get; set; }
    }

}