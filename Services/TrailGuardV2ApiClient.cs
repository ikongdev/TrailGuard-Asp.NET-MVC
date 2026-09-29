using System.Net;
using System.Net.Http.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Isolated client for the frozen TrailGuard v2 adapter. It is intentionally not
/// registered with the running MVC application until a later integration stage.
/// </summary>
public sealed class TrailGuardV2ApiClient
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TrailGuardV2ApiClient> _logger;

    public TrailGuardV2ApiClient(HttpClient httpClient, ILogger<TrailGuardV2ApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TrailGuardV2HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.UnexpectedResponse);
            }

            TrailGuardV2HealthResponse? health;
            try
            {
                health = await response.Content.ReadFromJsonAsync<TrailGuardV2HealthResponse>(
                    ResponseJsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.UnexpectedResponse);
            }

            return health?.Status == "ok"
                && health.Service == "TrailGuard ML v2 adapter"
                && health.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion
                && health.SelectedTreeCount == TrailGuardV2ResponseValidator.ExpectedSelectedTreeCount
                ? new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.Healthy)
                : new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.UnexpectedResponse);
        }
        catch (TaskCanceledException)
        {
            return new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.Unreachable);
        }
        catch (HttpRequestException)
        {
            return new TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus.Unreachable);
        }
    }

    public async Task<TrailGuardV2PredictionCallResult> PredictAsync(
        TrailGuardV2PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/predict", request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogWarning("TrailGuard v2 adapter rejected a prediction request with HTTP 422.");
                return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.InvalidInput };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TrailGuard v2 adapter returned HTTP {StatusCode}.", (int)response.StatusCode);
                return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.ServiceUnavailable };
            }

            TrailGuardV2PredictionResponse? prediction;
            try
            {
                prediction = await response.Content.ReadFromJsonAsync<TrailGuardV2PredictionResponse>(
                    ResponseJsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                _logger.LogError("TrailGuard v2 adapter returned a response that could not be deserialized.");
                return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.InvalidServiceResponse };
            }

            if (prediction is null)
            {
                _logger.LogError("TrailGuard v2 adapter returned an empty response.");
                return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.InvalidServiceResponse };
            }

            try
            {
                TrailGuardV2ResponseValidator.Validate(prediction, request);
            }
            catch (TrailGuardV2ResponseValidationException)
            {
                _logger.LogError("TrailGuard v2 adapter response did not satisfy the frozen response contract.");
                return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.InvalidServiceResponse };
            }

            return new TrailGuardV2PredictionCallResult { Prediction = prediction };
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("TrailGuard v2 adapter request timed out or was cancelled.");
            return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.ServiceUnavailable };
        }
        catch (HttpRequestException)
        {
            _logger.LogError("TrailGuard v2 adapter could not be reached.");
            return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.ServiceUnavailable };
        }
        catch (Exception)
        {
            _logger.LogError("Unexpected failure while calling the TrailGuard v2 adapter.");
            return new TrailGuardV2PredictionCallResult { Failure = TrailGuardV2PredictionFailure.ServiceUnavailable };
        }
    }
}

public enum TrailGuardV2HealthCheckStatus
{
    Healthy,
    Unreachable,
    UnexpectedResponse
}

public sealed record TrailGuardV2HealthCheckResult(TrailGuardV2HealthCheckStatus Status)
{
    public bool IsHealthy => Status == TrailGuardV2HealthCheckStatus.Healthy;
}

internal sealed class TrailGuardV2HealthResponse
{
    [JsonRequired, JsonPropertyName("status")] public string? Status { get; init; }
    [JsonRequired, JsonPropertyName("service")] public string? Service { get; init; }
    [JsonRequired, JsonPropertyName("model_version")] public string? ModelVersion { get; init; }
    [JsonRequired, JsonPropertyName("selected_tree_count")] public int? SelectedTreeCount { get; init; }
}

public sealed class TrailGuardV2ResponseValidationException : InvalidOperationException
{
    public TrailGuardV2ResponseValidationException() : base("The TrailGuard v2 adapter response is invalid.")
    {
    }
}

