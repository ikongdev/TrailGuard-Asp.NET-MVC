namespace TrailGuard.Models
{
    // Page-level shape for Trail/Index - replaces the old @model IEnumerable<Trail>
    // now that the page must render two distinct sets: the Active-only main grid
    // and a read-only Deactivated Trails summary. Built once in TrailController.Index
    // from bounded, consolidated queries - never one query per Trail.
    public class TrailManagementViewModel
    {
        // Active Trails only, in the requested sort order - the main grid.
        public List<Trail> ActiveTrails { get; set; } = new List<Trail>();

        // All active Trails, independent of client-side search - the header badge's
        // "Active Trails: N" value. Search is client-side (see Trail/Index.cshtml),
        // so this must not change while typing.
        public int ActiveTrailCount { get; set; }

        public int DeactivatedTrailCount { get; set; }

        public List<DeactivatedTrailRowViewModel> DeactivatedTrails { get; set; } = new List<DeactivatedTrailRowViewModel>();
    }

    // One row in the read-only Deactivated Trails modal. Event counts are grouped by
    // TrailId from a single consolidated query in TrailController.Index - never a
    // per-row database fetch - and always sum to TotalCount.
    public class DeactivatedTrailRowViewModel
    {
        public int TrailId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;

        public int UpcomingCount { get; set; }
        public int CompletedCount { get; set; }
        public int CancelledCount { get; set; }

        // Every linked Event whose stored Status is not Upcoming/Completed/Cancelled
        // (a stray/legacy status) - shown only when greater than zero, but always
        // included in TotalCount.
        public int OtherCount { get; set; }

        public int TotalCount { get; set; }
    }
}
