using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>TrailGuard's provisional, display-only route-effort difficulty policy.</summary>
public static class DifficultyCalculator
{
    public const int ScoreDecimalPlaces = 2;
    public static readonly string[] Bands = ["Minor Hike", "Major Hike", "Major Hike - Difficult"];

    // All weights, floor, thresholds, and precision are provisional TrailGuard policy constants.
    private const decimal DistanceHoursPerKm = 1m / 5m, GainHoursPerMeter = 1m / 600m, DurationExcessWeight = .25m, ScoreScale = 5m, Class4ScoreFloor = 35m, MajorThreshold = 35m, DifficultThreshold = 75m;
    private static readonly Dictionary<int, decimal> TechnicalClassMultiplier = new() { { 1, 1m }, { 2, 1.15m }, { 3, 1.35m }, { 4, 1.60m } };

    public static bool TryValidateInputs(double distanceKm, int gainMeters, decimal ascentHours, int technicalClass, out string? error)
    {
        if (!double.IsFinite(distanceKm) || distanceKm <= 0 || distanceKm > 100) { error = "Distance must be greater than 0 and at most 100 km."; return false; }
        if (gainMeters < 0 || gainMeters > 10000) { error = "Elevation gain must be from 0 to 10,000 m."; return false; }
        if (ascentHours <= 0 || ascentHours > 24) { error = "Typical duration must be greater than 0 and at most 24 hours."; return false; }
        if (!TechnicalClassMultiplier.ContainsKey(technicalClass)) { error = "Technical trail class must be 1, 2, 3, or 4."; return false; }
        error = null; return true;
    }

    public static bool TryCompute(Trail trail, out decimal score, out string label, out string? error)
    {
        score = 0; label = string.Empty;
        if (!TryValidateInputs(trail.DistanceKm, trail.ElevationGainMeters, trail.TypicalDurationHours, trail.TrailClass, out error))
        {
            return false;
        }
        score = ComputeScore(trail.DistanceKm, trail.ElevationGainMeters, trail.TypicalDurationHours, trail.TrailClass);
        label = LabelForScore(score); return true;
    }

    public static bool TryCompute(Event eventItem, out decimal score, out string label, out string? error)
    {
        score = 0; label = string.Empty;
        if (!TryValidateInputs(eventItem.TrailDistanceKmSnapshot, eventItem.TrailElevationGainMetersSnapshot,
            eventItem.TrailDurationHoursSnapshot, eventItem.TrailClassSnapshot, out error)) return false;
        score = ComputeScore(eventItem.TrailDistanceKmSnapshot, eventItem.TrailElevationGainMetersSnapshot,
            eventItem.TrailDurationHoursSnapshot, eventItem.TrailClassSnapshot);
        label = LabelForScore(score); return true;
    }

    public static decimal ComputeScore(double distanceKm, int elevationGainMeters, decimal typicalDurationHours, int trailClass)
    {
        if (!TryValidateInputs(distanceKm, elevationGainMeters, typicalDurationHours, trailClass, out var error)) throw new ArgumentOutOfRangeException(nameof(distanceKm), error);
        var baselineHours = (decimal)distanceKm * DistanceHoursPerKm + elevationGainMeters * GainHoursPerMeter;
        var raw = ScoreScale * TechnicalClassMultiplier[trailClass] * (baselineHours + DurationExcessWeight * Math.Max(0m, typicalDurationHours - baselineHours));
        return Math.Round(trailClass == 4 ? Math.Max(raw, Class4ScoreFloor) : raw, ScoreDecimalPlaces, MidpointRounding.AwayFromZero);
    }

    public static string LabelForScore(decimal canonicalScore) => canonicalScore < MajorThreshold ? Bands[0] : canonicalScore < DifficultThreshold ? Bands[1] : Bands[2];
    public static bool IsKnownLabel(string? label) => Bands.Contains(label);
    public static bool MatchesLabel(string? label, string requestedLabel) => string.Equals(label, requestedLabel, StringComparison.Ordinal);
    public static int BucketRank(string? label) => label switch { "Minor Hike" => 0, "Major Hike" => 1, "Major Hike - Difficult" => 2, _ => 3 };
    public static string DisplayLabel(string? label) => IsKnownLabel(label) ? label! : "Unknown difficulty";
    public static string BadgeClass(string? label) => label switch { "Minor Hike" => "badge-lime", "Major Hike" => "badge-orange", "Major Hike - Difficult" => "badge-hard", _ => "text-slate-400 bg-slate-500/15" };
    public static string TrailClassLabel(int trailClass) => trailClass switch { 1 => "Walking", 2 => "Hiking", 3 => "Scrambling", 4 => "Simple Climbing", _ => "N/A" };
}
