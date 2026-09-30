using TrailGuard.Models;

namespace TrailGuard.Services;

public static class PickupPointCatalogHelper
{
    public const int MaxNameLength = 200;

    public static bool TryNormalize(string? rawName, out string name, out string normalizedName, out string? error)
    {
        name = (rawName ?? string.Empty).Trim();
        normalizedName = string.Empty;
        error = null;

        if (name.Length == 0)
        {
            error = "A pickup point name is required.";
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            error = $"Pickup point names cannot exceed {MaxNameLength} characters.";
            return false;
        }

        if (name.Contains('\r') || name.Contains('\n') || name.Contains('—'))
        {
            error = "Pickup point names cannot contain line breaks or an em dash.";
            return false;
        }

        normalizedName = name.ToUpperInvariant();
        return true;
    }

    // Event schedules predate the catalog. Their stored locations retain the
    // schedule-format rules, but must not inherit the catalog's newer length
    // limit when an organizer is making an unrelated edit to an existing row.
    public static bool TryNormalizeScheduleLocation(string? rawLocation, out string location, out string normalizedName)
    {
        location = (rawLocation ?? string.Empty).Trim();
        normalizedName = string.Empty;

        if (location.Length == 0 || location.Contains('\r') || location.Contains('\n') || location.Contains('—'))
        {
            return false;
        }

        normalizedName = location.ToUpperInvariant();
        return true;
    }

    public static string? ValidateScheduleLocations(
        IEnumerable<PickupScheduleInputModel>? schedules,
        ISet<string> currentNormalizedNames,
        IEnumerable<PickupScheduleHelper.EditablePickupSchedule>? originalSchedules = null)
    {
        var submitted = schedules?.ToList() ?? [];
        var normalizedSubmitted = new List<(string Location, string Normalized)>();
        foreach (var schedule in submitted)
        {
            if (!TryNormalizeScheduleLocation(schedule.Location, out var location, out var normalized))
                return "A submitted pickup location is invalid.";
            normalizedSubmitted.Add((location, normalized));
        }

        var legacyAllowance = new Dictionary<string, int>(StringComparer.Ordinal);
        if (originalSchedules != null)
        {
            foreach (var original in originalSchedules)
            {
                if (!TryNormalizeScheduleLocation(original.Location, out _, out var normalized)) continue;
                legacyAllowance[normalized] = legacyAllowance.TryGetValue(normalized, out var count) ? count + 1 : 1;
            }
        }

        foreach (var submittedLocation in normalizedSubmitted)
        {
            if (currentNormalizedNames.Contains(submittedLocation.Normalized)) continue;
            if (legacyAllowance.TryGetValue(submittedLocation.Normalized, out var remaining) && remaining > 0)
            {
                legacyAllowance[submittedLocation.Normalized] = remaining - 1;
                continue;
            }

            return $"\"{submittedLocation.Location}\" is no longer an available pickup point. Choose a current catalog entry and try again.";
        }

        return null;
    }
}
