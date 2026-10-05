using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}

var repositoryRoot = FindRepositoryRoot();
var responseJson = File.ReadAllText(Path.Combine(repositoryRoot, "Verification", "TrailGuardV2AdapterVerification", "Fixtures", "adapter-recorded-example.json"));
var request = RecordedRequest();
var response = Deserialize(responseJson);
var timestamp = new DateTimeOffset(2026, 9, 29, 5, 30, 0, TimeSpan.Zero);

var result = TrailGuardV2SuitabilityResultFactory.Create(42, timestamp, request, response);
Check(result.AssessmentId == 42 && result.PredictedAt == timestamp.UtcDateTime, "Assessment linkage or UTC prediction timestamp changed.");
Check(result.UiLabel == "Good Match" && result.ModelScore == response.ModelScore,
    "Existing label or score columns did not preserve the v2 response.");
Check(result.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion
    && result.FrozenModelSha256 == TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256
    && result.SelectedTreeCount == 985
    && result.ScoreName == "probability_yes",
    "Model provenance changed.");
Check(result.BinaryPrediction == "Yes" && result.BinaryThreshold == 0.5
    && result.UiLabelPolicyVersion == "inherited-0.30-0.80"
    && result.GoodMatchOperator == ">=" && result.GoodMatchThreshold == 0.80
    && result.BorderlineMinimumOperator == ">=" && result.BorderlineMinimum == 0.30
    && result.BorderlineMaximumOperator == "<" && result.BorderlineMaximum == 0.80
    && result.NotRecommendedOperator == "<" && result.NotRecommendedThreshold == 0.30,
    "Binary or UI-policy provenance changed.");
Check(result.ExerciseFrequency == request.ExerciseFrequency
    && result.CardioDuration == request.CardioDuration
    && result.ExerciseConsistency == request.ExerciseConsistency
    && result.HikingExperience == request.HikingExperience
    && result.HikingRecency == request.HikingRecency
    && result.HardestTrailCompleted == request.HardestTrailCompleted
    && result.GearScore == request.GearScore
    && result.DistanceKm == request.DistanceKm
    && result.ElevationGainM == request.ElevationGainM
    && result.TrailClass == request.TrailClass
    && result.TypicalDurationHours == request.TypicalDurationHours,
    "Canonical submitted v2 inputs changed.");
Check(result.ShapValues.Count == TrailGuardV2ResponseValidator.FrozenFeatureOrder.Count,
    "All 11 SHAP values must be persisted.");
Check(result.ShapValues.Select(value => value.FeatureName).SequenceEqual(TrailGuardV2ResponseValidator.FrozenFeatureOrder)
    && result.ShapValues.Select(value => value.FeatureOrder).SequenceEqual(Enumerable.Range(0, 11).Select(index => (int?)index)),
    "Frozen SHAP feature order changed.");
Check(result.ShapValues.Zip(response.Shap!.Contributions!, (stored, source) =>
    stored.OriginalInputValue == source.OriginalInputValue!.Value.GetRawText()).All(matches => matches),
    "One or more original SHAP input values changed.");
Check(result.ShapValues.All(value => value.Category == "not-applicable-v2"
    && value.DisplayOrder is null && value.DisplaySharePct is null && value.DisplayFriendlyName is null),
    "v2 SHAP values must not be assigned invented v3 display metadata.");
Check(result.ShapBaseValue == response.Shap.BaseValue && result.ShapRawMargin == response.Shap.RawMargin
    && result.ShapScale == TrailGuardV2ResponseValidator.ExpectedExplanationScale
    && result.ShapVerificationToleranceAbsolute == TrailGuardV2ResponseValidator.NativeTreeShapTolerance
    && result.ShapVerificationWithinTolerance is true
    && result.ShapVerificationPredictionsUnchanged is true
    && result.ShapVerificationFeatureOrderMatchesFrozenSchema is true,
    "SHAP provenance or verification metadata changed.");

ExpectInvalid(() => TrailGuardV2SuitabilityResultFactory.Create(42, timestamp, request,
    Deserialize(SetPath(responseJson, ["binary_prediction"], "No"))),
    "Invalid responses must be rejected by the factory validator.");
var mismatchedRequest = RecordedRequest(gearScore: 7);
ExpectInvalid(() => TrailGuardV2SuitabilityResultFactory.Create(42, timestamp, mismatchedRequest, response),
    "Request-mismatched responses must be rejected by the factory validator.");

