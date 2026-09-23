using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{










    public static class EventTrailSnapshotHelper
    {
        public static void CaptureSnapshot(Event eventItem, Trail trail)
        {
            eventItem.TrailId = trail.Id;
            eventItem.TrailNameSnapshot = trail.Name;
            eventItem.TrailDistanceKmSnapshot = trail.DistanceKm;
            eventItem.TrailDurationHoursSnapshot = trail.TypicalDurationHours;
            eventItem.TrailElevationGainMetersSnapshot = trail.ElevationGainMeters;
            eventItem.TrailTerrainSnapshot = trail.Terrain;
            eventItem.TrailClassSnapshot = trail.TrailClass;
            eventItem.TrailThumbnailUrlSnapshot = trail.ThumbnailUrl;

            eventItem.Location = trail.Location;
            eventItem.TrailAdjustedRatingSnapshot = DifficultyCalculator.ComputeAdjustedRating(trail);
            eventItem.Difficulty = DifficultyCalculator.LabelFor(eventItem.TrailAdjustedRatingSnapshot);
        }










        public static Task<bool> IsThumbnailUrlReferencedByAnyEventAsync(ApplicationDbContext context, string? thumbnailUrl)
        {
            if (string.IsNullOrEmpty(thumbnailUrl))
            {
                return Task.FromResult(false);
            }

            return context.Events.AnyAsync(e => e.TrailThumbnailUrlSnapshot == thumbnailUrl);
        }
    }
}
