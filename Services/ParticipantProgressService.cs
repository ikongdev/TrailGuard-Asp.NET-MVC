using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{





    public record ParticipantProgressResult
    {
        public int DistinctCompletedEventCount { get; init; }
        public int DistinctCompletedTrailCount { get; init; }





        public int DistinctCompletionMonthCount { get; init; }




        public IReadOnlyList<int> DistinctCompletedTrailClasses { get; init; } = Array.Empty<int>();

        public int TrailPoints { get; init; }
        public string Tier { get; init; } = ParticipantProgressPolicy.TierFor(0);





        public string TierKey { get; init; } = ParticipantProgressPolicy.TierKeyFor(0);
        public int PointsIntoTier { get; init; }
        public int? PointsToNextTier { get; init; }
        public bool HasNextTier { get; init; }






        public int? Rank { get; init; }
        public int RankedParticipantCount { get; init; }
        public bool IsRanked { get; init; }








        public IReadOnlyList<ParticipantAchievementResult> Achievements { get; init; } = Array.Empty<ParticipantAchievementResult>();
        public int EarnedAchievementCount { get; init; }
        public int TotalAchievementCount { get; init; }



        public IEnumerable<ParticipantAchievementResult> EarnedAchievements => Achievements.Where(a => a.IsUnlocked);
        public IEnumerable<ParticipantAchievementResult> LockedAchievements => Achievements.Where(a => !a.IsUnlocked);
    }






    public class ParticipantProgressService
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleAssignmentService _roleAssignmentService;

        public ParticipantProgressService(ApplicationDbContext context, RoleAssignmentService roleAssignmentService)
        {
            _context = context;
            _roleAssignmentService = roleAssignmentService;
        }













        private IQueryable<EventRegistration> GetOwnQualifyingRegistrations(string userId) =>
            _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.UserId == userId
                    && r.Status == ParticipantProgressPolicy.QualifyingRegistrationStatus
                    && r.Event != null
                    && r.Event.Status == ParticipantProgressPolicy.QualifyingEventStatus);












        public async Task<ParticipantProgressResult> GetProgressAsync(string userId)
        {























            var qualifyingRows = await GetOwnQualifyingRegistrations(userId)
                .Select(r => new QualifyingEventRecord(
                    r.EventId,
                    r.Event!.TrailId,
                    r.Event.EventDate,
                    r.Event.TrailClassSnapshot,
                    r.Event.Difficulty))
                .ToListAsync();






            var chronologicalHistory = qualifyingRows
                .Distinct()
                .OrderBy(e => e.EventDate)
                .ThenBy(e => e.EventId)
                .ToList();

            var distinctCompletedEventCount = chronologicalHistory.Count;
            var distinctCompletedTrailCount = chronologicalHistory.Select(e => e.TrailId).Distinct().Count();




            var distinctCompletionMonthCount = chronologicalHistory
                .Select(e => (e.EventDate.Year, e.EventDate.Month))
                .Distinct()
                .Count();





            var distinctCompletedTrailClasses = chronologicalHistory
                .Where(e => e.TrailClass is >= 1 and <= 4)
                .Select(e => e.TrailClass!.Value)
                .Distinct()
                .OrderBy(c => c)
                .ToList();





            var achievements = ParticipantAchievementEvaluator.Evaluate(chronologicalHistory);
            var earnedAchievementCount = achievements.Count(a => a.IsUnlocked);

            var trailPoints = ParticipantProgressPolicy.ComputeTrailPoints(distinctCompletedEventCount, distinctCompletedTrailCount);
            var tier = ParticipantProgressPolicy.TierFor(trailPoints);
            var tierKey = ParticipantProgressPolicy.TierKeyFor(trailPoints);
            var pointsIntoTier = ParticipantProgressPolicy.PointsIntoTier(trailPoints);
            var pointsToNextTier = ParticipantProgressPolicy.PointsToNextTier(trailPoints);

            int? rank = null;
            var rankedParticipantCount = 0;
            var isRanked = false;





            if (distinctCompletedEventCount > 0)
            {





                var eligibleUserIds = await _roleAssignmentService.GetActiveUserIdsInSingleRoleAsync("Participant");
                var eligibleSet = eligibleUserIds.ToHashSet(StringComparer.Ordinal);

                var targetIsLeaderboardEligible = eligibleSet.Contains(userId);
                if (targetIsLeaderboardEligible)
                {







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
                TierKey = tierKey,
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










        public async Task<IReadOnlyList<RecentAdventureResult>> GetRecentAdventuresAsync(string userId, int maxCount = 20)
        {
            var rows = await GetOwnQualifyingRegistrations(userId)
                .Select(r => new
                {
                    r.EventId,
                    r.Event!.EventTitle,
                    TrailName = r.Event.TrailNameSnapshot,
                    r.Event.EventDate,
                    r.Event.Difficulty,
                    r.Event.OrganizerId
                })
                .ToListAsync();




            var newestFirst = rows
                .Distinct()
                .OrderByDescending(e => e.EventDate)
                .ThenByDescending(e => e.EventId)
                .Take(maxCount)
                .ToList();



            var organizerIds = newestFirst
                .Where(e => e.OrganizerId != null)
                .Select(e => e.OrganizerId!)
                .Distinct()
                .ToList();

            var organizerNames = organizerIds.Count == 0
                ? new Dictionary<string, string>()
                : await _context.Users
                    .AsNoTracking()
                    .Where(u => organizerIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FirstName, u.LastName })
                    .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

            return newestFirst.Select(e => new RecentAdventureResult
            {
                EventTitle = e.EventTitle,
                TrailName = string.IsNullOrEmpty(e.TrailName) ? "Unknown Trail" : e.TrailName,
                EventDate = e.EventDate,
                Difficulty = e.Difficulty,
                OrganizerDisplayName = e.OrganizerId == null
                    ? "Unassigned"
                    : organizerNames.TryGetValue(e.OrganizerId, out var name) ? name : "Organizer unavailable"
            }).ToList();
        }
    }
}
