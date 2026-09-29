using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Pure construction boundary for a validated frozen-v2 graph using the existing
/// SuitabilityResult and ShapValue entities. For a new assessment, create the result
/// through the Assessment overload, add the assessment graph once, and save once in a
/// transaction. This component never attaches to a DbContext, calls HTTP, or saves data.
/// </summary>
public static class TrailGuardV2SuitabilityResultFactory
{
    private const string V2NotApplicableCategory = "not-applicable-v2";

    /// <summary>Creates an unattached result for an already-persisted assessment ID.</summary>
    public static SuitabilityResult Create(
        int assessmentId,
        DateTimeOffset predictedAt,
        TrailGuardV2PredictionRequest request,
        TrailGuardV2PredictionResponse response)
    {
        if (assessmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assessmentId));
        }

        var result = CreateResult(predictedAt, request, response);
        result.AssessmentId = assessmentId;
        return result;
    }

    /// <summary>
    /// Creates a v2 result for a new or existing assessment through the existing
    /// one-to-many relationship. The assessment may have its default database ID.
    /// </summary>
    public static SuitabilityResult Create(
        Assessment assessment,
        DateTimeOffset predictedAt,
        TrailGuardV2PredictionRequest request,
        TrailGuardV2PredictionResponse response)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.SuitabilityResults.Any(result => result.FrozenModelSha256 is not null))
        {
            throw new InvalidOperationException("The assessment already has a TrailGuard v2 prediction result.");
        }

        var result = CreateResult(predictedAt, request, response);
        result.Assessment = assessment;
        assessment.SuitabilityResults.Add(result);
        return result;
    }

    private static SuitabilityResult CreateResult(
        DateTimeOffset predictedAt,
        TrailGuardV2PredictionRequest request,
        TrailGuardV2PredictionResponse response)
    {
        if (predictedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Prediction timestamps must be UTC.", nameof(predictedAt));
        }

        TrailGuardV2ResponseValidator.Validate(response, request);
        var policy = response.UiLabelPolicy!;
        var explanation = response.Shap!;
        var verification = explanation.Verification!;

        return new SuitabilityResult
        {
            UiLabel = response.UiLabel!,
            ModelScore = response.ModelScore!.Value,
            ModelVersion = response.ModelVersion!,
            PredictedAt = predictedAt.UtcDateTime,

            FrozenModelSha256 = response.FrozenModelSha256!,
            SelectedTreeCount = response.SelectedTreeCount!.Value,
            ScoreName = response.ScoreName!,
            BinaryPrediction = response.BinaryPrediction!,
            BinaryThreshold = response.BinaryThreshold!.Value,
            UiLabelPolicyVersion = response.UiLabelPolicyVersion!,
            GoodMatchOperator = policy.GoodMatch!.Operator!,
            GoodMatchThreshold = policy.GoodMatch.Threshold!.Value,
            BorderlineMinimumOperator = policy.Borderline!.MinimumOperator!,
            BorderlineMinimum = policy.Borderline.Minimum!.Value,
            BorderlineMaximumOperator = policy.Borderline.MaximumOperator!,
            BorderlineMaximum = policy.Borderline.Maximum!.Value,
            NotRecommendedOperator = policy.NotRecommended!.Operator!,
            NotRecommendedThreshold = policy.NotRecommended.Threshold!.Value,

            ExerciseFrequency = request.ExerciseFrequency!,
            CardioDuration = request.CardioDuration!,
            ExerciseConsistency = request.ExerciseConsistency!,
            HikingExperience = request.HikingExperience!,
            HikingRecency = request.HikingRecency!,
            HardestTrailCompleted = request.HardestTrailCompleted!,
            GearScore = request.GearScore,
            DistanceKm = request.DistanceKm,
            ElevationGainM = request.ElevationGainM,
            TrailClass = request.TrailClass,
            TypicalDurationHours = request.TypicalDurationHours,

            ShapBaseValue = explanation.BaseValue!.Value,
            ShapRawMargin = explanation.RawMargin!.Value,
            ShapScale = explanation.Scale!,
            ShapContributionInterpretation = explanation.ContributionInterpretation!,
            ShapVerificationAdditivityError = verification.AdditivityError!.Value,
            ShapVerificationProbabilityReconstructionError = verification.ProbabilityReconstructionError!.Value,
            ShapVerificationPredictionChangeAfterExplanation = verification.PredictionChangeAfterExplanation!.Value,
            ShapVerificationToleranceAbsolute = verification.ToleranceAbsolute!.Value,
            ShapVerificationPredictionsUnchanged = verification.PredictionsUnchanged!.Value,
            ShapVerificationWithinTolerance = verification.WithinTolerance!.Value,
            ShapVerificationFeatureOrderMatchesFrozenSchema = verification.FeatureOrderMatchesFrozenSchema!.Value,

            // v2 has no v3 display taxonomy; do not fabricate actionable/context categories or display ranks.
            ShapValues = explanation.Contributions!
                .Select((contribution, index) => new ShapValue
                {
                    FeatureName = contribution.Feature!,
                    Category = V2NotApplicableCategory,
                    ShapContribution = contribution.ShapContribution!.Value,
                    OriginalInputValue = contribution.OriginalInputValue!.Value.GetRawText(),
                    FeatureOrder = index
                })
                .ToList()
        };
    }
}
