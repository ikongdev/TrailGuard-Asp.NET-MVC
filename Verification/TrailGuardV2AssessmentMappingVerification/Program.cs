using System.Text.Json;
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

var mapper = new TrailGuardV2AssessmentRequestMapper();
CheckFrozenSchemaAgreement();
CheckAllCategoricalValues();
CheckHikingDependencies();
CheckGearValidation();
CheckSnapshotBoundsAndTypes();
CheckRequestShapeAndSnapshotIsolation();

Console.WriteLine($"PASS: {assertions} assertions; frozen v2 assessment mapping validation checks completed.");

void CheckFrozenSchemaAgreement()
{
    var repositoryRoot = FindRepositoryRoot();
    var schemaPath = Path.Combine(repositoryRoot, "ml-services", "trailguard-v2", "frozen-bundle", "feature_schema.json");
    Check(File.Exists(schemaPath), "The copied frozen v2 feature schema is required for this verification.");

    using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
    var schemaFeatures = document.RootElement.GetProperty("feature_order")
        .EnumerateArray().Select(value => value.GetString()).ToArray();
    Check(schemaFeatures.SequenceEqual(TrailGuardV2AssessmentRequestMapper.FrozenFeatureOrder),
        "Mapper feature order differs from the copied frozen schema.");

    foreach (var category in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues)
    {
        var schemaValues = document.RootElement.GetProperty("categorical_mappings").GetProperty(category.Key)
            .EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Check(schemaValues.SetEquals(category.Value), $"Mapper values differ from frozen schema for {category.Key}.");
    }
}

void CheckAllCategoricalValues()
{
    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["exercise_frequency"])
    {
        Check(Valid(value).IsValid, $"Valid exercise frequency {value} was rejected.");
    }

    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["cardio_duration"])
    {
        Check(Valid(cardioDuration: value).IsValid, $"Valid cardio duration {value} was rejected.");
    }

    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["exercise_consistency"])
    {
        Check(Valid(exerciseConsistency: value).IsValid, $"Valid exercise consistency {value} was rejected.");
    }

    Check(Valid(hikingExperience: "0", hikingRecency: "Never", hardestTrailCompleted: "None").IsValid,
        "Valid first-time hiking values were rejected.");
    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["hiking_experience"].Where(value => value != "0"))
    {
        Check(Valid(hikingExperience: value).IsValid, $"Valid hiking experience {value} was rejected.");
    }

    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["hiking_recency"].Where(value => value != "Never"))
    {
        Check(Valid(hikingRecency: value).IsValid, $"Valid hiking recency {value} was rejected.");
    }

    foreach (var value in TrailGuardV2AssessmentRequestMapper.FrozenCategoricalValues["hardest_trail_completed"].Where(value => value != "None"))
    {
        Check(Valid(hardestTrailCompleted: value).IsValid, $"Valid hardest trail {value} was rejected.");
    }

    Check(!Valid(exerciseFrequency: "Sedentary").IsValid, "Unknown category must be rejected.");
    Check(!Valid(cardioDuration: null).IsValid, "Missing category must be rejected.");
}

void CheckHikingDependencies()
{
    var firstTimerRecency = Valid(hikingExperience: "0", hikingRecency: "Within 3 months", hardestTrailCompleted: "None");
    Check(HasParticipantError(firstTimerRecency, "hiking_recency"), "First-time hikers must be limited to Never recency.");

    var firstTimerTrail = Valid(hikingExperience: "0", hikingRecency: "Never", hardestTrailCompleted: "Class 1");
    Check(HasParticipantError(firstTimerTrail, "hardest_trail_completed"), "First-time hikers must be limited to None hardest trail.");

    var experiencedRecency = Valid(hikingExperience: "1–3", hikingRecency: "Never");
    Check(HasParticipantError(experiencedRecency, "hiking_recency"), "Experienced hikers cannot use Never recency.");

    var experiencedTrail = Valid(hikingExperience: "1–3", hardestTrailCompleted: "None");
    Check(HasParticipantError(experiencedTrail, "hardest_trail_completed"), "Experienced hikers cannot use None hardest trail.");
}