var newAssessment = new Assessment { EventId = 1, UserId = "new-participant" };
Check(newAssessment.Id == 0, "The graph test must begin with an unsaved Assessment.");
var graphResult = TrailGuardV2SuitabilityResultFactory.Create(newAssessment, timestamp, request, response);
Check(ReferenceEquals(graphResult.Assessment, newAssessment)
    && newAssessment.SuitabilityResults.Count == 1
    && ReferenceEquals(newAssessment.SuitabilityResults.Single(), graphResult)
    && graphResult.AssessmentId == 0,
    "New-assessment factory construction must use navigation linkage without a dummy ID.");

using (var graphContext = MetadataOnlyContext())
{
    var historical = new SuitabilityResult { Id = 900, AssessmentId = 899, UiLabel = "Historical", ModelVersion = "historical" };
    graphContext.Attach(historical);
    graphContext.Add(newAssessment);

    Check(graphContext.Entry(newAssessment).State == EntityState.Added
        && graphContext.Entry(graphResult).State == EntityState.Added,
        "Assessment and v2 result must be Added together.");
    Check(graphContext.ChangeTracker.Entries<ShapValue>().Count(entry => entry.Entity.SuitabilityResult == graphResult) == 11
        && graphContext.ChangeTracker.Entries<ShapValue>().Where(entry => entry.Entity.SuitabilityResult == graphResult)
            .All(entry => entry.State == EntityState.Added),
        "All 11 v2 SHAP values must be Added with the graph.");
    Check(ReferenceEquals(graphResult.Assessment, newAssessment)
        && newAssessment.SuitabilityResults.Contains(graphResult),
        "EF graph tracking changed the navigation relationship.");
    Check(graphContext.Entry(historical).State == EntityState.Unchanged
        && graphContext.ChangeTracker.Entries().All(entry => entry.State is EntityState.Added or EntityState.Unchanged),
        "Graph construction must not modify or delete unrelated historical entities.");
}

var invalidAssessment = new Assessment();
ExpectInvalid(() => TrailGuardV2SuitabilityResultFactory.Create(invalidAssessment, timestamp, request,
    Deserialize(SetPath(responseJson, ["binary_prediction"], "No"))),
    "Invalid responses must be rejected before a result is attached.");
Check(invalidAssessment.SuitabilityResults.Count == 0,
    "Invalid responses must leave the Assessment without a v2 result.");
var mismatchedAssessment = new Assessment();
ExpectInvalid(() => TrailGuardV2SuitabilityResultFactory.Create(mismatchedAssessment, timestamp, mismatchedRequest, response),
    "Request-mismatched responses must be rejected before a result is attached.");
Check(mismatchedAssessment.SuitabilityResults.Count == 0,
    "Request-mismatched responses must leave the Assessment without a v2 result.");
var existingV2Result = new SuitabilityResult { FrozenModelSha256 = TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256 };
var alreadyAttachedAssessment = new Assessment();
alreadyAttachedAssessment.SuitabilityResults.Add(existingV2Result);
Expect<InvalidOperationException>(() => TrailGuardV2SuitabilityResultFactory.Create(alreadyAttachedAssessment, timestamp, request, response),
    "The factory must not overwrite an already attached v2 result.");
Check(alreadyAttachedAssessment.SuitabilityResults.Single() == existingV2Result,
    "The existing v2 result was overwritten.");

using (var context = MetadataOnlyContext())
{
    var model = context.Model;
    var resultType = model.FindEntityType(typeof(SuitabilityResult));
    var shapType = model.FindEntityType(typeof(ShapValue));
    Check(resultType?.GetTableName() == "SuitabilityResults" && shapType?.GetTableName() == "ShapValues",
        "Existing table mappings changed.");
    Check(resultType?.FindProperty(nameof(SuitabilityResult.ModelScore))?.GetColumnType() == "double precision"
        && resultType.FindProperty(nameof(SuitabilityResult.DistanceKm))?.GetColumnType() == "double precision"
        && resultType.FindProperty(nameof(SuitabilityResult.ShapRawMargin))?.GetColumnType() == "double precision"
        && shapType?.FindProperty(nameof(ShapValue.FeatureOrder))?.GetColumnType() == "integer",
        "v2 precision or feature-order store types changed.");
    Check(resultType?.GetIndexes().All(index => !index.IsUnique || !index.Properties.Select(property => property.Name)
        .SequenceEqual([nameof(SuitabilityResult.AssessmentId)])) is true,
        "The existing result cardinality must not gain an AssessmentId uniqueness constraint.");
    Check(model.FindEntityType("TrailGuard.Models.TrailGuardV2PredictionSnapshot") is null
        && model.FindEntityType("TrailGuard.Models.TrailGuardV2PredictionShapContribution") is null,
        "Removed separate v2 entities remain in the EF model.");
}

