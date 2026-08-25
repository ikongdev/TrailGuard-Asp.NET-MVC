using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class ShapHelper
    {
        // Exactly the 14 v2 SHAP feature names — kept in sync with
        // AssessmentController's participant-facing report by being the only
        // copy of this mapping (see CLAUDE.md, Explainability). Throws rather
        // than falling through to the raw snake_case string - a name we don't
        // recognize means the Python feature contract moved and this mapping
        // wasn't updated with it, and a raw "has_cvd_symptoms" shown to an
        // organizer deciding whether to approve someone is worse than a crash
        // caught in development.
        public static string GetFriendlyFeatureName(string featureName) => featureName switch
        {
            "bmi" => "Body Mass Index",
            "exercise_frequency_score" => "Exercise frequency",
            "continuous_cardio_duration_score" => "Cardio endurance",
            "exercise_consistency_score" => "How long you've been exercising",
            "hiking_experience_score" => "Hiking experience",
            "last_hike_recency_score" => "Recency of last hike",
            "hardest_trail_completed_score" => "Hardest trail completed",
            "gear_score" => "Gear preparedness",
            "has_asthma" => "Asthma or lung condition",
            "has_cvd" => "Hypertension or heart condition",
            "has_joint_knee_injury" => "Joint or knee injury",
            "has_cvd_symptoms" => "Reported symptoms",
            "trail_shenandoah_score" => "Trail difficulty rating",
            "trail_terrain_type" => "Trail class",
            _ => throw new ArgumentException($"Unrecognized SHAP feature name: '{featureName}'")
        };

        // BarWidth is each factor's share of the TOTAL impact among the displayed
        // (post-take) items, not a share of the single largest one - matching
        // CLAUDE.md's documented "share of total displayed impact." A max-relative
        // bar always stretches the largest factor to 100% regardless of how much
        // of the outcome it actually explains, which overstates it.
        public static List<ShapDisplayItem> BuildDisplayItems(ICollection<ShapValue> shapValues, int take = 6)
        {
            if (shapValues == null || !shapValues.Any())
                return new List<ShapDisplayItem>();

            var topShapValues = shapValues
                .OrderByDescending(s => Math.Abs(s.ImpactValue))
                .Take(take)
                .ToList();

            var totalAbsImpact = topShapValues.Sum(s => Math.Abs(s.ImpactValue));
            if (totalAbsImpact == 0) totalAbsImpact = 1;

            return topShapValues
                .Select(s => new ShapDisplayItem
                {
                    FeatureName = s.FeatureName,
                    FriendlyName = GetFriendlyFeatureName(s.FeatureName),
                    RawValue = s.RawValue ?? "",
                    Impact = s.ImpactValue,
                    BarWidth = Math.Round(Math.Abs(s.ImpactValue) / totalAbsImpact * 100, 1)
                })
                .ToList();
        }
    }
}