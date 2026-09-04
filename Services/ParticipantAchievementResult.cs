namespace TrailGuard.Services
{
    // Purpose-built, immutable projection returned by ParticipantAchievementEvaluator -
    // never an EF entity, registration row, or the catalog's own AchievementDefinition
    // by reference. Carries only what a future Profile view needs to render one
    // achievement card; no private or sensitive participant data belongs here.
    //
    // Invariants (enforced by ParticipantAchievementEvaluator, the only place these are
    // constructed):
    //   CurrentValue >= 0
    //   TargetValue > 0
    //   ClampedProgress == Math.Min(CurrentValue, TargetValue)
    //   ProgressPercent is between 0 and 100
    //   IsUnlocked == CurrentValue >= TargetValue
    //   EarnedAt is non-null only when IsUnlocked is true
    public sealed record ParticipantAchievementResult
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required AchievementCategory Category { get; init; }

        public required int CurrentValue { get; init; }
        public required int TargetValue { get; init; }
        public required int ClampedProgress { get; init; }
        public required int ProgressPercent { get; init; }

        public required bool IsUnlocked { get; init; }

        // The qualifying Event's own EventDate on which this achievement's criterion
        // was first satisfied - never the administrative CompletedAt timestamp. Null
        // whenever IsUnlocked is false; this is a dynamically derived date, not a
        // stored "unlock timestamp" - see ParticipantAchievementEvaluator and
        // CLAUDE.md, Participant Progress / Achievements.
        public DateTime? EarnedAt { get; init; }

        public required int DisplayOrder { get; init; }

        // Copied straight from the catalog's AchievementDefinition.AssetKey - the
        // only legal source for `/images/achievements/achievement-{key}.webp`. Never
        // derived from Name/Code in a view or here; see
        // ParticipantAchievementCatalog.AchievementDefinition.AssetKey.
        public required string AssetKey { get; init; }

        public string IconClass { get; init; } = "fa-solid fa-award";
    }
}