var migrationPath = Directory.GetFiles(Path.Combine(repositoryRoot, "Migrations"), "*AddTrailGuardV2ResultProvenance.cs")
    .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
var migration = File.ReadAllText(migrationPath);
var up = migration[migration.IndexOf("protected override void Up", StringComparison.Ordinal)..migration.IndexOf("protected override void Down", StringComparison.Ordinal)];
Check(up.Contains("AddColumn", StringComparison.Ordinal)
    && up.Contains("RenameColumn", StringComparison.Ordinal)
    && up.Contains("SuitabilityResults", StringComparison.Ordinal)
    && up.Contains("ShapValues", StringComparison.Ordinal)
    && !up.Contains("CreateTable", StringComparison.Ordinal)
    && !up.Contains("Drop", StringComparison.Ordinal)
    && !up.Contains("AlterColumn", StringComparison.Ordinal),
    "Replacement migration Up must be additive columns only.");
Check(up.Contains("CompletionProbability", StringComparison.Ordinal)
    && up.Contains("ModelScore", StringComparison.Ordinal)
    && up.Contains("PredictedLabel", StringComparison.Ordinal)
    && up.Contains("UiLabel", StringComparison.Ordinal)
    && up.Contains("ImpactValue", StringComparison.Ordinal)
    && up.Contains("ShapContribution", StringComparison.Ordinal)
    && up.Contains("RawValue", StringComparison.Ordinal)
    && up.Contains("OriginalInputValue", StringComparison.Ordinal)
    && !up.Contains("V2Exercise", StringComparison.Ordinal),
    "The un-applied migration must rename existing columns and introduce final, version-independent input names.");

var recognizedSelection = SuitabilityResultSelector.Select([result]);
Check(ReferenceEquals(recognizedSelection.Result, result) && recognizedSelection.IsRecognizedV2 && !recognizedSelection.IsInvalidOrUnsupported,
    "A complete v2 result must use the v2 presentation path.");
var invalidV2Selection = SuitabilityResultSelector.Select([
    new SuitabilityResult { Id = 2, ModelVersion = TrailGuardV2ResponseValidator.ExpectedModelVersion, PredictedAt = timestamp.UtcDateTime },
    new SuitabilityResult { Id = 1, ModelVersion = "v3-real-outcomes", PredictedAt = timestamp.AddMinutes(-1).UtcDateTime }
]);
Check(invalidV2Selection.Result is not null && invalidV2Selection.IsInvalidOrUnsupported,
    "An incomplete declared v2 result must not be rendered as a legacy result.");

var presentationValues = new[]
{
    new ShapValue { FeatureName = "gear_score", OriginalInputValue = "8", ShapContribution = -0.5, FeatureOrder = 6 },
    new ShapValue { FeatureName = "exercise_frequency", OriginalInputValue = "\"3–4\"", ShapContribution = 0.25, FeatureOrder = 0 },
    new ShapValue { FeatureName = "trail_class", OriginalInputValue = "3", ShapContribution = 0, FeatureOrder = 9 }
};
var factors = TrailGuardV2Presentation.BuildV2Factors(presentationValues);
Check(factors.Select(factor => factor.FeatureName).SequenceEqual(["gear_score", "exercise_frequency", "trail_class"])
    && factors[0].BarWidth == 100 && factors[1].BarWidth == 50 && factors[2].BarWidth == 0
    && factors[0].Direction == "lowered" && factors[1].Direction == "raised" && factors[2].Direction == "neutral",
    "v2 SHAP presentation must sort by absolute impact and retain all-factor scaling.");
Check(TrailGuardV2Presentation.BuildV2Factors([new ShapValue { FeatureName = "trail_class", ShapContribution = 0 }]).Single().BarWidth == 0,
    "All-zero SHAP values must not divide by zero.");
Check(TrailGuardV2Presentation.BuildV2Suggestions(presentationValues).SequenceEqual(["Review the event gear checklist before the event."])
    && TrailGuardV2Presentation.FormatDuration(4.5) == "4h 30min",
    "v2 recommendations must use only the reviewed actionable features and stored duration formatting.");

