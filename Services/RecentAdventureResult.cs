namespace TrailGuard.Services
{
    // Purpose-built, display-only projection for a Profile's Recent Adventures card -
    // no Event ID, Trail ID, Registration ID, or Organizer ID (internal or public)
    // reaches a view through this type, only what's safe to render. Sourced from the
    // same canonical Accepted+Completed, distinct-EventId history as every other
    // ParticipantProgressService figure - see GetRecentAdventuresAsync.
    public sealed record RecentAdventureResult
    {
        public required string EventTitle { get; init; }
        public required string TrailName { get; init; }
        public required DateTime EventDate { get; init; }
        public string? Difficulty { get; init; }

        // "Unassigned" for a null OrganizerId (an unresolved legacy Event - see
        // CLAUDE.md, Event Lifecycle), "Organizer unavailable" for a non-null
        // OrganizerId that didn't resolve to any account - never a raw ID or email in
        // either case.
        public required string OrganizerDisplayName { get; init; }
    }
}
