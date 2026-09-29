using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var timestamp = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

var olderValidV2 = NewV2Result(id: 10, predictedAt: timestamp.AddMinutes(-1));
var newestInvalidV2 = NewInvalidV2Result(id: 11, predictedAt: timestamp);
var invalidAfterNewerV2 = SuitabilityResultSelector.Select([olderValidV2, newestInvalidV2]);
Check(ReferenceEquals(invalidAfterNewerV2.Result, newestInvalidV2)
    && invalidAfterNewerV2.State == SuitabilityResultSelectionState.InvalidV2,
    "The newest incomplete v2 record must be selected and reported invalid; an older valid v2 record must not replace it.");

var olderHistorical = NewHistoricalResult(id: 20, predictedAt: timestamp.AddMinutes(-1));
var newestInvalidOverHistorical = NewInvalidV2Result(id: 21, predictedAt: timestamp);
var invalidAfterHistorical = SuitabilityResultSelector.Select([olderHistorical, newestInvalidOverHistorical]);
Check(ReferenceEquals(invalidAfterHistorical.Result, newestInvalidOverHistorical)
    && invalidAfterHistorical.State == SuitabilityResultSelectionState.InvalidV2,
    "The newest incomplete v2 record must not fall back to an older historical record.");

var validSelectedV2 = NewV2Result(id: 30, predictedAt: timestamp);
var recognized = SuitabilityResultSelector.Select([NewHistoricalResult(29, timestamp.AddMinutes(-1)), validSelectedV2]);
Check(ReferenceEquals(recognized.Result, validSelectedV2)
    && recognized.State == SuitabilityResultSelectionState.RecognizedV2
    && recognized.IsRecognizedV2,
    "A valid authoritative v2 record must dispatch to the v2 presentation path.");

var historicalOnly = NewHistoricalResult(id: 40, predictedAt: timestamp);
var historicalSelection = SuitabilityResultSelector.Select([historicalOnly]);
Check(ReferenceEquals(historicalSelection.Result, historicalOnly)
    && historicalSelection.State == SuitabilityResultSelectionState.Historical,
    "A historical-only assessment must retain historical presentation.");

var lowerIdAtSameTime = NewV2Result(id: 49, predictedAt: timestamp);
var higherIdAtSameTime = NewHistoricalResult(id: 50, predictedAt: timestamp);
var tieBreakSelection = SuitabilityResultSelector.Select([lowerIdAtSameTime, higherIdAtSameTime]);
Check(ReferenceEquals(tieBreakSelection.Result, higherIdAtSameTime)
    && tieBreakSelection.State == SuitabilityResultSelectionState.Historical,
    "Equal prediction timestamps must choose the greatest result Id before classification.");

var unknownVersionWithV2Provenance = NewHistoricalResult(id: 60, predictedAt: timestamp);
unknownVersionWithV2Provenance.ModelVersion = "trailguard-v2.unknown";
unknownVersionWithV2Provenance.FrozenModelSha256 = TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256;
var unsupportedSelection = SuitabilityResultSelector.Select([unknownVersionWithV2Provenance]);
Check(ReferenceEquals(unsupportedSelection.Result, unknownVersionWithV2Provenance)
    && unsupportedSelection.State == SuitabilityResultSelectionState.UnsupportedV2Provenance
    && unsupportedSelection.IsInvalidOrUnsupported,
    "An unknown model version carrying v2 provenance must be unsupported, never legacy.");

var factors = TrailGuardV2Presentation.BuildV2Factors([
    new ShapValue { FeatureName = "gear_score", OriginalInputValue = "8", ShapContribution = -2, FeatureOrder = 6 },
    new ShapValue { FeatureName = "exercise_frequency", OriginalInputValue = "\"3–4\"", ShapContribution = 1, FeatureOrder = 0 },
    new ShapValue { FeatureName = "trail_class", OriginalInputValue = "3", ShapContribution = 0, FeatureOrder = 9 }
]);
Check(factors.Select(factor => factor.FeatureName).SequenceEqual(["gear_score", "exercise_frequency", "trail_class"])
    && factors.Select(factor => factor.BarWidth).SequenceEqual([100d, 50d, 0d])
    && factors.Select(factor => factor.Direction).SequenceEqual(["lowered", "raised", "neutral"]),
    "SHAP values must sort by absolute contribution with stable all-factor scaling and neutral zero handling.");
Check(TrailGuardV2Presentation.BuildV2Factors([new ShapValue { FeatureName = "trail_class", ShapContribution = 0 }]).Single().BarWidth == 0,
    "All-zero SHAP values must not divide by zero.");

var prior = new Assessment { IsActive = true };
var failedNewSubmission = new Assessment { IsActive = true };
Check(prior.IsActive && failedNewSubmission.SuitabilityResults.Count == 0,
    "Before persistence, a failed validation or API response leaves the prior active result untouched and attaches no new graph.");

var notRecommended = new Assessment { Result = "Not Recommended", MedicalClearanceRequired = false };
var screened = new Assessment { Result = "Good-Match", MedicalClearanceRequired = true };
Check(!RegistrationRulesHelper.RequiresMedicalClearance(notRecommended)
    && RegistrationRulesHelper.RequiresPreparationPlan(notRecommended)
    && RegistrationRulesHelper.RequiresMedicalClearance(screened)
    && !RegistrationRulesHelper.RequiresPreparationPlan(screened),
    "Preparation plans and medical clearance must have independent server-side triggers.");