public static class TrailGuardV2ResponseValidator
{
    public const string ExpectedModelVersion = "trailguard-v2.0.0";
    public const string ExpectedFrozenModelSha256 = "bdc93bff426fadbcab6d4ce16b4a457050a6875fb67c355df0da5101caeb4e35";
    public const int ExpectedSelectedTreeCount = 985;
    public const double BinaryThreshold = 0.5;
    public const double BorderlineThreshold = 0.30;
    public const double GoodMatchThreshold = 0.80;
    public const double NativeTreeShapTolerance = 1e-4;
    public const string ExpectedUiLabelPolicyVersion = "inherited-0.30-0.80";
    public const string ExpectedExplanationScale = "raw_margin_log_odds";

    public static readonly IReadOnlyList<string> FrozenFeatureOrder =
    [
        "exercise_frequency",
        "cardio_duration",
        "exercise_consistency",
        "hiking_experience",
        "hiking_recency",
        "hardest_trail_completed",
        "gear_score",
        "distance_km",
        "elevation_gain_m",
        "trail_class",
        "typical_duration_hours"
    ];

    public static void Validate(
        TrailGuardV2PredictionResponse response,
        TrailGuardV2PredictionRequest request)
    {
        if (response is null
            || request is null
            || response.ModelVersion != ExpectedModelVersion
            || response.FrozenModelSha256 != ExpectedFrozenModelSha256
            || response.SelectedTreeCount != ExpectedSelectedTreeCount
            || response.ScoreName != "probability_yes"
            || !IsFiniteInRange(response.ModelScore, 0, 1)
            || response.BinaryThreshold != BinaryThreshold
            || response.BinaryPrediction != BinaryLabelFor(response.ModelScore!.Value)
            || response.UiLabelPolicyVersion != ExpectedUiLabelPolicyVersion
            || response.UiLabelPolicy is null
            || response.UiLabel != UiLabelFor(response.ModelScore!.Value))
        {
            ThrowInvalid();
        }

        ValidateUiPolicy(response.UiLabelPolicy!);
        ValidateExplanation(response.Shap!, response.ModelScore!.Value, request);
    }

    private static void ValidateUiPolicy(TrailGuardV2UiLabelPolicy policy)
    {
        if (policy.GoodMatch?.Operator != ">="
            || policy.GoodMatch.Threshold != GoodMatchThreshold
            || policy.Borderline?.MinimumOperator != ">="
            || policy.Borderline.Minimum != BorderlineThreshold
            || policy.Borderline.MaximumOperator != "<"
            || policy.Borderline.Maximum != GoodMatchThreshold
            || policy.NotRecommended?.Operator != "<"
            || policy.NotRecommended.Threshold != BorderlineThreshold)
        {
            ThrowInvalid();
        }
    }

    private static void ValidateExplanation(
        TrailGuardV2ShapResponse explanation,
        double score,
        TrailGuardV2PredictionRequest request)
    {
        if (explanation is null
            || !IsFinite(explanation.BaseValue)
            || !IsFinite(explanation.RawMargin)
            || explanation.Scale != ExpectedExplanationScale
            || string.IsNullOrWhiteSpace(explanation.ContributionInterpretation)
            || explanation.Contributions is null
            || explanation.Verification is null)
        {
            ThrowInvalid();
        }

        var contributions = explanation.Contributions!;
        if (contributions.Count != FrozenFeatureOrder.Count)
        {
            ThrowInvalid();
        }

        var seenFeatures = new HashSet<string>(StringComparer.Ordinal);
        var contributionSum = 0d;
        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (contribution is null
                || contribution.Feature != FrozenFeatureOrder[index]
                || !seenFeatures.Add(contribution.Feature)
                || !MatchesSubmittedInput(contribution.OriginalInputValue, contribution.Feature!, request)
                || !IsFinite(contribution.ShapContribution))
            {
                ThrowInvalid();
            }

            contributionSum += contribution.ShapContribution!.Value;
            if (!double.IsFinite(contributionSum))
            {
                ThrowInvalid();
            }
        }

