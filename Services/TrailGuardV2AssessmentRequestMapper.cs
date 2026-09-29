using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Maps only the frozen v2 model inputs. Eligibility, consent, medical screening,
/// persistence, HTTP, and legacy assessment-answer conversions remain outside this component.
/// </summary>
public sealed class TrailGuardV2AssessmentRequestMapper
{
    public const string NoneOfTheAboveGearItem = "None of the above";

    public static IReadOnlyList<string> FrozenFeatureOrder { get; } =
    [
        "exercise_frequency",
        "cardio_duration",
        "exercise_consistency",
        "hiking_experience",
        "hiking_recency",
        "hardest_trail_completed",
        "gear_score",
        "distance_km",
        "elevation_gain_m",
        "trail_class",
        "typical_duration_hours"
    ];

    // Values and bounds are copied from frozen-bundle/feature_schema.json and constants.py.
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> FrozenCategoricalValues { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["exercise_frequency"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "No regular exercise", "1–2", "3–4", "5+"
            },
            ["cardio_duration"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "None", "<15", "15–29", "30–60", ">60"
            },
            ["exercise_consistency"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "None", "<1 month", "1–2 months", "3+ months"
            },
            ["hiking_experience"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "0", "1–3", "4–10", "11+"
            },
            ["hiking_recency"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "Never", ">12 months", ">3–12 months", "Within 3 months"
            },
            ["hardest_trail_completed"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "None", "Class 1", "Class 2", "Class 3", "Class 4"
            }
        };

    public static IReadOnlySet<string> RecognizedGearItems { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Enough water",
        "Trail food",
        "First aid kit",
        "Flashlight",
        "Whistle",
        "Raincoat",
        "Navigation tool",
        "Proper hiking shoes"
    };

    public TrailGuardV2AssessmentMappingResult Map(TrailGuardV2AssessmentAnswers? answers, Event? eventItem)
    {
        var errors = new List<TrailGuardV2MappingError>();
        if (answers is null)
        {
            AddParticipantError(errors, "answers", "Assessment answers are required.");
        }
        else
        {
            ValidateCategory(errors, "exercise_frequency", answers.ExerciseFrequency);
            ValidateCategory(errors, "cardio_duration", answers.CardioDuration);
            ValidateCategory(errors, "exercise_consistency", answers.ExerciseConsistency);
            ValidateCategory(errors, "hiking_experience", answers.HikingExperience);
            ValidateCategory(errors, "hiking_recency", answers.HikingRecency);
            ValidateCategory(errors, "hardest_trail_completed", answers.HardestTrailCompleted);
            ValidateHikingConsistency(errors, answers);
            ValidateGearItems(errors, answers.GearItems);
        }

        ValidateEventSnapshots(errors, eventItem);
        if (errors.Count != 0 || answers is null || eventItem is null)
        {
            return new TrailGuardV2AssessmentMappingResult { Errors = errors };
        }

        var gearScore = GearScore(answers.GearItems!);
        return new TrailGuardV2AssessmentMappingResult
        {
            Request = new TrailGuardV2PredictionRequest
            {
                ExerciseFrequency = answers.ExerciseFrequency,
                CardioDuration = answers.CardioDuration,
                ExerciseConsistency = answers.ExerciseConsistency,
                HikingExperience = answers.HikingExperience,
                HikingRecency = answers.HikingRecency,
                HardestTrailCompleted = answers.HardestTrailCompleted,
                GearScore = gearScore,
                DistanceKm = eventItem.TrailDistanceKmSnapshot,
                ElevationGainM = eventItem.TrailElevationGainMetersSnapshot,
                TrailClass = eventItem.TrailClassSnapshot,
                TypicalDurationHours = (double)eventItem.TrailDurationHoursSnapshot
            },
            Errors = errors
        };
    }

