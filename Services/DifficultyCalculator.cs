using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class DifficultyCalculator
    {
        public static string ComputeDifficulty(Trail trail, double estimatedDurationHours)
        {
            return LabelFor(ComputeRating(trail, estimatedDurationHours));
        }

        public static int ComputeRating(Trail trail, double estimatedDurationHours)
        {
            double score = 0;

            if (trail.DistanceKm <= 5) score += 0.5;
            else if (trail.DistanceKm <= 8) score += 1.0;
            else if (trail.DistanceKm <= 12) score += 1.5;
            else if (trail.DistanceKm <= 18) score += 2.0;
            else if (trail.DistanceKm <= 25) score += 2.5;
            else score += 3.0;

            if (trail.ElevationGainMeters <= 300) score += 0.5;
            else if (trail.ElevationGainMeters <= 600) score += 1.0;
            else if (trail.ElevationGainMeters <= 900) score += 1.5;
            else if (trail.ElevationGainMeters <= 1300) score += 2.0;
            else if (trail.ElevationGainMeters <= 1800) score += 2.5;
            else score += 3.0;

            var terrain = (trail.Terrain ?? "").ToLower();
            if (terrain.Contains("rocky") || terrain.Contains("boulder") || terrain.Contains("volcanic"))
                score += 2.0;
            else if (terrain.Contains("muddy") || terrain.Contains("mixed"))
                score += 1.5;
            else if (terrain.Contains("mossy") || terrain.Contains("river"))
                score += 1.0;
            else
                score += 0.5;

            if (estimatedDurationHours <= 4) score += 0.5;
            else if (estimatedDurationHours <= 7) score += 1.0;
            else if (estimatedDurationHours <= 10) score += 1.5;
            else if (estimatedDurationHours <= 16) score += 2.0;
            else score += 3.0;

            var rating = (int)Math.Ceiling(score);
            return Math.Clamp(rating, 1, 9);
        }

        public static string LabelFor(int rating)
        {
            if (rating <= 3) return "Easy";
            if (rating <= 6) return "Moderate";
            return "Difficult";
        }
    }
}