        var verification = explanation.Verification!;
        if (verification.PredictionsUnchanged is not true
            || verification.WithinTolerance is not true
            || verification.FeatureOrderMatchesFrozenSchema is not true
            || verification.PredictionChangeAfterExplanation is not 0d
            || verification.ToleranceAbsolute != NativeTreeShapTolerance
            || !IsNonNegativeFiniteWithinTolerance(verification.AdditivityError)
            || !IsNonNegativeFiniteWithinTolerance(verification.ProbabilityReconstructionError))
        {
            ThrowInvalid();
        }

        var reconstructedMargin = explanation.BaseValue!.Value + contributionSum;
        var additivityError = Math.Abs(reconstructedMargin - explanation.RawMargin!.Value);
        var reconstructedProbability = Sigmoid(explanation.RawMargin.Value);
        var probabilityReconstructionError = Math.Abs(reconstructedProbability - score);
        if (!double.IsFinite(additivityError)
            || !double.IsFinite(probabilityReconstructionError)
            || additivityError > NativeTreeShapTolerance
            || probabilityReconstructionError > NativeTreeShapTolerance)
        {
            ThrowInvalid();
        }
    }

    private static bool MatchesSubmittedInput(
        JsonElement? originalInputValue,
        string feature,
        TrailGuardV2PredictionRequest request) => feature switch
    {
        "exercise_frequency" => IsExactString(originalInputValue, request.ExerciseFrequency),
        "cardio_duration" => IsExactString(originalInputValue, request.CardioDuration),
        "exercise_consistency" => IsExactString(originalInputValue, request.ExerciseConsistency),
        "hiking_experience" => IsExactString(originalInputValue, request.HikingExperience),
        "hiking_recency" => IsExactString(originalInputValue, request.HikingRecency),
        "hardest_trail_completed" => IsExactString(originalInputValue, request.HardestTrailCompleted),
        "gear_score" => IsExactNumber(originalInputValue, request.GearScore),
        "distance_km" => IsExactNumber(originalInputValue, request.DistanceKm),
        "elevation_gain_m" => IsExactNumber(originalInputValue, request.ElevationGainM),
        "trail_class" => IsExactNumber(originalInputValue, request.TrailClass),
        "typical_duration_hours" => IsExactNumber(originalInputValue, request.TypicalDurationHours),
        _ => false
    };

    private static bool IsExactString(JsonElement? value, string? expected)
    {
        return expected is not null
            && value.HasValue
            && value.Value.ValueKind == JsonValueKind.String
            && string.Equals(value.Value.GetString(), expected, StringComparison.Ordinal);
    }

    private static bool IsExactNumber(JsonElement? value, double expected)
    {
        return double.IsFinite(expected)
            && value.HasValue
            && value.Value.ValueKind == JsonValueKind.Number
            && value.Value.TryGetDouble(out var actual)
            && double.IsFinite(actual)
            && actual == expected;
    }

    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);

    private static bool IsFiniteInRange(double? value, double minimum, double maximum) =>
        IsFinite(value) && value!.Value >= minimum && value.Value <= maximum;

    private static bool IsNonNegativeFiniteWithinTolerance(double? value) =>
        IsFinite(value) && value!.Value >= 0 && value.Value <= NativeTreeShapTolerance;

    private static string BinaryLabelFor(double score) => score >= BinaryThreshold ? "Yes" : "No";

    private static string UiLabelFor(double score) => score >= GoodMatchThreshold
        ? "Good Match"
        : score >= BorderlineThreshold
            ? "Borderline"
            : "Not Recommended";

    private static double Sigmoid(double margin) => margin >= 0
        ? 1d / (1d + Math.Exp(-margin))
        : Math.Exp(margin) / (1d + Math.Exp(margin));

    [DoesNotReturn]
    private static void ThrowInvalid() => throw new TrailGuardV2ResponseValidationException();
}