var notRecommendedWithoutScreening = new Assessment { Result = "Not Recommended", MedicalClearanceRequired = false };
var notRecommendedWithScreening = new Assessment { Result = "Not Recommended", MedicalClearanceRequired = true };
var unscreenedGoodMatch = new Assessment { Result = "Good-Match", MedicalClearanceRequired = false };
var unscreenedBorderline = new Assessment { Result = "Borderline", MedicalClearanceRequired = false };
var screenedGoodMatch = new Assessment { Result = "Good-Match", MedicalClearanceRequired = true };
var screenedBorderline = new Assessment { Result = "Borderline", MedicalClearanceRequired = true };
Check(RegistrationRulesHelper.RequiresMedicalClearance(notRecommendedWithoutScreening)
    && RegistrationRulesHelper.RequiresMedicalClearance(notRecommendedWithScreening)
    && !RegistrationRulesHelper.RequiresMedicalClearance(unscreenedGoodMatch)
    && !RegistrationRulesHelper.RequiresMedicalClearance(unscreenedBorderline)
    && RegistrationRulesHelper.RequiresPreparationPlan(notRecommendedWithoutScreening)
    && RegistrationRulesHelper.RequiresMedicalClearance(screenedGoodMatch)
    && RegistrationRulesHelper.RequiresMedicalClearance(screenedBorderline)
    && !RegistrationRulesHelper.RequiresPreparationPlan(screenedGoodMatch),
    "Medical-clearance policy must require clearance for Not Recommended results or a health-screening flag while retaining the preparation-plan rule.");
Check(RegistrationRulesHelper.MedicalClearanceReason(screenedGoodMatch)
        == "Required because your health screening indicates medical clearance is needed before registration."
      && RegistrationRulesHelper.MedicalClearanceReason(notRecommendedWithoutScreening)
        == "Required by registration policy because your assessment result is Not Recommended."
      && RegistrationRulesHelper.MedicalClearanceReason(notRecommendedWithScreening)
        == "Required because your health screening indicates medical clearance is needed and registration policy requires it for a Not Recommended assessment result.",
    "Medical-clearance reasons must distinguish health screening, assessment-result policy, and both reasons.");

var priorActiveAssessment = new Assessment { IsActive = true };
var failedRetake = new Assessment { IsActive = true };
ExpectInvalid(() => TrailGuardV2SuitabilityResultFactory.Create(failedRetake, timestamp, request,
    Deserialize(SetPath(responseJson, ["binary_prediction"], "No"))),
    "A failed new prediction must be rejected before a persistence graph is created.");
Check(priorActiveAssessment.IsActive && failedRetake.SuitabilityResults.Count == 0,
    "A failed retake leaves the prior active assessment untouched and attaches no new result.");

Console.WriteLine($"PASS: {assertions} assertions. Factory and EF metadata only; no database connection, save, controller, or Python service call.");

TrailGuardV2PredictionRequest RecordedRequest(int gearScore = 6) => new()
{
    ExerciseFrequency = "3–4",
    CardioDuration = "15–29",
    ExerciseConsistency = "3+ months",
    HikingExperience = "4–10",
    HikingRecency = "Within 3 months",
    HardestTrailCompleted = "Class 3",
    GearScore = gearScore,
    DistanceKm = 8,
    ElevationGainM = 600,
    TrailClass = 3,
    TypicalDurationHours = 5
};

TrailGuardV2PredictionResponse Deserialize(string json) => JsonSerializer.Deserialize<TrailGuardV2PredictionResponse>(json,
    new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("Fixture response could not be deserialized.");

ApplicationDbContext MetadataOnlyContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql("Host=metadata-only.invalid;Database=metadata_only;Username=metadata_only;Password=metadata_only")
    .Options);

string SetPath(string source, string[] path, object value)
{
    var root = JsonNode.Parse(source)!.AsObject();
    JsonObject current = root;
    for (var index = 0; index < path.Length - 1; index++)
    {
        current = current[path[index]]!.AsObject();
    }

    current[path[^1]] = JsonSerializer.SerializeToNode(value);
    return root.ToJsonString();
}

void ExpectInvalid(Action action, string message) => Expect<TrailGuardV2ResponseValidationException>(action, message);

void Expect<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        assertions++;
        return;
    }

    throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TrailGuard.csproj")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}
