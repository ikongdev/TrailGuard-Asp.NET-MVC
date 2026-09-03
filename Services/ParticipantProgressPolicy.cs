namespace TrailGuard.Services
{
    // Single source of truth for what counts as TrailGuard-recognized Participant
    // participation, and for every constant and pure calculation built on it - Trail
    // Points, progression tiers, and competition ranking. ParticipantProgressService is
    // the only caller that should ever run the underlying database queries; this class
    // owns none of that I/O, only the numbers and formulas so they can never drift
    // between the dashboard, a future Profile page, and future achievement work.
    //
    // "TrailGuard-recognized participation" is deliberately not "verified attendance" -
    // a Completed Event plus an Accepted Registration is the system's own record of
    // participation, not independent proof the participant physically attended. See
    // CLAUDE.md, Registration Domain Rules / Participant Progress.
    public static class ParticipantProgressPolicy
    {
        public const string QualifyingEventStatus = "Completed";
        public const string QualifyingRegistrationStatus = "Accepted";

        public const int PointsPerCompletedEvent = 10;
        public const int PointsPerDistinctTrail = 5;

        // One immutable tier entry - key, display name, and minimum Trail Points
        // together, so the three can never be independently edited out of sync with
        // each other (as three separate parallel arrays previously allowed).
        private sealed record ParticipantTierDefinition(string Key, string Name, int MinimumPoints);

        // The single source of truth for every tier's key, display name, and minimum
        // Trail Points threshold - replaces what used to be three separately stored,
        // index-aligned arrays (TierNames/TierThresholds/TierKeys). Ordered ascending
        // by MinimumPoints; TierIndexFor's scan below depends on this order. Private and
        // never exposed as a mutable list/array/record - every accessor in this class
        // reads through TierIndexFor/TierDefinitionFor rather than this field itself.
        private static readonly IReadOnlyList<ParticipantTierDefinition> Catalog = new List<ParticipantTierDefinition>
        {
            new("trail-starter", "Trail Starter", 0),
            new("pathfinder", "Pathfinder", 15),
            new("explorer", "Trail Explorer", 75),
            new("summit-seeker", "Summit Seeker", 150),
            new("trailblazer", "Trailblazer", 300)
        }.AsReadOnly();

        public static int ComputeTrailPoints(int distinctCompletedEventCount, int distinctCompletedTrailCount) =>
            distinctCompletedEventCount * PointsPerCompletedEvent + distinctCompletedTrailCount * PointsPerDistinctTrail;

        // The one search over Catalog every tier helper below is built on - the index
        // of the last entry whose MinimumPoints is <= trailPoints. Catalog[0].MinimumPoints
        // is 0, so a defensively-supplied negative trailPoints still resolves to index 0
        // (Trail Starter) rather than throwing or returning an out-of-range index.
        private static int TierIndexFor(int trailPoints)
        {
            var index = 0;
            for (var i = 0; i < Catalog.Count; i++)
            {
                if (trailPoints >= Catalog[i].MinimumPoints) index = i;
            }
            return index;
        }

        // Single shared resolution point for a Trail Points value's complete tier entry
        // - name, key, and threshold together. Every other tier accessor below (current
        // name, current key, next-tier name, points-to-next-tier, progress percent, safe
        // key validation) ultimately reads through this or TierIndexFor directly, so
        // there is exactly one threshold search in this class, never a separate one for
        // the display name and a separate one for the icon key.
        private static ParticipantTierDefinition TierDefinitionFor(int trailPoints) => Catalog[TierIndexFor(trailPoints)];

        public static string TierFor(int trailPoints) => TierDefinitionFor(trailPoints).Name;

        // The only legal source for a Tier emblem filename: `/images/tiers/tier-{key}.webp`.
        // Always derived from the same TierDefinitionFor/TierIndexFor lookup TierFor
        // itself uses, so the display name and the key can never come from two
        // independently-maintained threshold chains.
        public static string TierKeyFor(int trailPoints) => TierDefinitionFor(trailPoints).Key;

        // Defensive-only: TierKeyFor always returns a Catalog key by construction
        // (TierIndexFor's loop starts at index 0, and Catalog[0].MinimumPoints is 0, so
        // the index is always in range), so this can never actually fall through to the
        // fallback in practice. It exists so a Tier emblem path can never be built from
        // an unrecognized key even if a future caller ever constructs a
        // ParticipantProgressResult by hand with a bad value - falls back to the lowest
        // tier's key rather than exposing a broken path or a raw value.
        public static string SafeTierKey(string? tierKey) =>
            tierKey != null && Catalog.Any(t => t.Key == tierKey) ? tierKey : Catalog[0].Key;

        // Safe, presentation-only projection of one Catalog entry for the Profile Tier
        // preview carousel - never the private ParticipantTierDefinition itself. Carries
        // only what a view needs to render one carousel slide: the stable key (the only
        // legal source for that slide's emblem filename), the display name, whether this
        // is the participant's actual current tier, whether it has been reached at all
        // (current counts as reached), and its fixed display position.
        public sealed record ParticipantTierPreviewEntry(string Key, string Name, bool IsCurrent, bool IsUnlocked, int Position);

        // The only way a caller may enumerate all five tiers - every entry's IsCurrent/
        // IsUnlocked is computed from the same TierIndexFor lookup TierFor/TierKeyFor
        // themselves use, so a browsed carousel preview can never disagree with the
        // participant's actual tier. Returns a fresh, independent read-only list on
        // every call (via List<T>.AsReadOnly()) - never the private Catalog field
        // itself, and never a mutable array/List<T> a caller could alter.
        public static IReadOnlyList<ParticipantTierPreviewEntry> TierPreviewEntriesFor(int trailPoints)
        {
            var currentIndex = TierIndexFor(trailPoints);
            var entries = new List<ParticipantTierPreviewEntry>(Catalog.Count);
            for (var i = 0; i < Catalog.Count; i++)
            {
                entries.Add(new ParticipantTierPreviewEntry(Catalog[i].Key, Catalog[i].Name, i == currentIndex, i <= currentIndex, i));
            }
            return entries.AsReadOnly();
        }

        // Points already earned since crossing into the current tier's threshold - the
        // "filled" portion of an in-tier progress bar.
        public static int PointsIntoTier(int trailPoints) => trailPoints - TierDefinitionFor(trailPoints).MinimumPoints;

        // Points still needed to reach the next tier, or null while already at the top
        // tier (Trailblazer has no ceiling to progress toward).
        public static int? PointsToNextTier(int trailPoints)
        {
            var index = TierIndexFor(trailPoints);
            return index >= Catalog.Count - 1 ? null : Catalog[index + 1].MinimumPoints - trailPoints;
        }

        public static bool HasNextTier(int trailPoints) => PointsToNextTier(trailPoints) != null;

        // Name of the tier one step above the current one, or null at the top tier
        // (Trailblazer). Single source for a future Profile rank card's "X points to
        // [next tier]" copy, so that string is never independently re-derived from
        // Catalog a second time.
        public static string? NextTierName(int trailPoints)
        {
            var index = TierIndexFor(trailPoints);
            return index >= Catalog.Count - 1 ? null : Catalog[index + 1].Name;
        }

        // Percentage of the current tier's own point range already earned - 100 at
        // the top tier (Trailblazer), which has no ceiling to progress toward, so the
        // bar reads as complete rather than dividing by zero.
        public static int TierProgressPercent(int trailPoints)
        {
            var pointsIntoTier = PointsIntoTier(trailPoints);
            var pointsToNextTier = PointsToNextTier(trailPoints);
            if (pointsToNextTier == null) return 100;

            var tierWidth = pointsIntoTier + pointsToNextTier.Value;
            return tierWidth <= 0 ? 100 : (int)Math.Round(Math.Min(100.0, pointsIntoTier * 100.0 / tierWidth));
        }

        // Competition ("1224") ranking: equal Trail Points share the same rank, with no
        // arbitrary tie-breaker. `eligibleScores` must be exactly the Trail Points of
        // every ranked (score > 0) eligible Participant, including the one being ranked.
        public static int CompetitionRank(int participantScore, IEnumerable<int> eligibleScores) =>
            eligibleScores.Count(score => score > participantScore) + 1;
    }
}
