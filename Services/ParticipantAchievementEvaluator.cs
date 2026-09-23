namespace TrailGuard.Services
{














    public sealed record QualifyingEventRecord(int EventId, int TrailId, DateTime EventDate, int? TrailClass, string? Difficulty);








    public static class ParticipantAchievementEvaluator
    {





        private static bool IsValidTrailClass(int? trailClass) => trailClass is >= 1 and <= 4;










        private static string? NormalizeDifficulty(string? rawDifficulty)
        {
            if (string.IsNullOrWhiteSpace(rawDifficulty)) return null;
            var trimmed = rawDifficulty.Trim();
            foreach (var band in DifficultyCalculator.Bands)
            {
                if (string.Equals(trimmed, band, StringComparison.OrdinalIgnoreCase)) return band;
            }
            return null;
        }






        public static IReadOnlyList<ParticipantAchievementResult> Evaluate(IReadOnlyList<QualifyingEventRecord> chronologicalHistory)
        {
            DateTime? firstAdventureDate = null;
            DateTime? fiveAdventuresDate = null;
            DateTime? doubleDigitsDate = null;
            DateTime? newGroundDate = null;
            DateTime? trailCollectorDate = null;
            DateTime? steadyStepsDate = null;
            DateTime? seasonedExplorerDate = null;
            DateTime? technicalExplorerDate = null;
            DateTime? versatileHikerDate = null;






            var seenTrails = new HashSet<int>();
            var seenMonths = new HashSet<(int Year, int Month)>();
            var seenTrailClasses = new HashSet<int>();
            var seenDifficulties = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < chronologicalHistory.Count; i++)
            {
                var qualifyingEvent = chronologicalHistory[i];
                var completedSoFar = i + 1;

                if (completedSoFar == 1) firstAdventureDate = qualifyingEvent.EventDate;
                if (completedSoFar == 5) fiveAdventuresDate = qualifyingEvent.EventDate;
                if (completedSoFar == 10) doubleDigitsDate = qualifyingEvent.EventDate;

                if (seenTrails.Add(qualifyingEvent.TrailId))
                {
                    if (seenTrails.Count == 3) newGroundDate = qualifyingEvent.EventDate;
                    if (seenTrails.Count == 5) trailCollectorDate = qualifyingEvent.EventDate;
                }

                var monthKey = (qualifyingEvent.EventDate.Year, qualifyingEvent.EventDate.Month);
                if (seenMonths.Add(monthKey))
                {
                    if (seenMonths.Count == 3) steadyStepsDate = qualifyingEvent.EventDate;
                    if (seenMonths.Count == 6) seasonedExplorerDate = qualifyingEvent.EventDate;
                }

                if (IsValidTrailClass(qualifyingEvent.TrailClass) && seenTrailClasses.Add(qualifyingEvent.TrailClass!.Value))
                {
                    if (seenTrailClasses.Count == 3) technicalExplorerDate = qualifyingEvent.EventDate;
                }

                var normalizedDifficulty = NormalizeDifficulty(qualifyingEvent.Difficulty);
                if (normalizedDifficulty != null && seenDifficulties.Add(normalizedDifficulty))
                {
                    if (seenDifficulties.Count == 3) versatileHikerDate = qualifyingEvent.EventDate;
                }
            }

            var totalCompleted = chronologicalHistory.Count;
            var distinctTrails = seenTrails.Count;
            var distinctMonths = seenMonths.Count;
            var distinctValidTrailClasses = seenTrailClasses.Count;
            var distinctValidDifficulties = seenDifficulties.Count;

            var results = new List<ParticipantAchievementResult>(ParticipantAchievementCatalog.Definitions.Count);
            foreach (var definition in ParticipantAchievementCatalog.Definitions)
            {
                var (currentValue, earnedAt) = definition.Code switch
                {
                    AchievementCodes.FirstAdventure => (totalCompleted, firstAdventureDate),
                    AchievementCodes.FiveAdventures => (totalCompleted, fiveAdventuresDate),
                    AchievementCodes.DoubleDigits => (totalCompleted, doubleDigitsDate),
                    AchievementCodes.NewGround => (distinctTrails, newGroundDate),
                    AchievementCodes.TrailCollector => (distinctTrails, trailCollectorDate),
                    AchievementCodes.SteadySteps => (distinctMonths, steadyStepsDate),
                    AchievementCodes.SeasonedExplorer => (distinctMonths, seasonedExplorerDate),
                    AchievementCodes.TechnicalExplorer => (distinctValidTrailClasses, technicalExplorerDate),
                    AchievementCodes.VersatileHiker => (distinctValidDifficulties, versatileHikerDate),
                    _ => throw new InvalidOperationException($"Unhandled achievement code '{definition.Code}'.")
                };

                var isUnlocked = currentValue >= definition.TargetValue;
                var clampedProgress = Math.Min(currentValue, definition.TargetValue);
                var progressPercent = (int)Math.Round(Math.Min(100.0, clampedProgress * 100.0 / definition.TargetValue));

                results.Add(new ParticipantAchievementResult
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    Description = definition.Description,
                    Category = definition.Category,
                    CurrentValue = currentValue,
                    TargetValue = definition.TargetValue,
                    ClampedProgress = clampedProgress,
                    ProgressPercent = progressPercent,
                    IsUnlocked = isUnlocked,
                    EarnedAt = isUnlocked ? earnedAt : null,
                    DisplayOrder = definition.DisplayOrder,
                    AssetKey = definition.AssetKey,
                    IconClass = definition.IconClass
                });
            }

            return results;
        }
    }
}
