using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class ShapHelper
    {
        public static string GetFriendlyFeatureName(string featureName) => featureName switch
        {
            "age" => "Age",
            "height_cm" => "Height",
            "weight_kg" => "Weight",
            "bmi" => "Body Mass Index",
            "has_asthma" => "Asthma condition",
            "has_hypertension_heart_condition" => "Hypertension / heart condition",
            "has_joint_knee_injury" => "Joint or knee injury",
            "has_vertigo" => "Vertigo",
            "exercise_frequency_score" => "Exercise frequency",
            "exercise_type_category" => "Type of exercise",
            "continuous_cardio_duration_score" => "Cardio endurance",
            "hiking_experience_score" => "Hiking experience",
            "last_hike_recency_score" => "Recency of last hike",
            "hardest_trail_completed_score" => "Hardest trail completed",
            "gear_water" => "Water supply",
            "gear_trail_food" => "Trail food",
            "gear_first_aid_medicine" => "First aid kit",
            "gear_flashlight_headlamp" => "Flashlight / headlamp",
            "gear_whistle" => "Whistle",
            "gear_raincoat_poncho" => "Raincoat / poncho",
            "gear_navigation" => "Navigation tool",
            "gear_proper_shoes" => "Proper hiking shoes",
            "gear_score" => "Overall gear preparedness",
            "trail_distance_km" => "Trail distance",
            "trail_elevation_gain_m" => "Trail elevation gain",
            "trail_terrain_type" => "Terrain difficulty",
            "trail_estimated_duration_hr" => "Estimated hike duration",
            _ => featureName
        };

        public static List<ShapDisplayItem> BuildDisplayItems(ICollection<ShapValue> shapValues, int take = 6)
        {
            if (shapValues == null || !shapValues.Any())
                return new List<ShapDisplayItem>();

            var maxAbsImpact = shapValues.Max(s => Math.Abs(s.ImpactValue));
            if (maxAbsImpact == 0) maxAbsImpact = 1;

            return shapValues
                .OrderByDescending(s => Math.Abs(s.ImpactValue))
                .Take(take)
                .Select(s => new ShapDisplayItem
                {
                    FeatureName = s.FeatureName,
                    FriendlyName = GetFriendlyFeatureName(s.FeatureName),
                    RawValue = s.RawValue ?? "",
                    Impact = s.ImpactValue,
                    BarWidth = Math.Round(Math.Abs(s.ImpactValue) / maxAbsImpact * 100, 1)
                })
                .ToList();
        }
    }
}