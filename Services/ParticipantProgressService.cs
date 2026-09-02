using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;

namespace TrailGuard.Services
{
    // Purpose-built projection returned by ParticipantProgressService - never an
    // ApplicationUser or EF entity graph, and carries nothing from
    // Assessment/SuitabilityResult/EventFeedback/PostEventAssessment/payment or
    // medical fields. Safe to hand to the Participant Dashboard today and to a future
    // Profile view without a second privacy review of this type itself.
    public record ParticipantProgressResult
    {
        public int DistinctCompletedEventCount { get; init; }
        public int DistinctCompletedTrailCount { get; init; }

        // Distinct (year, month) of qualifying completions - for a future "active N
        // different months" consistency achievement. Not surfaced anywhere yet.
        public int DistinctCompletionMonthCount { get; init; }

        // Distinct Trail.TrailClass values across qualifying completions - for a future
        // terrain-variety achievement. Not surfaced anywhere yet.
        public IReadOnlyList<int> DistinctCompletedTrailClasses { get; init; } = Array.Empty<int>();

        public int TrailPoints { get; init; }
        public string Tier { get; init; } = ParticipantProgressPolicy.TierNames[0];
        public int PointsIntoTier { get; init; }
        public int? PointsToNextTier { get; init; }
        public bool HasNextTier { get; init; }

        // Null/0/false whenever the target isn't leaderboard-eligible - inactive,
        // conflicted/missing-role, Admin/Organizer, or zero qualifying completions -
        // determined entirely inside GetProgressAsync, never by a caller-supplied flag.
        // A clean active Participant always gets a Tier (Trail Starter at zero points)
        // but only an eligible, ranked Participant gets a placement.
        public int? Rank { get; init; }
        public int RankedParticipantCount { get; init; }
        public bool IsRanked { get; init; }
    }

    // The sole source for Participant progress and all-time leaderboard ranking -
    // the Dashboard, and every future Profile/achievement consumer, must call this
    // rather than holding a second copy of the qualifying-history query or the Trail
    // Points formula. See ParticipantProgressPolicy for the constants/pure math this
    // service applies to the data it reads.
    public class ParticipantProgressService
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleAssignmentService _roleAssignmentService;

        public ParticipantProgressService(ApplicationDbContext context, RoleAssignmentService roleAssignmentService)
        {
            _context = context;
            _roleAssignmentService = roleAssignmentService;
        }

        // userId is always the internal Identity Id - a caller resolving a
        // PublicProfileId (e.g. a future ProfileController via ProfileAccessService)
        // must translate it to this before calling here.
        //
        // Leaderboard eligibility is decided entirely inside this method, never by a
        // caller-supplied flag - a caller (Dashboard today, a future Profile view)
        // must never be able to accidentally rank an inactive, conflicted, missing-
        // role, Admin, or Organizer account just by passing the wrong boolean. History
        // and tier always compute regardless of eligibility (an Admin viewing an
        // inactive-but-clean Participant's historical Profile still needs both); only
        // Rank/IsRanked/RankedParticipantCount depend on it.
        public async Task<ParticipantProgressResult> GetProgressAsync(string userId)
        {
            // One query for this Participant's own qualifying history. Duplicate
            // Accepted+Completed rows for the same EventId shouldn't exist in practice
            // (RegistrationStatusHelper.ActiveStatuses blocks a second active
            // registration for the same event) but the schema doesn't forbid it at the
            // database level (see CLAUDE.md, "Resolve a registration by status, never
            // FirstOrDefault alone") - the GroupBy below is the deduplication step that
            // makes every count that follows safe regardless.
            var ownRows = await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.UserId == userId
                    && r.Status == ParticipantProgressPolicy.QualifyingRegistrationStatus
                    && r.Event != null
                    && r.Event.Status == ParticipantProgressPolicy.QualifyingEventStatus)
                .Select(r => new
                {
                    r.EventId,
                    r.Event!.TrailId,
                    r.Event.CompletedAt,
                    r.Event.EventDate,
                    TrailClass = r.Event.Trail != null ? r.Event.Trail.TrailClass : (int?)null
                })
                .ToListAsync();

            var distinctEvents = ownRows
                .GroupBy(r => r.EventId)
                .Select(g => g.First())
                .ToList();