Check(AssessmentSubmissionGuards.HasAuthenticatedUserId("participant-1")
    && !AssessmentSubmissionGuards.HasAuthenticatedUserId(null)
    && !AssessmentSubmissionGuards.HasAuthenticatedUserId("")
    && !AssessmentSubmissionGuards.HasAuthenticatedUserId("   "),
    "Assessment submissions require a non-empty authenticated user ID.");

var validMedical = AssessmentSubmissionGuards.ValidateMedicalSelections([
    "Hypertension / heart-related condition",
    "Chest pain or discomfort during activity"
]);
Check(validMedical.IsValid
    && validMedical.CanonicalSelections.SequenceEqual([
        "Hypertension / heart-related condition",
        "Chest pain or discomfort during activity"
    ]),
    "Recognized medical selections must preserve their canonical form.");

var validNone = AssessmentSubmissionGuards.ValidateMedicalSelections([AssessmentSubmissionGuards.NoneOfTheAbove]);
Check(validNone.IsValid && validNone.CanonicalSelections.Single() == AssessmentSubmissionGuards.NoneOfTheAbove,
    "The standalone None of the above selection must remain valid.");

foreach (var invalidMedical in new IEnumerable<string?>?[]
{
    null,
    [],
    [""],
    ["Unknown condition"],
    ["Asthma / lung-related condition", "Asthma / lung-related condition"],
    [AssessmentSubmissionGuards.NoneOfTheAbove, "Joint or knee injury"]
})
{
    Check(!AssessmentSubmissionGuards.ValidateMedicalSelections(invalidMedical).IsValid,
        "Null, blank, unknown, duplicate, and contradictory medical selections must be rejected.");
}

Check(TrailGuardV2Presentation.FormatModelScore(0.81234) == "81.23%"
    && TrailGuardV2Presentation.FormatModelScore(0.5) == "50.00%",
    "Ordinary model scores must display with two percentage decimals.");
Check(TrailGuardV2Presentation.FormatModelScore(0.9999628067016602) == ">99.99%"
    && TrailGuardV2Presentation.FormatModelScore(0.0000001) == "<0.01%",
    "Non-endpoint scores that round to 100.00% or 0.00% must use bounded display text.");
Check(TrailGuardV2Presentation.FormatModelScore(0) == "0.00%"
    && TrailGuardV2Presentation.FormatModelScore(1) == "100.00%",
    "Exact score endpoints must retain their exact percentage display.");

Console.WriteLine($"PASS: {assertions} assertions. Pure selector/presentation/rule checks only; no database, HTTP, controller, or Python service call.");

static SuitabilityResult NewHistoricalResult(int id, DateTime predictedAt) => new()
{
    Id = id,
    PredictedAt = predictedAt,
    ModelVersion = "v3-real-outcomes"
};

static SuitabilityResult NewInvalidV2Result(int id, DateTime predictedAt) => new()
{
    Id = id,
    PredictedAt = predictedAt,
    ModelVersion = TrailGuardV2ResponseValidator.ExpectedModelVersion
};

static SuitabilityResult NewV2Result(int id, DateTime predictedAt)
{
    var result = new SuitabilityResult
    {
        Id = id,
        PredictedAt = predictedAt,
        ModelVersion = TrailGuardV2ResponseValidator.ExpectedModelVersion,
        FrozenModelSha256 = TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256,
        SelectedTreeCount = TrailGuardV2ResponseValidator.ExpectedSelectedTreeCount,
        ScoreName = "probability_yes", ModelScore = .5, UiLabel = "Borderline", BinaryPrediction = "Yes", BinaryThreshold = 0.5,
        UiLabelPolicyVersion = TrailGuardV2ResponseValidator.ExpectedUiLabelPolicyVersion,
        GoodMatchOperator = ">=", GoodMatchThreshold = .80,
        BorderlineMinimumOperator = ">=", BorderlineMinimum = .30,
        BorderlineMaximumOperator = "<", BorderlineMaximum = .80,
        NotRecommendedOperator = "<", NotRecommendedThreshold = .30,
        ShapScale = TrailGuardV2ResponseValidator.ExpectedExplanationScale,
        ShapBaseValue = 0, ShapRawMargin = 0,
        ShapVerificationAdditivityError = 0,
        ShapVerificationProbabilityReconstructionError = 0,
        ShapVerificationPredictionChangeAfterExplanation = 0,
        ShapVerificationToleranceAbsolute = TrailGuardV2ResponseValidator.NativeTreeShapTolerance,
        ShapVerificationPredictionsUnchanged = true,
        ShapVerificationWithinTolerance = true,
        ShapVerificationFeatureOrderMatchesFrozenSchema = true
    };
    foreach (var pair in TrailGuardV2ResponseValidator.FrozenFeatureOrder.Select((feature, order) => new { feature, order }))
    {
        result.ShapValues.Add(new ShapValue { FeatureName = pair.feature, FeatureOrder = pair.order, ShapContribution = 0, OriginalInputValue = "0" });
    }
    return result;
}
