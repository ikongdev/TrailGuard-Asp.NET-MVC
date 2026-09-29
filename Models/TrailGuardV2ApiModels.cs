using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrailGuard.Models;

/// <summary>Exact raw request contract for the isolated frozen TrailGuard v2 adapter.</summary>
public sealed class TrailGuardV2PredictionRequest
{
    [JsonPropertyName("exercise_frequency")] public string? ExerciseFrequency { get; init; }
    [JsonPropertyName("cardio_duration")] public string? CardioDuration { get; init; }
    [JsonPropertyName("exercise_consistency")] public string? ExerciseConsistency { get; init; }
    [JsonPropertyName("hiking_experience")] public string? HikingExperience { get; init; }
    [JsonPropertyName("hiking_recency")] public string? HikingRecency { get; init; }
    [JsonPropertyName("hardest_trail_completed")] public string? HardestTrailCompleted { get; init; }
    [JsonPropertyName("gear_score")] public int GearScore { get; init; }
    [JsonPropertyName("distance_km")] public double DistanceKm { get; init; }
    [JsonPropertyName("elevation_gain_m")] public int ElevationGainM { get; init; }
    [JsonPropertyName("trail_class")] public int TrailClass { get; init; }
    [JsonPropertyName("typical_duration_hours")] public double TypicalDurationHours { get; init; }
}

public sealed class TrailGuardV2PredictionResponse
{
    [JsonRequired, JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
    [JsonRequired, JsonPropertyName("frozen_model_sha256")] public string? FrozenModelSha256 { get; init; }
    [JsonRequired, JsonPropertyName("selected_tree_count")] public int? SelectedTreeCount { get; init; }
    [JsonRequired, JsonPropertyName("model_score")] public double? ModelScore { get; init; }
    [JsonRequired, JsonPropertyName("score_name")] public string? ScoreName { get; init; }
    [JsonRequired, JsonPropertyName("binary_prediction")] public string? BinaryPrediction { get; init; }
    [JsonRequired, JsonPropertyName("binary_threshold")] public double? BinaryThreshold { get; init; }
    [JsonRequired, JsonPropertyName("ui_label")] public string? UiLabel { get; init; }
    [JsonRequired, JsonPropertyName("ui_label_policy_version")] public string? UiLabelPolicyVersion { get; init; }
    [JsonRequired, JsonPropertyName("ui_label_policy")] public TrailGuardV2UiLabelPolicy? UiLabelPolicy { get; init; }
    [JsonRequired, JsonPropertyName("shap")] public TrailGuardV2ShapResponse? Shap { get; init; }
}

public sealed class TrailGuardV2UiLabelPolicy
{
    [JsonRequired, JsonPropertyName("good_match")] public TrailGuardV2Threshold? GoodMatch { get; init; }
    [JsonRequired, JsonPropertyName("borderline")] public TrailGuardV2RangeThreshold? Borderline { get; init; }
    [JsonRequired, JsonPropertyName("not_recommended")] public TrailGuardV2Threshold? NotRecommended { get; init; }
}

public sealed class TrailGuardV2Threshold
{
    [JsonRequired, JsonPropertyName("operator")] public string? Operator { get; init; }
    [JsonRequired, JsonPropertyName("threshold")] public double? Threshold { get; init; }
}

public sealed class TrailGuardV2RangeThreshold
{
    [JsonRequired, JsonPropertyName("minimum_operator")] public string? MinimumOperator { get; init; }
    [JsonRequired, JsonPropertyName("minimum")] public double? Minimum { get; init; }
    [JsonRequired, JsonPropertyName("maximum_operator")] public string? MaximumOperator { get; init; }
    [JsonRequired, JsonPropertyName("maximum")] public double? Maximum { get; init; }
}

public sealed class TrailGuardV2ShapResponse
{
    [JsonRequired, JsonPropertyName("base_value")] public double? BaseValue { get; init; }
    [JsonRequired, JsonPropertyName("raw_margin")] public double? RawMargin { get; init; }
    [JsonRequired, JsonPropertyName("scale")] public string? Scale { get; init; }
    [JsonRequired, JsonPropertyName("contribution_interpretation")] public string? ContributionInterpretation { get; init; }
    [JsonRequired, JsonPropertyName("contributions")] public List<TrailGuardV2ShapContribution>? Contributions { get; init; }
    [JsonRequired, JsonPropertyName("verification")] public TrailGuardV2ShapVerification? Verification { get; init; }
}

public sealed class TrailGuardV2ShapContribution
{
    [JsonRequired, JsonPropertyName("feature")] public string? Feature { get; init; }
    [JsonRequired, JsonPropertyName("original_input_value")] public JsonElement? OriginalInputValue { get; init; }
    [JsonRequired, JsonPropertyName("shap_contribution")] public double? ShapContribution { get; init; }
}

public sealed class TrailGuardV2ShapVerification
{
    [JsonRequired, JsonPropertyName("additivity_error")] public double? AdditivityError { get; init; }
    [JsonRequired, JsonPropertyName("probability_reconstruction_error")] public double? ProbabilityReconstructionError { get; init; }
    [JsonRequired, JsonPropertyName("prediction_change_after_explanation")] public double? PredictionChangeAfterExplanation { get; init; }
    [JsonRequired, JsonPropertyName("tolerance_absolute")] public double? ToleranceAbsolute { get; init; }
    [JsonRequired, JsonPropertyName("predictions_unchanged")] public bool? PredictionsUnchanged { get; init; }
    [JsonRequired, JsonPropertyName("within_tolerance")] public bool? WithinTolerance { get; init; }
    [JsonRequired, JsonPropertyName("feature_order_matches_frozen_schema")] public bool? FeatureOrderMatchesFrozenSchema { get; init; }
}

public enum TrailGuardV2PredictionFailure
{
    None,
    InvalidInput,
    ServiceUnavailable,
    InvalidServiceResponse
}

public sealed class TrailGuardV2PredictionCallResult
{
    public TrailGuardV2PredictionResponse? Prediction { get; init; }
    public TrailGuardV2PredictionFailure Failure { get; init; }
    public bool IsSuccess => Prediction is not null && Failure == TrailGuardV2PredictionFailure.None;
}
