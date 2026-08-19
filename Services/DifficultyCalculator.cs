using TrailGuard.Models;

namespace TrailGuard.Services
{
    // NPS Shenandoah hiking difficulty rating - mirrors TrailGuard-ML/acsm_gate.py's
    // shenandoah_rating()/nps_band()/nps_pace_mph(). These two files must be changed
    // together. The rating here is deliberately the PLAIN, un-terrain-adjusted value,
    // matching what main.py returns as nps_score/nps_band and what Report.cshtml
    // displays - this is a property of the trail itself, the same number for every
    // participant. The terrain multiplier only matters for the ACSM gate's matching
    // logic (demand vs. an individual's capacity), which already lives in
    // acsm_gate.py and has no business being folded into a number shown to someone
    // who hasn't even taken the assessment yet.
    //
    // SOURCE: National Park Service, Shenandoah National Park.
    //         "How to Determine Hiking Difficulty."
    //         Rating = sqrt(elevation_gain_ft * 2 * distance_mi)
    //         Bands: <50 Easiest | 50-100 Moderate | 100-150 Moderately Strenuous
    //                | 150-200 Strenuous | >200 Very Strenuous
    //         Pace bands: 1.5 / 1.4 / 1.3 / 1.2 mph respectively.
    public static class DifficultyCalculator
    {
        public static readonly string[] Bands =
        {
            "Easiest", "Moderate", "Moderately Strenuous", "Strenuous", "Very Strenuous"
        };

        public static double ComputeRating(Trail trail)
        {
            var elevationFt = trail.ElevationGainMeters * 3.28084;
            var distanceMi = trail.DistanceKm / 1.60934;
            return Math.Sqrt(elevationFt * 2.0 * distanceMi);
        }

        public static string LabelFor(double rating)
        {
            if (rating < 50) return "Easiest";
            if (rating < 100) return "Moderate";
            if (rating < 150) return "Moderately Strenuous";
            if (rating < 200) return "Strenuous";
            return "Very Strenuous";
        }

        public static string ComputeDifficulty(Trail trail)
        {
            return LabelFor(ComputeRating(trail));
        }

        public static double PaceMph(double rating)
        {
            if (rating < 50) return 1.5;
            if (rating < 100) return 1.4;
            if (rating < 150) return 1.3;
            return 1.2;
        }

        // Suggested starting duration from the NPS pace band for this rating - a
        // prefilled default the organizer can override, not a value silently forced
        // onto every event.
        public static double SuggestedDurationHours(Trail trail)
        {
            var rating = ComputeRating(trail);
            var pace = PaceMph(rating);
            var distanceMi = trail.DistanceKm / 1.60934;
            return distanceMi / pace;
        }

        // Single source for band -> badge CSS class. Every view must call this rather
        // than re-deriving its own difficulty->color mapping - the same hardcoded
        // vocabulary has been found duplicated across views three times now.
        // Five bands, five distinct colors (see wwwroot/css/input.css for the classes).
        public static string BadgeClass(string? difficultyLabel) => difficultyLabel switch
        {
            "Easiest" => "badge-easy",
            "Moderate" => "badge-lime",
            "Moderately Strenuous" => "badge-moderate",
            "Strenuous" => "badge-orange",
            "Very Strenuous" => "badge-hard",
            _ => "text-slate-400 bg-slate-500/15"
        };

        public static string TerrainTypeLabel(int terrainType) => terrainType switch
        {
            1 => "Established",
            2 => "Rugged",
            3 => "Technical",
            _ => "N/A"
        };
    }
}
