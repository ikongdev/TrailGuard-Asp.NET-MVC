using System.Diagnostics.CodeAnalysis;

namespace TrailGuard.Services;

/// <summary>Pure guards for assessment submission boundaries; medical values never enter ML mapping.</summary>
public static class AssessmentSubmissionGuards
{
    public const string NoneOfTheAbove = "None of the above";

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

public sealed record MedicalSelectionValidationResult(bool IsValid, IReadOnlyList<string> CanonicalSelections, string? Error)
{
    public static MedicalSelectionValidationResult Valid(IReadOnlyList<string> selections) => new(true, selections, null);
    public static MedicalSelectionValidationResult Invalid(string error) => new(false, Array.Empty<string>(), error);
}
