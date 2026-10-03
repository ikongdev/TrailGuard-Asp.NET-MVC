using TrailGuard.Models;
using TrailGuard.Services;

static class Verify
{
    static void Equal<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }
    static void True(bool value, string name) { if (!value) throw new InvalidOperationException(name); }

    static void Main()
    {
        // User fixtures; no route names are used by the calculator.
        Equal(14.21m, DifficultyCalculator.ComputeScore(5.6, 512, 2.5m, 3), "fixture 1 score");
        Equal("Minor Hike", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(5.6, 512, 2.5m, 3)), "fixture 1 label");
        Equal(54.44m, DifficultyCalculator.ComputeScore(13.7, 2000, 9m, 4), "fixture 2 score");
        Equal("Major Hike", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(13.7, 2000, 9m, 4)), "fixture 2 label");
        Equal(86.50m, DifficultyCalculator.ComputeScore(20, 2250, 20m, 4), "fixture 3 score");
        Equal("Major Hike - Difficult", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(20, 2250, 20m, 4)), "fixture 3 label");

        Equal(35.00m, DifficultyCalculator.ComputeScore(34.995, 0, 6.999m, 1), "lower threshold rounding");
        Equal("Major Hike", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(34.995, 0, 6.999m, 1)), "lower threshold label");
        Equal(75.00m, DifficultyCalculator.ComputeScore(74.995, 0, 14.999m, 1), "upper threshold rounding");
        Equal("Major Hike - Difficult", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(74.995, 0, 14.999m, 1)), "upper threshold label");
        True(!DifficultyCalculator.TryValidateInputs(double.NaN, 0, 1m, 1, out _), "non-finite distance rejected");
        True(!DifficultyCalculator.TryValidateInputs(100.01, 0, 1m, 1, out _), "distance upper bound rejected");
        True(!DifficultyCalculator.TryValidateInputs(1, 10001, 1m, 1, out _), "gain upper bound rejected");
        True(!DifficultyCalculator.TryValidateInputs(1, 0, 24.01m, 1, out _), "duration upper bound rejected");
        True(!DifficultyCalculator.TryValidateInputs(1, 0, 1m, 5, out _), "class five rejected");

        Equal("Major Hike", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(40, 0, 8m, 1)), "long flat route");
        Equal("Major Hike", DifficultyCalculator.LabelForScore(DifficultyCalculator.ComputeScore(1.5, 300, 3m, 4)), "short technical route floor");
        True(DifficultyCalculator.ComputeScore(6, 400, 4m, 3) >= DifficultyCalculator.ComputeScore(5, 400, 4m, 3), "distance monotonic");
        True(DifficultyCalculator.ComputeScore(5, 500, 4m, 3) >= DifficultyCalculator.ComputeScore(5, 400, 4m, 3), "gain monotonic");
        True(DifficultyCalculator.ComputeScore(5, 400, 5m, 3) >= DifficultyCalculator.ComputeScore(5, 400, 4m, 3), "duration monotonic");
        True(DifficultyCalculator.ComputeScore(5, 400, 4m, 4) >= DifficultyCalculator.ComputeScore(5, 400, 4m, 3), "technical class effect");

        var trail = new Trail { Id = 9, Name = "Example", Location = "Place", DistanceKm = 5.6, TypicalDurationHours = 2.5m, ElevationGainMeters = 512, Terrain = "Rock", TrailClass = 3 };
        True(DifficultyCalculator.TryCompute(trail, out var previewScore, out var previewLabel, out _), "trail preview computes");
        var eventItem = new Event(); EventTrailSnapshotHelper.CaptureSnapshot(eventItem, trail);
        Equal(14.21m, eventItem.DifficultyScoreSnapshot, "new snapshot canonical score");
        Equal(previewScore, eventItem.DifficultyScoreSnapshot, "preview and capture score agree");
        Equal(previewLabel, eventItem.Difficulty, "preview and capture label agree");
        Equal(5.6d, eventItem.TrailDistanceKmSnapshot, "distance captured");
        trail.DistanceKm = 20; trail.ElevationGainMeters = 2250; trail.TypicalDurationHours = 20; trail.TrailClass = 4;
        Equal(14.21m, eventItem.DifficultyScoreSnapshot, "ordinary trail edit does not recapture event snapshot");

        var mixed = ParticipantAchievementEvaluator.Evaluate([
            new(1, 1, new DateTime(2026, 1, 1), 1, "Minor Hike"),
            new(2, 2, new DateTime(2026, 2, 1), 2, "Major Hike"),
            new(3, 3, new DateTime(2026, 3, 1), 3, "Major Hike - Difficult")]);
        var versatile = mixed.Single(x => x.Code == AchievementCodes.VersatileHiker);
        Equal(3, versatile.CurrentValue, "three difficulty categories counted"); True(versatile.IsUnlocked && versatile.EarnedAt == new DateTime(2026, 3, 1), "versatile hiker unlock date");
        Console.WriteLine("TrailGuard difficulty verification passed.");
    }
}
