using System.Text.Json.Serialization;

namespace TrailGuard.Models
{
    public class SuitabilityPredictionRequest
    {
        [JsonPropertyName("age")]
        public int Age { get; set; }

        [JsonPropertyName("height_cm")]
        public double HeightCm { get; set; }

        [JsonPropertyName("weight_kg")]
        public double WeightKg { get; set; }

        [JsonPropertyName("bmi")]
        public double Bmi { get; set; }

        [JsonPropertyName("has_asthma")]
        public int HasAsthma { get; set; }

        [JsonPropertyName("has_hypertension_heart_condition")]
        public int HasHypertensionHeartCondition { get; set; }

        [JsonPropertyName("has_joint_knee_injury")]
        public int HasJointKneeInjury { get; set; }

        [JsonPropertyName("has_vertigo")]
        public int HasVertigo { get; set; }

        [JsonPropertyName("exercise_frequency_score")]
        public int ExerciseFrequencyScore { get; set; }

        [JsonPropertyName("exercise_type_category")]
        public int ExerciseTypeCategory { get; set; }

        [JsonPropertyName("continuous_cardio_duration_score")]
        public int ContinuousCardioDurationScore { get; set; }

        [JsonPropertyName("hiking_experience_score")]
        public int HikingExperienceScore { get; set; }

        [JsonPropertyName("last_hike_recency_score")]
        public int LastHikeRecencyScore { get; set; }

        [JsonPropertyName("hardest_trail_completed_score")]
        public int HardestTrailCompletedScore { get; set; }

        [JsonPropertyName("gear_water")]
        public int GearWater { get; set; }

        [JsonPropertyName("gear_trail_food")]
        public int GearTrailFood { get; set; }

        [JsonPropertyName("gear_first_aid_medicine")]
        public int GearFirstAidMedicine { get; set; }

        [JsonPropertyName("gear_flashlight_headlamp")]
        public int GearFlashlightHeadlamp { get; set; }

        [JsonPropertyName("gear_whistle")]
        public int GearWhistle { get; set; }

        [JsonPropertyName("gear_raincoat_poncho")]
        public int GearRaincoatPoncho { get; set; }

        [JsonPropertyName("gear_navigation")]
        public int GearNavigation { get; set; }

        [JsonPropertyName("gear_proper_shoes")]
        public int GearProperShoes { get; set; }

        [JsonPropertyName("gear_score")]
        public int GearScore { get; set; }

        [JsonPropertyName("trail_distance_km")]
        public double TrailDistanceKm { get; set; }

        [JsonPropertyName("trail_elevation_gain_m")]
        public double TrailElevationGainM { get; set; }

        [JsonPropertyName("trail_terrain_type")]
        public int TrailTerrainType { get; set; }

        [JsonPropertyName("trail_estimated_duration_hr")]
        public double TrailEstimatedDurationHr { get; set; }
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

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonPropertyName("model_version")]
        public string ModelVersion { get; set; } = string.Empty;

        [JsonPropertyName("shap_breakdown")]
        public List<ShapFeatureImpactDto> ShapBreakdown { get; set; } = new();
    }
}