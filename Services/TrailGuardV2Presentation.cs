using System.Text.Json;
using System.Globalization;
using TrailGuard.Models;

namespace TrailGuard.Services;

public static class TrailGuardV2Presentation
{
    private static readonly IReadOnlyDictionary<string, string> FriendlyNames = new Dictionary<string, string>
    {
        ["exercise_frequency"] = "Exercise frequency",
        ["cardio_duration"] = "Cardio session duration",
        ["exercise_consistency"] = "Exercise consistency",
        ["hiking_experience"] = "Hiking experience",
        ["hiking_recency"] = "Hiking recency",
        ["hardest_trail_completed"] = "Hardest trail completed",
        ["gear_score"] = "Gear preparedness",
        ["distance_km"] = "Trail distance",
        ["elevation_gain_m"] = "Elevation gain",
        ["trail_class"] = "Trail class",
        ["typical_duration_hours"] = "Trail duration"
    };

    private static readonly HashSet<string> Actionable = ["exercise_frequency", "cardio_duration", "exercise_consistency", "gear_score"];

    public static bool IsRecognizedV2(SuitabilityResult? result) => result is not null
        && result.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion
        && result.FrozenModelSha256 == TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256
        && result.SelectedTreeCount == TrailGuardV2ResponseValidator.ExpectedSelectedTreeCount
        && result.ScoreName == "probability_yes"
        && double.IsFinite(result.ModelScore) && result.ModelScore is >= 0 and <= 1
        && result.BinaryThreshold == TrailGuardV2ResponseValidator.BinaryThreshold
        && result.BinaryPrediction == BinaryLabelFor(result.ModelScore)
        && result.UiLabelPolicyVersion == TrailGuardV2ResponseValidator.ExpectedUiLabelPolicyVersion
        && result.GoodMatchOperator == ">=" && result.GoodMatchThreshold == 0.80
        && result.BorderlineMinimumOperator == ">=" && result.BorderlineMinimum == 0.30
        && result.BorderlineMaximumOperator == "<" && result.BorderlineMaximum == 0.80
        && result.NotRecommendedOperator == "<" && result.NotRecommendedThreshold == 0.30
        && result.UiLabel == UiLabelFor(result.ModelScore)
        && IsFinite(result.ShapBaseValue) && IsFinite(result.ShapRawMargin)
        && result.ShapScale == TrailGuardV2ResponseValidator.ExpectedExplanationScale
        && result.ShapVerificationPredictionsUnchanged is true
        && result.ShapVerificationWithinTolerance is true
        && result.ShapVerificationFeatureOrderMatchesFrozenSchema is true
        && result.ShapVerificationPredictionChangeAfterExplanation is 0d
        && result.ShapVerificationToleranceAbsolute == TrailGuardV2ResponseValidator.NativeTreeShapTolerance
        && IsErrorWithinTolerance(result.ShapVerificationAdditivityError)
        && IsErrorWithinTolerance(result.ShapVerificationProbabilityReconstructionError)
        && result.ShapValues.Count == TrailGuardV2ResponseValidator.FrozenFeatureOrder.Count
        && result.ShapValues.All(value => value.FeatureOrder.HasValue
            && double.IsFinite(value.ShapContribution)
            && !string.IsNullOrWhiteSpace(value.OriginalInputValue))
        && result.ShapValues.OrderBy(value => value.FeatureOrder).Select(value => value.FeatureOrder!.Value)
            .SequenceEqual(Enumerable.Range(0, TrailGuardV2ResponseValidator.FrozenFeatureOrder.Count))
        && result.ShapValues.OrderBy(value => value.FeatureOrder).Select(value => value.FeatureName)
            .SequenceEqual(TrailGuardV2ResponseValidator.FrozenFeatureOrder)
        && IsConsistentStoredExplanation(result);

    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);

    private static bool IsErrorWithinTolerance(double? value) => IsFinite(value)
        && value!.Value >= 0 && value.Value <= TrailGuardV2ResponseValidator.NativeTreeShapTolerance;

    private static string BinaryLabelFor(double score) => score >= TrailGuardV2ResponseValidator.BinaryThreshold ? "Yes" : "No";

    private static string UiLabelFor(double score) => score >= TrailGuardV2ResponseValidator.GoodMatchThreshold
        ? "Good Match"
        : score >= TrailGuardV2ResponseValidator.BorderlineThreshold ? "Borderline" : "Not Recommended";

    private static bool IsConsistentStoredExplanation(SuitabilityResult result)
    {
        var rawMargin = result.ShapRawMargin!.Value;
        var reconstructedMargin = result.ShapBaseValue!.Value + result.ShapValues.Sum(value => value.ShapContribution);
        var additivityError = Math.Abs(reconstructedMargin - rawMargin);
        var reconstructedScore = rawMargin >= 0
            ? 1d / (1d + Math.Exp(-rawMargin))
            : Math.Exp(rawMargin) / (1d + Math.Exp(rawMargin));
        var scoreError = Math.Abs(reconstructedScore - result.ModelScore);
        return double.IsFinite(additivityError)
            && double.IsFinite(scoreError)
            && additivityError <= TrailGuardV2ResponseValidator.NativeTreeShapTolerance
            && scoreError <= TrailGuardV2ResponseValidator.NativeTreeShapTolerance;
    }

    public static List<ShapDisplayItem> BuildV2Factors(IEnumerable<ShapValue> values)
    {
        var ordered = values.OrderByDescending(value => Math.Abs(value.ShapContribution))
            .ThenBy(value => value.FeatureOrder ?? int.MaxValue).ToList();
        var largest = ordered.Select(value => Math.Abs(value.ShapContribution)).DefaultIfEmpty().Max();
        return ordered.Select(value => new ShapDisplayItem
        {
            FeatureName = value.FeatureName,
            FriendlyName = FriendlyNames.TryGetValue(value.FeatureName, out var friendly) ? friendly : value.FeatureName,
            Category = value.Category,
            OriginalInputValue = FormatOriginalInputValue(value.OriginalInputValue),
            Impact = value.ShapContribution,
            BarWidth = largest == 0 ? 0 : Math.Abs(value.ShapContribution) / largest * 100,
            Direction = value.ShapContribution > 0 ? "raised" : value.ShapContribution < 0 ? "lowered" : "neutral"
        }).ToList();
    }

    public static List<string> BuildV2Suggestions(IEnumerable<ShapValue> values) => values
        .Where(value => value.ShapContribution < 0 && Actionable.Contains(value.FeatureName))
        .OrderBy(value => value.ShapContribution).ThenBy(value => value.FeatureOrder ?? int.MaxValue).Take(3)
        .Select(value => value.FeatureName switch
        {
            "exercise_frequency" => "Consider building a regular exercise routine before the event.",
            "cardio_duration" => "Consider gradually building the duration of comfortable cardio sessions.",
            "exercise_consistency" => "Consider maintaining a consistent routine before the event.",
            "gear_score" => "Review the event gear checklist before the event.",
            _ => string.Empty
        }).Where(text => text.Length > 0).ToList();

    public static string FormatDuration(double? hours)
    {
        if (!hours.HasValue || !double.IsFinite(hours.Value) || hours.Value < 0) return "N/A";
        var minutes = (int)Math.Round(hours.Value * 60, MidpointRounding.AwayFromZero);
        return $"{minutes / 60}h {minutes % 60}min";
    }

    /// <summary>Formats a stored raw model score for display only; it never changes score semantics.</summary>
    public static string FormatModelScore(double score)
    {
        if (!double.IsFinite(score) || score < 0 || score > 1)
        {
            return "N/A";
        }

        if (score is 0 or 1)
        {
            return (score * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        var roundedPercentage = Math.Round(score * 100, 2, MidpointRounding.AwayFromZero);
        if (roundedPercentage >= 100)
        {
            return ">99.99%";
        }

        if (roundedPercentage <= 0)
        {
            return "<0.01%";
        }

        return roundedPercentage.ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatOriginalInputValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "N/A";
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() ?? "N/A" : document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
