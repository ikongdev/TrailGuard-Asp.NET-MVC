using System.Text.Json.Serialization;

namespace TrailGuard.Models
{
    public class SuitabilityPredictionRequest
    {
        [JsonPropertyName("bmi")]
        public double Bmi { get; set; }

        [JsonPropertyName("exercise_frequency_score")]
        public int ExerciseFrequencyScore { get; set; }

        [JsonPropertyName("continuous_cardio_duration_score")]
        public int ContinuousCardioDurationScore { get; set; }

        [JsonPropertyName("exercise_consistency_score")]
        public int ExerciseConsistencyScore { get; set; }

        [JsonPropertyName("hiking_experience_score")]
        public int HikingExperienceScore { get; set; }

        [JsonPropertyName("last_hike_recency_score")]
        public int LastHikeRecencyScore { get; set; }

        [JsonPropertyName("hardest_trail_completed_score")]
        public int HardestTrailCompletedScore { get; set; }

        [JsonPropertyName("gear_score")]
        public int GearScore { get; set; }

        [JsonPropertyName("has_asthma")]
        public int HasAsthma { get; set; }

        [JsonPropertyName("has_cvd")]
        public int HasCvd { get; set; }

        [JsonPropertyName("has_joint_knee_injury")]
        public int HasJointKneeInjury { get; set; }

        [JsonPropertyName("has_cvd_symptoms")]
        public int HasCvdSymptoms { get; set; }

        [JsonPropertyName("trail_distance_km")]
        public double TrailDistanceKm { get; set; }

        [JsonPropertyName("trail_elevation_gain_m")]
        public double TrailElevationGainM { get; set; }

        [JsonPropertyName("trail_terrain_type")]
        public int TrailTerrainType { get; set; }
    }

    public class ShapFeatureImpactDto
    {
        [JsonPropertyName("feature")]
        public string Feature { get; set; } = string.Empty;

        [JsonPropertyName("raw_value")]
        public double RawValue { get; set; }

        [JsonPropertyName("impact")]
        public double Impact { get; set; }
    }

    public class SuitabilityPredictionResponse
    {
        [JsonPropertyName("suitability_label")]
        public string SuitabilityLabel { get; set; } = string.Empty;

        [JsonPropertyName("model_label")]
        public string ModelLabel { get; set; } = string.Empty;

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonPropertyName("medical_clearance_required")]
        public bool MedicalClearanceRequired { get; set; }

        [JsonPropertyName("gate_applied")]
        public bool GateApplied { get; set; }

        [JsonPropertyName("gate_reason")]
        public string GateReason { get; set; } = string.Empty;

        [JsonPropertyName("nps_score")]
        public double NpsScore { get; set; }

        [JsonPropertyName("nps_band")]
        public string NpsBand { get; set; } = string.Empty;

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; set; } = string.Empty;

        [JsonPropertyName("shap_breakdown")]
        public List<ShapFeatureImpactDto> ShapBreakdown { get; set; } = new();
    }
}
