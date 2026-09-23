using TrailGuard.Models;

namespace TrailGuard.Services
{




























    public static class DifficultyCalculator
    {
        public static readonly string[] Bands =
        {
            "Easy", "Minor Climb", "Major Climb", "Major Climb — Difficult"
        };




        public static readonly string[] PinoyMountaineerRanges =
        {
            "PM 1–2/9", "PM 3–4/9", "PM 5–6/9", "PM 7–9/9"
        };






        private static readonly Dictionary<int, double> TerrainMultiplier = new()
        {
            { 1, 1.00 },
            { 2, 1.15 },
            { 3, 1.35 },
            { 4, 1.60 },
        };

        public static double ComputeRating(Trail trail)
        {
            var elevationFt = trail.ElevationGainMeters * 3.28084;
            var distanceMi = trail.DistanceKm / 1.60934;
            return Math.Sqrt(elevationFt * 2.0 * distanceMi);
        }




        public static double GetTerrainMultiplier(int trailClass) =>
            TerrainMultiplier.TryGetValue(trailClass, out var m) ? m : 1.0;

        public static double ComputeAdjustedRating(Trail trail) =>
            ComputeRating(trail) * GetTerrainMultiplier(trail.TrailClass);



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
