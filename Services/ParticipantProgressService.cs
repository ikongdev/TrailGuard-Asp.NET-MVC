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

        // Distinct (year, month) of qualifying completions - the same figure the
        // Steady Steps/Seasoned Explorer achievements' CurrentValue reaches once every
        // Event has been walked. A standalone diagnostic aggregate; no view renders it
        // directly.
        public int DistinctCompletionMonthCount { get; init; }

        // Distinct valid (1-4) Trail.TrailClass values across qualifying completions -
        // the same figure the Technical Explorer achievement's CurrentValue reaches. A
        // standalone diagnostic aggregate; no view renders it directly.
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

        // Dynamic achievement results - see ParticipantAchievementEvaluator. Computed
        // fresh from the same deduplicated qualifying history as everything above on
        // every call; nothing here is ever written or persisted, so a target with zero
        // qualifying completions still gets all eight results (every one locked at
        // CurrentValue 0), and a corrected Registration/Event status changes this the
        // very next time GetProgressAsync runs. Always in the catalog's fixed display
        // order.
        public IReadOnlyList<ParticipantAchievementResult> Achievements { get; init; } = Array.Empty<ParticipantAchievementResult>();
        public int EarnedAchievementCount { get; init; }
        public int TotalAchievementCount { get; init; }

        // Safe derived views for a future Profile UI - computed on read, not
        // duplicated/stored lists, so they can never drift from Achievements itself.
        public IEnumerable<ParticipantAchievementResult> EarnedAchievements => Achievements.Where(a => a.IsUnlocked);
        public IEnumerable<ParticipantAchievementResult> LockedAchievements => Achievements.Where(a => !a.IsUnlocked);
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
            // One query for this Participant's own qualifying history - the single
            // shared source for every stat below AND for achievement evaluation
            // (ParticipantAchievementEvaluator.Evaluate takes this same list; no
            // second history query exists anywhere in this method).
            //
            // Duplicate Accepted+Completed rows for the same EventId shouldn't exist in
            // practice (RegistrationStatusHelper.ActiveStatuses blocks a second active
            // registration for the same event) but the schema doesn't forbid it at the
            // database level (see CLAUDE.md, "Resolve a registration by status, never
            // FirstOrDefault alone"). Deduplication below handles it regardless -
            // EventId, TrailId, EventDate, and TrailClass are all derived from Event
            // (never from the registration row itself), so two duplicate rows for the
            // same EventId always project to an identical QualifyingEventRecord, and a
            // plain value-equality Distinct() collapses them with no dependency on
            // which row the database happens to return first - a stricter, more
            // deterministic replacement for a GroupBy(...).First().
            var qualifyingRows = await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.UserId == userId
                    && r.Status == ParticipantProgressPolicy.QualifyingRegistrationStatus
                    && r.Event != null
                    && r.Event.Status == ParticipantProgressPolicy.QualifyingEventStatus)
                .Select(r => new QualifyingEventRecord(
                    r.EventId,
                    r.Event!.TrailId,
                    r.Event.EventDate,
                    r.Event.Trail != null ? r.Event.Trail.TrailClass : (int?)null))
                .ToListAsync();

            // Chronological order for achievement earned-date derivation: Event date
            // ascending, then Event ID ascending as a deterministic secondary key for
            // same-day Events (this can only affect which of two same-day Events is
            // "first" for tie-breaking purposes, never which calendar month/Trail/Trail
            // Class is considered first - see ParticipantAchievementEvaluator).
            var chronologicalHistory = qualifyingRows
                .Distinct()
                .OrderBy(e => e.EventDate)
                .ThenBy(e => e.EventId)
                .ToList();

            var distinctCompletedEventCount = chronologicalHistory.Count;
            var distinctCompletedTrailCount = chronologicalHistory.Select(e => e.TrailId).Distinct().Count();

            // Calendar year+month of the Event's own EventDate only - never
            // CompletedAt - so this always agrees with Steady Steps/Seasoned
            // Explorer's month-membership rule below.
            var distinctCompletionMonthCount = chronologicalHistory
                .Select(e => (e.EventDate.Year, e.EventDate.Month))
                .Distinct()
                .Count();

            // Valid canonical Trail Classes only (1-4 - see
            // ParticipantAchievementEvaluator.IsValidTrailClass), matching Technical
            // Explorer's own validity rule so this diagnostic aggregate can never
            // disagree with the achievement built from the same data.
            var distinctCompletedTrailClasses = chronologicalHistory
                .Where(e => e.TrailClass is >= 1 and <= 4)
                .Select(e => e.TrailClass!.Value)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Pure, in-memory evaluation against the exact same deduplicated
            // chronological history above - zero additional database queries, and no
            // per-achievement query or loop (a single O(n) pass inside Evaluate builds
            // every achievement's progress and earned date together).
            var achievements = ParticipantAchievementEvaluator.Evaluate(chronologicalHistory);
            var earnedAchievementCount = achievements.Count(a => a.IsUnlocked);

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
                IsRanked = isRanked,
                Achievements = achievements,
                EarnedAchievementCount = earnedAchievementCount,
                TotalAchievementCount = ParticipantAchievementCatalog.Definitions.Count
            };
        }
    }
}
