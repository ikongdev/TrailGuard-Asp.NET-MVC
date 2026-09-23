namespace TrailGuard.Services
{













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






        public DateTime? EarnedAt { get; init; }

        public required int DisplayOrder { get; init; }





        public required string AssetKey { get; init; }

        public string IconClass { get; init; } = "fa-solid fa-award";
    }
}
