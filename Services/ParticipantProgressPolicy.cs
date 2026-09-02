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

        // Ordered easiest (fewest points) to hardest. Index-aligned with TierThresholds -
        // TierThresholds[i] is the minimum Trail Points to hold TierNames[i].
        public static readonly string[] TierNames =
        {
            "Trail Starter", "Pathfinder", "Trail Explorer", "Summit Seeker", "Trailblazer"
        };

        private static readonly int[] TierThresholds = { 0, 15, 75, 150, 300 };

        public static int ComputeTrailPoints(int distinctCompletedEventCount, int distinctCompletedTrailCount) =>
            distinctCompletedEventCount * PointsPerCompletedEvent + distinctCompletedTrailCount * PointsPerDistinctTrail;

        private static int TierIndexFor(int trailPoints)
        {
            var index = 0;
            for (var i = 0; i < TierThresholds.Length; i++)
            {
                if (trailPoints >= TierThresholds[i]) index = i;
            }
            return index;
        }

        public static string TierFor(int trailPoints) => TierNames[TierIndexFor(trailPoints)];

        // Points already earned since crossing into the current tier's threshold - the
        // "filled" portion of an in-tier progress bar.
        public static int PointsIntoTier(int trailPoints) => trailPoints - TierThresholds[TierIndexFor(trailPoints)];

        // Points still needed to reach the next tier, or null while already at the top
        // tier (Trailblazer has no ceiling to progress toward).
        public static int? PointsToNextTier(int trailPoints)
        {
            var index = TierIndexFor(trailPoints);
            return index >= TierThresholds.Length - 1 ? null : TierThresholds[index + 1] - trailPoints;
        }

        public static bool HasNextTier(int trailPoints) => PointsToNextTier(trailPoints) != null;

        // Competition ("1224") ranking: equal Trail Points share the same rank, with no
        // arbitrary tie-breaker. `eligibleScores` must be exactly the Trail Points of
        // every ranked (score > 0) eligible Participant, including the one being ranked.
        public static int CompetitionRank(int participantScore, IEnumerable<int> eligibleScores) =>
            eligibleScores.Count(score => score > participantScore) + 1;
    }
}