    private static void ValidateCategory(
        ICollection<TrailGuardV2MappingError> errors,
        string feature,
        string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            AddParticipantError(errors, feature, "A response is required.");
        }
        else if (!FrozenCategoricalValues[feature].Contains(value))
        {
            AddParticipantError(errors, feature, "The selected value is not recognized by the TrailGuard v2 model.");
        }
    }

    private static void ValidateHikingConsistency(
        ICollection<TrailGuardV2MappingError> errors,
        TrailGuardV2AssessmentAnswers answers)
    {
        if (!FrozenCategoricalValues["hiking_experience"].Contains(answers.HikingExperience ?? string.Empty))
        {
            return;
        }

        if (answers.HikingExperience == "0")
        {
            if (answers.HikingRecency != "Never")
            {
                AddParticipantError(errors, "hiking_recency", "First-time hikers must select Never.");
            }
            if (answers.HardestTrailCompleted != "None")
            {
                AddParticipantError(errors, "hardest_trail_completed", "First-time hikers must select None.");
            }
            return;
        }

        if (answers.HikingRecency == "Never")
        {
            AddParticipantError(errors, "hiking_recency", "Participants with hiking experience cannot select Never.");
        }
        if (answers.HardestTrailCompleted == "None")
        {
            AddParticipantError(errors, "hardest_trail_completed", "Participants with hiking experience cannot select None.");
        }
    }

    private static void ValidateGearItems(
        ICollection<TrailGuardV2MappingError> errors,
        IReadOnlyCollection<string>? gearItems)
    {
        if (gearItems is null || gearItems.Count == 0)
        {
            AddParticipantError(errors, "gear_items", "Select one or more gear options.");
            return;
        }

        var uniqueItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in gearItems)
        {
            if (!uniqueItems.Add(item))
            {
                AddParticipantError(errors, "gear_items", "Duplicate gear selections are not allowed.");
            }
            if (item != NoneOfTheAboveGearItem && !RecognizedGearItems.Contains(item))
            {
                AddParticipantError(errors, "gear_items", "One or more selected gear items are not recognized.");
            }
        }

        if (uniqueItems.Contains(NoneOfTheAboveGearItem) && uniqueItems.Count != 1)
        {
            AddParticipantError(errors, "gear_items", "None of the above cannot be selected with other gear items.");
        }
    }

    private static void ValidateEventSnapshots(
        ICollection<TrailGuardV2MappingError> errors,
        Event? eventItem)
    {
        if (eventItem is null)
        {
            errors.Add(new TrailGuardV2MappingError(
                "event",
                TrailGuardV2MappingErrorSource.EventSnapshot,
                "Event trail snapshots are required."));
            return;
        }

        if (!double.IsFinite(eventItem.TrailDistanceKmSnapshot)
            || eventItem.TrailDistanceKmSnapshot <= 0
            || eventItem.TrailDistanceKmSnapshot > 100)
        {
            AddEventError(errors, "trail_distance_km_snapshot", "Distance snapshot must be finite, greater than 0 km, and at most 100 km.");
        }
        if (eventItem.TrailElevationGainMetersSnapshot is < 0 or > 10000)
        {
            AddEventError(errors, "trail_elevation_gain_meters_snapshot", "Elevation snapshot must be a whole number from 0 to 10000 m.");
        }
        if (eventItem.TrailClassSnapshot is < 1 or > 4)
        {
            AddEventError(errors, "trail_class_snapshot", "Trail class snapshot must be a whole number from 1 to 4.");
        }
        if (eventItem.TrailDurationHoursSnapshot <= 0 || eventItem.TrailDurationHoursSnapshot > 24)
        {
            AddEventError(errors, "trail_duration_hours_snapshot", "Duration snapshot must be greater than 0 and at most 24 hours.");
        }
    }

    private static int GearScore(IReadOnlyCollection<string> gearItems) =>
        gearItems.Count == 1 && gearItems.Contains(NoneOfTheAboveGearItem) ? 0 : gearItems.Count;

    private static void AddParticipantError(
        ICollection<TrailGuardV2MappingError> errors,
        string field,
        string message) => errors.Add(new TrailGuardV2MappingError(
            field,
            TrailGuardV2MappingErrorSource.ParticipantAnswer,
            message));

    private static void AddEventError(
        ICollection<TrailGuardV2MappingError> errors,
        string field,
        string message) => errors.Add(new TrailGuardV2MappingError(
            field,
            TrailGuardV2MappingErrorSource.EventSnapshot,
            message));
}
