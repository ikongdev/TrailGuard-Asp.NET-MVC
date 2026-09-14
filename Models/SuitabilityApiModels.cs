using System.Text.Json.Serialization;

namespace TrailGuard.Models;

public class SuitabilityPredictionRequest
{
    [JsonPropertyName("bmi")] public double Bmi { get; set; }
    [JsonPropertyName("exercise_frequency")] public string ExerciseFrequency { get; set; } = string.Empty;
    [JsonPropertyName("cardio_duration")] public string CardioDuration { get; set; } = string.Empty;
    [JsonPropertyName("exercise_consistency")] public string ExerciseConsistency { get; set; } = string.Empty;
    [JsonPropertyName("hiking_experience")] public string HikingExperience { get; set; } = string.Empty;
    [JsonPropertyName("last_hike_recency")] public string LastHikeRecency { get; set; } = string.Empty;
    [JsonPropertyName("hardest_trail_completed")] public string HardestTrailCompleted { get; set; } = string.Empty;
    [JsonPropertyName("gear_score")] public int GearScore { get; set; }
    [JsonPropertyName("has_asthma")] public int HasAsthma { get; set; }
    [JsonPropertyName("has_cvd")] public int HasCvd { get; set; }
    [JsonPropertyName("has_joint_knee_injury")] public int HasJointKneeInjury { get; set; }
    [JsonPropertyName("has_signs_symptoms")] public int HasSignsSymptoms { get; set; }
    [JsonPropertyName("distance_km")] public double DistanceKm { get; set; }
    [JsonPropertyName("elevation_gain_m")] public double ElevationGainM { get; set; }
    [JsonPropertyName("trail_class")] public int TrailClass { get; set; }
    [JsonPropertyName("typical_duration_hours")] public double TypicalDurationHours { get; set; }
}

public class ShapFeatureImpactDto
{
    [JsonPropertyName("feature")] public string Feature { get; set; } = string.Empty;
    [JsonPropertyName("friendly_name")] public string FriendlyName { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("raw_value")] public double RawValue { get; set; }
    [JsonPropertyName("shap_value")] public double ShapValue { get; set; }
    [JsonPropertyName("share_pct")] public double SharePct { get; set; }
    [JsonPropertyName("direction")] public string Direction { get; set; } = string.Empty;
}

public class ShapAllFeatureImpactDto
{
    [JsonPropertyName("feature")] public string Feature { get; set; } = string.Empty;
    [JsonPropertyName("friendly_name")] public string FriendlyName { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("raw_value")] public double RawValue { get; set; }
    [JsonPropertyName("shap_value")] public double ShapValue { get; set; }
}

public class SuitabilityPredictionResponse
{
    [JsonPropertyName("suitability_label")] public string SuitabilityLabel { get; set; } = string.Empty;
    [JsonPropertyName("completion_probability")] public double CompletionProbability { get; set; }
    [JsonPropertyName("model_version")] public string ModelVersion { get; set; } = string.Empty;
    [JsonPropertyName("shap_breakdown")] public List<ShapFeatureImpactDto> ShapBreakdown { get; set; } = new();
    [JsonPropertyName("shap_all")] public List<ShapAllFeatureImpactDto> ShapAll { get; set; } = new();
}

public sealed class SuitabilityPredictionCallResult
{
    public SuitabilityPredictionResponse? Prediction { get; init; }
    public bool IsValidationFailure { get; init; }
}
