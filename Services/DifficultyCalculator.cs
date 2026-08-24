using TrailGuard.Models;

namespace TrailGuard.Services
{
    // NPS Shenandoah hiking difficulty rating - mirrors TrailGuard-ML/acsm_gate.py's
    // shenandoah_rating()/nps_band()/nps_pace_mph(). These two files must be changed
    // together. ComputeRating is the PLAIN, un-terrain-adjusted value - a property of
    // the trail's geometry alone, matching what main.py returns as nps_score and used
    // for pace/duration only. Ordering and display must use ComputeAdjustedRating - the
    // same value the difficulty band is derived from - or a short Class 4 trail sorts
    // as though it were an easy walk. Band labelling multiplies this by the trail's own
    // TrailClass multiplier before applying the
    // PinoyMountaineer-derived boundaries below - that's still a trail-level number
    // (TrailClass doesn't vary per participant, only Trail does), it's just a more
    // realistic one for Philippine trails, most of which sit at or above the top of
    // the published NPS bands.
    //
    // SOURCE (rating formula): National Park Service, Shenandoah National Park.
    //         "How to Determine Hiking Difficulty."
    //         Rating = sqrt(elevation_gain_ft * 2 * distance_mi)
    //         Pace bands: 1.5 / 1.4 / 1.3 / 1.2 mph at <50 / 50-100 / 100-150 / >=150.
    //
    // SOURCE (difficulty bands): boundaries fitted against 28 Philippine mountains
    //         with published PinoyMountaineer difficulty ratings (1-9 scale), applied
    //         to the NPS rating x TrailClass multiplier. Spearman rho 0.859 between
    //         the adjusted rating and the published PM rating; 82% exact-tier
    //         agreement, 100% agreement within one tier. This is NOT the
    //         PinoyMountaineer scale itself - applying PM's own written rule
    //         (duration + trail class) reproduced its published ratings only 50% of
    //         the time, because multi-day status in the Philippines often reflects
    //         logistics (e.g. camping for sunrise) rather than difficulty. The
    //         boundaries were fitted and validated on the same 28-mountain sample -
    //         see MODEL.md for the calibration caveat.
    public static class DifficultyCalculator
    {
        public static readonly string[] Bands =
        {
            "Easy", "Minor Climb", "Major Climb", "Major Climb — Difficult"
        };

        // PinoyMountaineer's own published level range for each band, shown
        // alongside the name since that's the scale Filipino hikers actually
        // recognise. Index-aligned with Bands.
        public static readonly string[] PinoyMountaineerRanges =
        {
            "PM 1–2/9", "PM 3–4/9", "PM 5–6/9", "PM 7–9/9"
        };

        // PinoyMountaineer Trail Class 1-4 only - classes 5/6 (technical rock
        // climbing, aid climbing) are excluded; no organized hiking event runs
        // those for general participants.
        // SOURCE: fitted against 28 Philippine mountains with published
        // PinoyMountaineer difficulty ratings, Spearman rho 0.859.
        private static readonly Dictionary<int, double> TerrainMultiplier = new()
        {
            { 1, 1.00 }, // Walking
            { 2, 1.15 }, // Hiking
            { 3, 1.35 }, // Scrambling
            { 4, 1.60 }, // Simple Climbing
        };

        public static double ComputeRating(Trail trail)
        {
            var elevationFt = trail.ElevationGainMeters * 3.28084;
            var distanceMi = trail.DistanceKm / 1.60934;
            return Math.Sqrt(elevationFt * 2.0 * distanceMi);
        }

        // 1.0 for an unclassified trail (TrailClass outside 1-4) - not a real
        // classification, just enough to keep the difficulty label from throwing
        // before an organizer assigns one.
        public static double GetTerrainMultiplier(int trailClass) =>
            TerrainMultiplier.TryGetValue(trailClass, out var m) ? m : 1.0;

        public static double ComputeAdjustedRating(Trail trail) =>
            ComputeRating(trail) * GetTerrainMultiplier(trail.TrailClass);

        // Boundaries fitted on the adjusted rating (NPS rating x TrailClass
        // multiplier) - see the SOURCE note above.
        public static string LabelFor(double adjustedRating)
        {
            if (adjustedRating < 81) return "Easy";
            if (adjustedRating < 354) return "Minor Climb";
            if (adjustedRating < 411) return "Major Climb";
            return "Major Climb — Difficult";
        }

        public static string ComputeDifficulty(Trail trail)
        {
            return LabelFor(ComputeAdjustedRating(trail));
        }

        // Pace is about the raw geometry of the trail, not its technicality, so this
        // stays on the plain (un-adjusted) rating.
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
        public static string BadgeClass(string? difficultyLabel) => difficultyLabel switch
        {
            "Easy" => "badge-easy",
            "Minor Climb" => "badge-lime",
            "Major Climb" => "badge-orange",
            "Major Climb — Difficult" => "badge-hard",
            _ => "text-slate-400 bg-slate-500/15"
        };

        public static string PmRangeFor(string? difficultyLabel)
        {
            var i = Array.IndexOf(Bands, difficultyLabel);
            return i >= 0 ? PinoyMountaineerRanges[i] : "";
        }

        public static string TrailClassLabel(int trailClass) => trailClass switch
        {
            1 => "Walking",
            2 => "Hiking",
            3 => "Scrambling",
            4 => "Simple Climbing",
            _ => "N/A"
        };
    }
}