            var distinctCompletedEventCount = distinctEvents.Count;
            var distinctCompletedTrailCount = distinctEvents.Select(e => e.TrailId).Distinct().Count();
            var distinctCompletionMonthCount = distinctEvents
                .Select(e => (e.CompletedAt ?? e.EventDate).ToString("yyyy-MM"))
                .Distinct()
                .Count();
            var distinctCompletedTrailClasses = distinctEvents
                .Where(e => e.TrailClass.HasValue)
                .Select(e => e.TrailClass!.Value)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var trailPoints = ParticipantProgressPolicy.ComputeTrailPoints(distinctCompletedEventCount, distinctCompletedTrailCount);
            var tier = ParticipantProgressPolicy.TierFor(trailPoints);
            var pointsIntoTier = ParticipantProgressPolicy.PointsIntoTier(trailPoints);
            var pointsToNextTier = ParticipantProgressPolicy.PointsToNextTier(trailPoints);

            int? rank = null;
            var rankedParticipantCount = 0;
            var isRanked = false;

            // Zero qualifying completions can never be leaderboard-eligible regardless
            // of role/active status, so the role-integrity read below is skipped
            // entirely for that (common, new-account) case rather than run and then
            // discarded.
            if (distinctCompletedEventCount > 0)
            {
                // Bounded eligibility read (2 queries, constant regardless of how many
                // Participants exist), not GetRolesAsync in a per-user loop. This is the
                // one and only source of leaderboard eligibility - it already excludes
                // inactive, conflicted, missing-role, Admin, and Organizer accounts, so
                // nothing else in this method (or any caller) decides eligibility.
                var eligibleUserIds = await _roleAssignmentService.GetActiveUserIdsInSingleRoleAsync("Participant");
                var eligibleSet = eligibleUserIds.ToHashSet(StringComparer.Ordinal);

                var targetIsLeaderboardEligible = eligibleSet.Contains(userId);
                if (targetIsLeaderboardEligible)
                {
                    // One grouped aggregate over every eligible Participant's qualifying
                    // rows - not one query per ranked Participant. Filtering
                    // r.UserId to eligibleSet here (rather than joining against a
                    // Users/UserRoles table) keeps this a single indexed
                    // EventRegistrations scan plus an in-memory Contains check, which is
                    // fine at this data scale and avoids re-deriving role integrity in
                    // SQL.
                    var eligibleRows = await _context.EventRegistrations
                        .AsNoTracking()
                        .Where(r => r.Status == ParticipantProgressPolicy.QualifyingRegistrationStatus
                            && r.Event != null
                            && r.Event.Status == ParticipantProgressPolicy.QualifyingEventStatus
                            && eligibleSet.Contains(r.UserId))
                        .Select(r => new { r.UserId, r.EventId, r.Event!.TrailId })
                        .ToListAsync();

                    var rankedScores = eligibleRows
                        .GroupBy(r => r.UserId)
                        .Select(g =>
                        {
                            var events = g.Select(x => x.EventId).Distinct().Count();
                            var trails = g.Select(x => x.TrailId).Distinct().Count();
                            return ParticipantProgressPolicy.ComputeTrailPoints(events, trails);
                        })
                        // Every id grouped here already has >= 1 qualifying row (it came
                        // from eligibleRows), and PointsPerCompletedEvent > 0, so every
                        // group's score is already > 0 - "ranked" is exactly this set.
                        .ToList();

                    rankedParticipantCount = rankedScores.Count;
                    rank = ParticipantProgressPolicy.CompetitionRank(trailPoints, rankedScores);
                    isRanked = true;
                }
            }

            return new ParticipantProgressResult
            {
                DistinctCompletedEventCount = distinctCompletedEventCount,
                DistinctCompletedTrailCount = distinctCompletedTrailCount,
                DistinctCompletionMonthCount = distinctCompletionMonthCount,
                DistinctCompletedTrailClasses = distinctCompletedTrailClasses,
                TrailPoints = trailPoints,
                Tier = tier,
                PointsIntoTier = pointsIntoTier,
                PointsToNextTier = pointsToNextTier,
                HasNextTier = pointsToNextTier != null,
                Rank = rank,
                RankedParticipantCount = rankedParticipantCount,
                IsRanked = isRanked
            };
        }
    }
}