void CheckGearValidation()
{
    var none = Valid(gearItems: [TrailGuardV2AssessmentRequestMapper.NoneOfTheAboveGearItem]);
    Check(none.IsValid && none.Request!.GearScore == 0, "Sole None of the above must map to zero gear score.");

    var allGear = Valid(gearItems: TrailGuardV2AssessmentRequestMapper.RecognizedGearItems.ToArray());
    Check(allGear.IsValid && allGear.Request!.GearScore == 8, "All eight recognized gear items must map to gear score eight.");
    foreach (var item in TrailGuardV2AssessmentRequestMapper.RecognizedGearItems)
    {
        Check(Valid(gearItems: [item]).IsValid, $"Recognized gear item {item} was rejected.");
    }

    Check(HasParticipantError(Valid(gearItems: ["Enough water", "Enough water"]), "gear_items"),
        "Duplicate gear selections must be rejected.");
    Check(HasParticipantError(Valid(gearItems: ["Unknown item"]), "gear_items"),
        "Unknown gear selections must be rejected.");
    Check(HasParticipantError(Valid(gearItems: [TrailGuardV2AssessmentRequestMapper.NoneOfTheAboveGearItem, "Whistle"]), "gear_items"),
        "None of the above combined with gear must be rejected.");
    Check(HasParticipantError(Valid(gearItems: []), "gear_items"), "Unanswered gear selection must be rejected.");
}

void CheckSnapshotBoundsAndTypes()
{
    var missingEvent = mapper.Map(new TrailGuardV2AssessmentAnswers
    {
        ExerciseFrequency = "3–4",
        CardioDuration = "15–29",
        ExerciseConsistency = "3+ months",
        HikingExperience = "4–10",
        HikingRecency = "Within 3 months",
        HardestTrailCompleted = "Class 3",
        GearItems = ["Enough water"]
    }, null);
    Check(HasEventError(missingEvent, "event"), "Missing Event snapshots must be reported as event-data errors.");

    Check(Valid(eventItem: EventWith(distance: double.Epsilon)).IsValid, "Positive distance lower boundary was rejected.");
    Check(Valid(eventItem: EventWith(distance: 100)).IsValid, "Distance upper boundary was rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(distance: 0)), "trail_distance_km_snapshot"), "Zero distance must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(distance: 100.0001)), "trail_distance_km_snapshot"), "Out-of-range distance must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(distance: double.NaN)), "trail_distance_km_snapshot"), "NaN distance must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(distance: double.PositiveInfinity)), "trail_distance_km_snapshot"), "Infinite distance must be rejected.");

    Check(Valid(eventItem: EventWith(elevation: 0)).IsValid, "Elevation lower boundary was rejected.");
    Check(Valid(eventItem: EventWith(elevation: 10000)).IsValid, "Elevation upper boundary was rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(elevation: -1)), "trail_elevation_gain_meters_snapshot"), "Negative elevation must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(elevation: 10001)), "trail_elevation_gain_meters_snapshot"), "Out-of-range elevation must be rejected.");

    Check(Valid(eventItem: EventWith(trailClass: 1)).IsValid, "Trail class lower boundary was rejected.");
    Check(Valid(eventItem: EventWith(trailClass: 4)).IsValid, "Trail class upper boundary was rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(trailClass: 0)), "trail_class_snapshot"), "Out-of-range trail class must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(trailClass: 5)), "trail_class_snapshot"), "Out-of-range trail class must be rejected.");

    Check(Valid(eventItem: EventWith(duration: 0.1m)).IsValid, "Positive duration lower boundary was rejected.");
    Check(Valid(eventItem: EventWith(duration: 24m)).IsValid, "Duration upper boundary was rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(duration: 0m)), "trail_duration_hours_snapshot"), "Zero duration must be rejected.");
    Check(HasEventError(Valid(eventItem: EventWith(duration: 24.1m)), "trail_duration_hours_snapshot"), "Out-of-range duration must be rejected.");

    Check(typeof(Event).GetProperty(nameof(Event.TrailElevationGainMetersSnapshot))!.PropertyType == typeof(int),
        "Elevation snapshot must remain an integer-only Event property.");
    Check(typeof(Event).GetProperty(nameof(Event.TrailClassSnapshot))!.PropertyType == typeof(int),
        "Trail class snapshot must remain an integer-only Event property.");
    Check(typeof(TrailGuardV2PredictionRequest).GetProperty(nameof(TrailGuardV2PredictionRequest.ElevationGainM))!.PropertyType == typeof(int),
        "Mapped elevation must remain integer-only.");
    Check(typeof(TrailGuardV2PredictionRequest).GetProperty(nameof(TrailGuardV2PredictionRequest.TrailClass))!.PropertyType == typeof(int),
        "Mapped trail class must remain integer-only.");
}

