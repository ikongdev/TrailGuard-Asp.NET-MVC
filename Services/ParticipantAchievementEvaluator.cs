namespace TrailGuard.Services
{
    // One deduplicated qualifying Event, in the exact shape both
    // ParticipantProgressService's own stats and ParticipantAchievementEvaluator need -
    // nothing else. EventDate is the Event's own scheduled date, never the
    // administrative CompletedAt timestamp - see CLAUDE.md, Participant Progress /
    // Achievements: earned dates and calendar-month membership both use this field
    // only, so a Steady Steps/Seasoned Explorer "month" and a completion milestone's
    // earned date can never disagree about which date defines an Event.
    // Difficulty is the raw Event.Difficulty string, exactly as stored - always
    // one of DifficultyCalculator.Bands in practice (Event.Difficulty is only ever
    // set via DifficultyCalculator.ComputeDifficulty, never free-typed), but the
    // column itself carries no database-level constraint, so this stays a plain,
    // unvalidated string here. NormalizeDifficulty (below) is what turns it into a
    // trusted, canonical value - the same "validate at evaluation time" split
    // TrailClass/IsValidTrailClass already uses.
    public sealed record QualifyingEventRecord(int EventId, int TrailId, DateTime EventDate, int? TrailClass, string? Difficulty);

    // Pure, dynamic evaluator - no database access, no persistence, no side effects,
    // and no notion of "already unlocked" carried between calls. Every call
    // recomputes the full result from scratch against whatever qualifying history is
    // passed in, so a corrected Registration or Event status is reflected the very
    // next time this runs, and an achievement can just as easily re-lock if the
    // history that satisfied it no longer does. See CLAUDE.md, Participant Progress /
    // Achievements.
    public static class ParticipantAchievementEvaluator
    {
        // Trail.TrailClass is documented as PinoyMountaineer Trail Class 1-4
        // (Walking/Hiking/Scrambling/Simple Climbing) - see Trail.cs and
        // DifficultyCalculator's TerrainMultiplier keys, the same valid range. A null
        // or out-of-range value (an unclassified Trail) must never advance Technical
        // Explorer.
        private static bool IsValidTrailClass(int? trailClass) => trailClass is >= 1 and <= 4;

        // Matches DifficultyCalculator.Bands - the single canonical Event Difficulty
        // source (see Services/DifficultyCalculator.cs) - after trimming and
        // case-insensitive comparison, returning the canonical Bands casing so two
        // differently-cased matches for the same band collapse to one HashSet entry
        // below. Null, empty, whitespace-only, and anything not matching one of the
        // four Bands values (a stray/legacy row) returns null and must never advance
        // Versatile Hiker - this is deliberately not the same trust level as
        // TrailClass's already-validated int, since Difficulty is a free string
        // column with no database constraint.
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

        // chronologicalHistory must already be deduplicated to one entry per
        // qualifying EventId and ordered by EventDate ascending, then EventId
        // ascending as a deterministic tie-breaker for same-day Events - see
        // ParticipantProgressService.GetProgressAsync, the only caller. This method
        // does not re-sort or re-deduplicate its input.
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

            // "First appearance" trackers - a Trail/month/Trail Class only ever
            // contributes to its milestone the first time it's seen while walking the
            // history in chronological order, exactly as the earned-date rules require
            // (repeating a Trail advances the completed-Event count but never
            // New Ground/Trail Collector's distinct-Trail progress).
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
