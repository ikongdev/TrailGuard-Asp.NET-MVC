using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class SuitabilityResult
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssessmentId { get; set; }

        [ForeignKey("AssessmentId")]
        public virtual Assessment? Assessment { get; set; }

        [Required]
        public string UiLabel { get; set; } = string.Empty;



        public double ModelScore { get; set; }

        public string ModelVersion { get; set; } = "v3-real-outcomes";

        public DateTime PredictedAt { get; set; } = DateTime.Now;

        // Nullable provenance and exact submitted model-input snapshot. The model version and
        // frozen model hash identify the contract; nulls keep historical records distinguishable.
        public string? FrozenModelSha256 { get; set; }
        public int? SelectedTreeCount { get; set; }
        public string? ScoreName { get; set; }
        public string? BinaryPrediction { get; set; }
        public double? BinaryThreshold { get; set; }
        public string? UiLabelPolicyVersion { get; set; }
        public string? GoodMatchOperator { get; set; }
        public double? GoodMatchThreshold { get; set; }
        public string? BorderlineMinimumOperator { get; set; }
        public double? BorderlineMinimum { get; set; }
        public string? BorderlineMaximumOperator { get; set; }
        public double? BorderlineMaximum { get; set; }
        public string? NotRecommendedOperator { get; set; }
        public double? NotRecommendedThreshold { get; set; }

        public string? ExerciseFrequency { get; set; }
        public string? CardioDuration { get; set; }
        public string? ExerciseConsistency { get; set; }
        public string? HikingExperience { get; set; }
        public string? HikingRecency { get; set; }
        public string? HardestTrailCompleted { get; set; }
        public int? GearScore { get; set; }
        public double? DistanceKm { get; set; }
        public int? ElevationGainM { get; set; }
        public int? TrailClass { get; set; }
        public double? TypicalDurationHours { get; set; }

        public double? ShapBaseValue { get; set; }
        public double? ShapRawMargin { get; set; }
        public string? ShapScale { get; set; }
        public string? ShapContributionInterpretation { get; set; }
        public double? ShapVerificationAdditivityError { get; set; }
        public double? ShapVerificationProbabilityReconstructionError { get; set; }
        public double? ShapVerificationPredictionChangeAfterExplanation { get; set; }
        public double? ShapVerificationToleranceAbsolute { get; set; }
        public bool? ShapVerificationPredictionsUnchanged { get; set; }
        public bool? ShapVerificationWithinTolerance { get; set; }
        public bool? ShapVerificationFeatureOrderMatchesFrozenSchema { get; set; }

        public virtual ICollection<ShapValue> ShapValues { get; set; } = new List<ShapValue>();
    }
}
