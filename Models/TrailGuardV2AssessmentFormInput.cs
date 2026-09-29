using System.ComponentModel.DataAnnotations;

namespace TrailGuard.Models;

/// <summary>Bound form data for a new frozen-v2 assessment; medical and consent fields never enter the ML request.</summary>
public sealed class TrailGuardV2AssessmentFormInput
{
    public int EventId { get; set; }
    [Range(18, 60)] public int? Age { get; set; }
    [Range(120, 220)] public double? HeightCm { get; set; }
    [Range(25, 200)] public double? WeightKg { get; set; }
    public string[]? MedicalConditions { get; set; }
    public string? ExerciseFrequency { get; set; }
    public string? ExerciseType { get; set; }
    public string? CardioEndurance { get; set; }
    public string? ExerciseConsistency { get; set; }
    public string? MountainsClimbed { get; set; }
    public string? RecencyOfHike { get; set; }
    public string? TrailDifficultyCompleted { get; set; }
    public string[]? GearItems { get; set; }
    public bool ConsentGiven { get; set; }
    public bool DataPrivacyConsent { get; set; }
}
