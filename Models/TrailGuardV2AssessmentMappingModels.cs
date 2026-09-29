namespace TrailGuard.Models;

/// <summary>
/// Raw participant answers for the frozen TrailGuard v2 model only. This is not
/// an Assessment persistence model and intentionally contains no medical or identity data.
/// </summary>
public sealed class TrailGuardV2AssessmentAnswers
{
    public string? ExerciseFrequency { get; init; }
    public string? CardioDuration { get; init; }
    public string? ExerciseConsistency { get; init; }
    public string? HikingExperience { get; init; }
    public string? HikingRecency { get; init; }
    public string? HardestTrailCompleted { get; init; }
    public IReadOnlyCollection<string>? GearItems { get; init; }
}

public enum TrailGuardV2MappingErrorSource
{
    ParticipantAnswer,
    EventSnapshot
}

public sealed record TrailGuardV2MappingError(
    string Field,
    TrailGuardV2MappingErrorSource Source,
    string Message);

public sealed class TrailGuardV2AssessmentMappingResult
{
    public TrailGuardV2PredictionRequest? Request { get; init; }
    public IReadOnlyList<TrailGuardV2MappingError> Errors { get; init; } = [];
    public bool IsValid => Request is not null && Errors.Count == 0;
}