void CheckRequestShapeAndSnapshotIsolation()
{
    var eventItem = EventWith();
    eventItem.EstimatedDuration = 23;
    eventItem.Trail = new Trail
    {
        DistanceKm = 99,
        ElevationGainMeters = 9999,
        TrailClass = 4,
        TypicalDurationHours = 22
    };

    var result = Valid(eventItem: eventItem);
    Check(result.IsValid && result.Request is not null, "Valid snapshot mapping was rejected.");
    var request = result.Request!;
    Check(request.DistanceKm == 8 && request.ElevationGainM == 600 && request.TrailClass == 3 && request.TypicalDurationHours == 5,
        "Mapper must use only frozen Event trail snapshots.");

    using var document = JsonDocument.Parse(JsonSerializer.Serialize(request));
    var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Check(properties.Length == 11, "Mapped request must have exactly 11 features.");
    Check(properties.SequenceEqual(TrailGuardV2AssessmentRequestMapper.FrozenFeatureOrder), "Mapped request feature names or order changed.");
    Check(!properties.Any(name => name.Contains("medical", StringComparison.OrdinalIgnoreCase)
        || name.Contains("bmi", StringComparison.OrdinalIgnoreCase)
        || name.Contains("age", StringComparison.OrdinalIgnoreCase)
        || name.Contains("height", StringComparison.OrdinalIgnoreCase)
        || name.Contains("weight", StringComparison.OrdinalIgnoreCase)
        || name.Contains("id", StringComparison.OrdinalIgnoreCase)
        || name.Contains("consent", StringComparison.OrdinalIgnoreCase)),
        "Mapped request contains non-model participant data.");
}

TrailGuardV2AssessmentMappingResult Valid(
    string? exerciseFrequency = "3–4",
    string? cardioDuration = "15–29",
    string? exerciseConsistency = "3+ months",
    string? hikingExperience = "4–10",
    string? hikingRecency = "Within 3 months",
    string? hardestTrailCompleted = "Class 3",
    IReadOnlyCollection<string>? gearItems = null,
    Event? eventItem = null) => mapper.Map(new TrailGuardV2AssessmentAnswers
{
    ExerciseFrequency = exerciseFrequency,
    CardioDuration = cardioDuration,
    ExerciseConsistency = exerciseConsistency,
    HikingExperience = hikingExperience,
    HikingRecency = hikingRecency,
    HardestTrailCompleted = hardestTrailCompleted,
    GearItems = gearItems ?? ["Enough water"]
}, eventItem ?? EventWith());

Event EventWith(double distance = 8, int elevation = 600, int trailClass = 3, decimal duration = 5m) => new()
{
    TrailDistanceKmSnapshot = distance,
    TrailElevationGainMetersSnapshot = elevation,
    TrailClassSnapshot = trailClass,
    TrailDurationHoursSnapshot = duration
};

bool HasParticipantError(TrailGuardV2AssessmentMappingResult result, string field) =>
    result.Errors.Any(error => error.Source == TrailGuardV2MappingErrorSource.ParticipantAnswer && error.Field == field);

bool HasEventError(TrailGuardV2AssessmentMappingResult result, string field) =>
    result.Errors.Any(error => error.Source == TrailGuardV2MappingErrorSource.EventSnapshot && error.Field == field);

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
