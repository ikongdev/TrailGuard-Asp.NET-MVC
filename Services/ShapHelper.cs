using TrailGuard.Models;

namespace TrailGuard.Services;

public static class ShapHelper
{
    public const string Actionable = "actionable";
    public const string Context = "context";
    public const string Medical = "medical";
    public const string Trail = "trail";




    private static readonly IReadOnlyDictionary<string, (string FriendlyName, string Category)> Features =
        new Dictionary<string, (string, string)>
        {
            ["bmi"] = ("Body mass index", Context),
            ["exercise_frequency"] = ("Exercise frequency", Actionable),
            ["cardio_duration"] = ("Cardio endurance", Actionable),
            ["exercise_consistency"] = ("Exercise consistency", Actionable),
            ["hiking_experience"] = ("Hiking experience", Context),
            ["last_hike_recency"] = ("Recency of last hike", Context),
            ["hardest_trail_completed"] = ("Hardest trail completed", Context),
            ["gear_score"] = ("Gear preparedness", Actionable),
            ["has_asthma"] = ("Asthma", Medical),
            ["has_cvd"] = ("Heart condition or hypertension", Medical),
            ["has_joint_knee_injury"] = ("Joint or knee injury", Medical),
            ["has_signs_symptoms"] = ("Cardiovascular signs or symptoms", Medical),
            ["distance_km"] = ("Trail distance", Trail),
            ["elevation_gain_m"] = ("Trail elevation gain", Trail),
            ["trail_class"] = ("Trail technical class", Trail),
            ["typical_duration_hours"] = ("Trail duration", Trail)
        };



    private static readonly string[] FeatureOrder = Features.Keys.ToArray();

    public static void ValidateResponseFeatures(IReadOnlyCollection<ShapAllFeatureImpactDto> factors)
    {
        if (factors.Count != Features.Count || factors.Select(f => f.Feature).Distinct().Count() != Features.Count)
            throw new InvalidOperationException("The ML response did not contain exactly the 16 v3 SHAP features.");

        foreach (var factor in factors)
        {
            var expected = GetFeature(factor.Feature);
            if (factor.Category != expected.Category)
                throw new InvalidOperationException($"Unexpected SHAP category for '{factor.Feature}': '{factor.Category}'.");
        }
    }

    public static void ValidateDisplayBreakdown(
        IReadOnlyCollection<ShapFeatureImpactDto> breakdown,
        IReadOnlyCollection<ShapAllFeatureImpactDto> allFactors)
    {
        if (breakdown.Count > 5 || breakdown.Select(f => f.Feature).Distinct().Count() != breakdown.Count)
            throw new InvalidOperationException("The ML response contained an invalid SHAP display breakdown.");

        if (breakdown.Count == 0) return;

        if (Math.Abs(breakdown.Sum(f => f.SharePct) - 100) > 0.2)
            throw new InvalidOperationException("The ML SHAP display shares did not total 100%.");

        var allByFeature = allFactors.ToDictionary(f => f.Feature);
        foreach (var factor in breakdown)
        {
            if (!allByFeature.TryGetValue(factor.Feature, out var rawFactor)
                || factor.Category != rawFactor.Category
                || factor.RawValue != rawFactor.RawValue
                || factor.ShapValue != rawFactor.ShapValue)
            {
                throw new InvalidOperationException("The ML SHAP display breakdown did not match its raw values.");
            }
        }
    }

    public static List<ShapDisplayItem> BuildDisplayItems(ICollection<ShapValue> shapValues)
    {
        if (shapValues == null || !shapValues.Any()) return new List<ShapDisplayItem>();

        var displayed = shapValues
            .Where(s => s.DisplayOrder.HasValue && s.DisplaySharePct.HasValue && s.DisplayFriendlyName != null)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        return displayed.Select(s => new ShapDisplayItem
            {
                FeatureName = s.FeatureName,
                FriendlyName = s.DisplayFriendlyName!,
                Category = s.Category,
                RawValue = s.RawValue ?? "",
                Impact = s.ImpactValue,
                BarWidth = s.DisplaySharePct!.Value
            }).ToList();
    }



    public static List<string> BuildRecommendations(ICollection<ShapValue> shapValues)
    {
        var totalImpact = shapValues.Sum(s => Math.Abs(s.ImpactValue));
        if (totalImpact == 0) return new List<string>();

        return shapValues
            .Where(s => s.ImpactValue < 0 && s.Category == Actionable)
            .Where(s => 100 * Math.Abs(s.ImpactValue) / totalImpact >= 2.0)
            .OrderBy(s => s.ImpactValue)
            .ThenBy(s => Array.IndexOf(FeatureOrder, s.FeatureName))
            .Take(3)
            .Select(s => RecommendationText(s.FeatureName))
            .ToList();
    }

    private static (string FriendlyName, string Category) GetFeature(string featureName) =>
        Features.TryGetValue(featureName, out var feature)
            ? feature
            : throw new ArgumentException($"Unrecognized SHAP feature name: '{featureName}'");

    private static string RecommendationText(string featureName) => featureName switch
    {
        "exercise_frequency" => "Increase how often you exercise each week.",
        "cardio_duration" => "Build up how long you can sustain cardio without stopping.",
        "exercise_consistency" => "Build a consistent routine before the hike.",
        "gear_score" => "Complete your gear checklist before the hike.",
        _ => throw new InvalidOperationException($"Non-actionable SHAP feature selected for a recommendation: '{featureName}'.")
    };
}
