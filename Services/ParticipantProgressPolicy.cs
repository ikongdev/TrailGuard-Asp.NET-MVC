namespace TrailGuard.Services
{











    public static class ParticipantProgressPolicy
    {
        public const string QualifyingEventStatus = "Completed";
        public const string QualifyingRegistrationStatus = "Accepted";

        public const int PointsPerCompletedEvent = 10;
        public const int PointsPerDistinctTrail = 5;




        private sealed record ParticipantTierDefinition(string Key, string Name, int MinimumPoints);







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





        private static int TierIndexFor(int trailPoints)
        {
            var index = 0;
            for (var i = 0; i < Catalog.Count; i++)
            {
                if (trailPoints >= Catalog[i].MinimumPoints) index = i;
            }
            return index;
        }







        private static ParticipantTierDefinition TierDefinitionFor(int trailPoints) => Catalog[TierIndexFor(trailPoints)];

        public static string TierFor(int trailPoints) => TierDefinitionFor(trailPoints).Name;





        public static string TierKeyFor(int trailPoints) => TierDefinitionFor(trailPoints).Key;








        public static string SafeTierKey(string? tierKey) =>
            tierKey != null && Catalog.Any(t => t.Key == tierKey) ? tierKey : Catalog[0].Key;







        public sealed record ParticipantTierPreviewEntry(string Key, string Name, bool IsCurrent, bool IsUnlocked, int Position);







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



        public static int PointsIntoTier(int trailPoints) => trailPoints - TierDefinitionFor(trailPoints).MinimumPoints;



        public static int? PointsToNextTier(int trailPoints)
        {
            var index = TierIndexFor(trailPoints);
            return index >= Catalog.Count - 1 ? null : Catalog[index + 1].MinimumPoints - trailPoints;
        }

        public static bool HasNextTier(int trailPoints) => PointsToNextTier(trailPoints) != null;





        public static string? NextTierName(int trailPoints)
        {
            var index = TierIndexFor(trailPoints);
            return index >= Catalog.Count - 1 ? null : Catalog[index + 1].Name;
        }




        public static int TierProgressPercent(int trailPoints)
        {
            var pointsIntoTier = PointsIntoTier(trailPoints);
            var pointsToNextTier = PointsToNextTier(trailPoints);
            if (pointsToNextTier == null) return 100;

            var tierWidth = pointsIntoTier + pointsToNextTier.Value;
            return tierWidth <= 0 ? 100 : (int)Math.Round(Math.Min(100.0, pointsIntoTier * 100.0 / tierWidth));
        }




        public static int CompetitionRank(int participantScore, IEnumerable<int> eligibleScores) =>
            eligibleScores.Count(score => score > participantScore) + 1;
    }
}
