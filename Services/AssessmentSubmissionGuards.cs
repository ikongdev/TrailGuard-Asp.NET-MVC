using System.Diagnostics.CodeAnalysis;
using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>Pure guards for assessment submission boundaries; medical values never enter ML mapping.</summary>
public static class AssessmentSubmissionGuards
{
    public const string NoneOfTheAbove = "None of the above";
    public const string CompletedEventMessage = "This event has been completed. New assessments and retakes are closed.";
    public const string TrailChangedMessage = "The event's trail has changed. Review the updated event and retry your assessment.";

    public static bool IsEventCompleted(Event eventItem) => eventItem.Status == "Completed";

    public static readonly IReadOnlySet<string> MedicalChoices = new HashSet<string>(StringComparer.Ordinal)
    {
        "Asthma / lung-related condition",
        "Hypertension / heart-related condition",
        "Joint or knee injury",
        "Vertigo / frequent dizziness",
        "Chest pain or discomfort during activity",
        "Shortness of breath at rest or with mild exertion",
        NoneOfTheAbove
    };

    public static bool HasAuthenticatedUserId([NotNullWhen(true)] string? userId) => !string.IsNullOrWhiteSpace(userId);

    public static MedicalSelectionValidationResult ValidateMedicalSelections(IEnumerable<string?>? selections)
    {
        if (selections is null)
        {
            return MedicalSelectionValidationResult.Invalid("Please answer the medical conditions question.");
        }

        var canonical = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection))
            {
                return MedicalSelectionValidationResult.Invalid("Medical condition selections cannot be blank.");
            }

            if (!MedicalChoices.Contains(selection))
            {
                return MedicalSelectionValidationResult.Invalid("One or more medical condition selections are not recognized.");
            }

            if (!seen.Add(selection))
            {
                return MedicalSelectionValidationResult.Invalid("Medical condition selections cannot contain duplicates.");
            }

            canonical.Add(selection);
        }

        if (canonical.Count == 0)
        {
            return MedicalSelectionValidationResult.Invalid("Please answer the medical conditions question.");
        }

        if (seen.Contains(NoneOfTheAbove) && canonical.Count > 1)
        {
            return MedicalSelectionValidationResult.Invalid("None of the above cannot be selected with another medical condition.");
        }

        return MedicalSelectionValidationResult.Valid(canonical);
    }
}

/// <summary>
/// Exact Event trail values captured beside the immutable prediction request before inference.
/// Keep decimal duration here so changes lost in its model-facing double conversion are rejected too.
/// </summary>
public readonly record struct AssessmentTrailInputSnapshot(
    int TrailId, double DistanceKm, int ElevationGainMeters, int TrailClass, decimal DurationHours)
{
    public static AssessmentTrailInputSnapshot Capture(Event eventItem) => new(
        eventItem.TrailId, eventItem.TrailDistanceKmSnapshot, eventItem.TrailElevationGainMetersSnapshot,
        eventItem.TrailClassSnapshot, eventItem.TrailDurationHoursSnapshot);

    public bool Matches(Event eventItem) => TrailId == eventItem.TrailId
        && DistanceKm == eventItem.TrailDistanceKmSnapshot
        && ElevationGainMeters == eventItem.TrailElevationGainMetersSnapshot
        && TrailClass == eventItem.TrailClassSnapshot
        && DurationHours == eventItem.TrailDurationHoursSnapshot;
}

public sealed record MedicalSelectionValidationResult(bool IsValid, IReadOnlyList<string> CanonicalSelections, string? Error)
{
    public static MedicalSelectionValidationResult Valid(IReadOnlyList<string> selections) => new(true, selections, null);
    public static MedicalSelectionValidationResult Invalid(string error) => new(false, Array.Empty<string>(), error);
}
