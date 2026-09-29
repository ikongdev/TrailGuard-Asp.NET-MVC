using TrailGuard.Models;

namespace TrailGuard.Services;

public enum SuitabilityResultSelectionState
{
    None,
    Historical,
    RecognizedV2,
    InvalidV2,
    UnsupportedV2Provenance
}

public sealed record SuitabilityResultSelection(
    SuitabilityResult? Result,
    SuitabilityResultSelectionState State)
{
    public bool IsRecognizedV2 => State == SuitabilityResultSelectionState.RecognizedV2;
    public bool IsInvalidOrUnsupported => State is SuitabilityResultSelectionState.InvalidV2
        or SuitabilityResultSelectionState.UnsupportedV2Provenance;
}

/// <summary>
/// Selects exactly one authoritative record before classifying it: newest PredictedAt first,
/// then greatest database Id for an equal timestamp. A selected record that declares v2 but
/// fails validation, or carries v2 provenance under an unknown version, is never scanned past
/// or rendered as historical.
/// </summary>
public static class SuitabilityResultSelector
{
    public static SuitabilityResultSelection Select(IEnumerable<SuitabilityResult> candidates)
    {
        var selected = candidates
            .OrderByDescending(result => result.PredictedAt)
            .ThenByDescending(result => result.Id)
            .FirstOrDefault();

        if (selected is null)
        {
            return new SuitabilityResultSelection(null, SuitabilityResultSelectionState.None);
        }

        if (selected.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion)
        {
            return new SuitabilityResultSelection(
                selected,
                TrailGuardV2Presentation.IsRecognizedV2(selected)
                    ? SuitabilityResultSelectionState.RecognizedV2
                    : SuitabilityResultSelectionState.InvalidV2);
        }

        return new SuitabilityResultSelection(
            selected,
            HasV2Provenance(selected)
                ? SuitabilityResultSelectionState.UnsupportedV2Provenance
                : SuitabilityResultSelectionState.Historical);
    }

    private static bool HasV2Provenance(SuitabilityResult result) =>
        result.FrozenModelSha256 is not null
        || result.SelectedTreeCount is not null
        || result.ScoreName is not null
        || result.BinaryPrediction is not null
        || result.BinaryThreshold is not null
        || result.UiLabelPolicyVersion is not null
        || result.GoodMatchOperator is not null
        || result.GoodMatchThreshold is not null
        || result.BorderlineMinimumOperator is not null
        || result.BorderlineMinimum is not null
        || result.BorderlineMaximumOperator is not null
        || result.BorderlineMaximum is not null
        || result.NotRecommendedOperator is not null
        || result.NotRecommendedThreshold is not null
        || result.ExerciseFrequency is not null
        || result.CardioDuration is not null
        || result.ExerciseConsistency is not null
        || result.HikingExperience is not null
        || result.HikingRecency is not null
        || result.HardestTrailCompleted is not null
        || result.GearScore is not null
        || result.DistanceKm is not null
        || result.ElevationGainM is not null
        || result.TrailClass is not null
        || result.TypicalDurationHours is not null
        || result.ShapBaseValue is not null
        || result.ShapRawMargin is not null
        || result.ShapScale is not null
        || result.ShapContributionInterpretation is not null
        || result.ShapVerificationAdditivityError is not null
        || result.ShapVerificationProbabilityReconstructionError is not null
        || result.ShapVerificationPredictionChangeAfterExplanation is not null
        || result.ShapVerificationToleranceAbsolute is not null
        || result.ShapVerificationPredictionsUnchanged is not null
        || result.ShapVerificationWithinTolerance is not null
        || result.ShapVerificationFeatureOrderMatchesFrozenSchema is not null
        || result.ShapValues.Any(value => value.FeatureOrder is not null);
}
