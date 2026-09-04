using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    // Single place that captures a validated Trail into an Event's immutable
    // Trail Snapshot fields - see CLAUDE.md, "Event Trail Snapshot". Every
    // write site (EventController.AddEvent, EventController.EditEvent when the
    // organizer deliberately changes TrailId, Data/DbSeeder) must call this
    // rather than assigning Location/Difficulty/TrailXSnapshot individually, so
    // the fields captured together can never drift out of sync with each
    // other. The Trail passed in must already be loaded fresh from the
    // database by the caller - this method trusts it completely and never
    // re-reads or re-validates it, so a caller must never build one from
    // browser-posted values.
    public static class EventTrailSnapshotHelper
    {
        public static void CaptureSnapshot(Event eventItem, Trail trail)
        {
            eventItem.TrailId = trail.Id;
            eventItem.TrailNameSnapshot = trail.Name;
            eventItem.TrailDistanceKmSnapshot = trail.DistanceKm;
            eventItem.TrailElevationGainMetersSnapshot = trail.ElevationGainMeters;
            eventItem.TrailTerrainSnapshot = trail.Terrain;
            eventItem.TrailClassSnapshot = trail.TrailClass;
            eventItem.TrailThumbnailUrlSnapshot = trail.ThumbnailUrl;

            eventItem.Location = trail.Location;
            eventItem.TrailAdjustedRatingSnapshot = DifficultyCalculator.ComputeAdjustedRating(trail);
            eventItem.Difficulty = DifficultyCalculator.LabelFor(eventItem.TrailAdjustedRatingSnapshot);
        }

        // True when at least one Event's snapshot still points at this exact
        // stored thumbnail URL - checked before a Trail thumbnail replacement
        // or a Trail deletion is allowed to delete the underlying file. Deletes
        // are otherwise blocked while any Event's TrailId still references the
        // Trail (see TrailController.DeleteTrail), but a snapshot can outlive
        // that relationship - Edit Event may have since pointed the same Event
        // at a different Trail while keeping its old snapshot thumbnail - so
        // this checks the snapshot column directly rather than trusting the
        // current TrailId relationship.
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
