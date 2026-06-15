using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class DifficultyCalculator
    {
        public static string ComputeDifficulty(Trail trail, double estimatedDurationHours)
        {
            double score = 0;

            // Distance Factor
            if (trail.DistanceKm <= 5) score += 0.5;
            else if (trail.DistanceKm <= 10) score += 1;
            else if (trail.DistanceKm <= 15) score += 1.5;
            else if (trail.DistanceKm <= 20) score += 2;
            else score += 3;

            // Elevation Gain Factor
            if (trail.ElevationGainMeters <= 200) score += 0.5;
            else if (trail.ElevationGainMeters <= 500) score += 1;
            else if (trail.ElevationGainMeters <= 1000) score += 1.5;
            else if (trail.ElevationGainMeters <= 1500) score += 2;
            else score += 3;

            // Terrain Factor
            string terrain = trail.Terrain.ToLower();
            if (terrain.Contains("grassland") || terrain.Contains("pine forest"))
                score += 0.5;
            else if (terrain.Contains("mossy forest") || terrain.Contains("river trek"))
                score += 1;
            else if (terrain.Contains("rocky") || terrain.Contains("volcanic"))
                score += 1.5;
            else if (terrain.Contains("muddy") || terrain.Contains("mixed"))
                score += 2;

            // Duration Factor
            if (estimatedDurationHours <= 4) score += 0.5;
            else if (estimatedDurationHours <= 8) score += 1;
            else if (estimatedDurationHours <= 12) score += 1.5;
            else score += 2;

            int rating = (int)Math.Ceiling(score);
            if (rating < 1) rating = 1;
            if (rating > 9) rating = 9;

            // Return simplified difficulty
            if (rating <= 3)
                return "Easy";
            else if (rating <= 6)
                return "Moderate";
            else
                return "Difficult";
        }
    }
}