namespace TrailGuard.Services
{
    public enum AchievementCategory
    {
        Milestone,
        Exploration,
        Consistency,
        Variety
    }

    // Stable, code-defined achievement codes - these are never database rows, so a
    // released code must never be renamed or reused for a different criterion. Every
    // reference elsewhere in the app (evaluator, future Profile UI) must go through
    // these constants rather than a hand-typed string literal.
    public static class AchievementCodes
    {
        public const string FirstAdventure = "first_adventure";
        public const string FiveAdventures = "five_adventures";
        public const string DoubleDigits = "double_digits";
        public const string NewGround = "new_ground";
        public const string TrailCollector = "trail_collector";
        public const string SteadySteps = "steady_steps";
        public const string SeasonedExplorer = "seasoned_explorer";
        public const string TechnicalExplorer = "technical_explorer";
    }

    public sealed record AchievementDefinition
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required AchievementCategory Category { get; init; }
        public required int TargetValue { get; init; }
        public required int DisplayOrder { get; init; }

        // Fixed, trusted Font Awesome class from this catalog only - never a user- or
        // database-supplied string, so a future view can render it directly.
        //
        // Deliberately no per-achievement tone/colour key: DESIGN.md's Color section
        // is explicit that several metrics of the same kind shouldn't be given
        // different colours just for variety (that's why the existing Progress &
        // Achievements progress bars are all accent, not three different hues). All
        // eight of these are "an achievement" - the same kind of thing - so a future
        // Profile view should style every card the same way and vary only by
        // locked/unlocked state, not by reading a tone key out of this catalog.
        public string IconClass { get; init; } = "fa-solid fa-award";
    }

    // Single source of truth for the v1 achievement catalog - eight fixed,
    // code-defined achievements, never database rows (see CLAUDE.md, Participant
    // Progress / Achievements). Evaluated dynamically by
    // ParticipantAchievementEvaluator against a Participant's own qualifying
    // history; nothing here is ever written, unlocked, or persisted anywhere, and
    // no achievement UI reads this catalog yet.
    //
    // Every criterion here is built only from TrailGuard's own recognized
    // completed-participation record (Completed Event + Accepted Registration) -
    // never payment, medical, assessment, feedback, or ML/SHAP data, and never a
    // specific Trail Class, Event difficulty, speed, distance, or elevation
    // threshold. See CLAUDE.md for the full list of inputs deliberately excluded.
    public static class ParticipantAchievementCatalog
    {
        public static readonly IReadOnlyList<AchievementDefinition> Definitions = new List<AchievementDefinition>
        {
            new()
            {
                Code = AchievementCodes.FirstAdventure,
                Name = "First Adventure",
                Description = "Complete your first TrailGuard adventure.",
                Category = AchievementCategory.Milestone,
                TargetValue = 1,
                DisplayOrder = 1,
                IconClass = "fa-solid fa-flag-checkered"
            },
            new()
            {
                Code = AchievementCodes.FiveAdventures,
                Name = "Five Adventures",
                Description = "Complete 5 TrailGuard adventures.",
                Category = AchievementCategory.Milestone,
                TargetValue = 5,
                DisplayOrder = 2,
                IconClass = "fa-solid fa-shoe-prints"
            },
            new()
            {
                Code = AchievementCodes.DoubleDigits,
                Name = "Double Digits",
                Description = "Complete 10 TrailGuard adventures.",
                Category = AchievementCategory.Milestone,
                TargetValue = 10,
                DisplayOrder = 3,
                IconClass = "fa-solid fa-medal"
            },
            new()
            {
                Code = AchievementCodes.NewGround,
                Name = "New Ground",
                Description = "Complete adventures on 3 different trails.",
                Category = AchievementCategory.Exploration,
                TargetValue = 3,
                DisplayOrder = 4,
                IconClass = "fa-solid fa-map-location-dot"
            },
            new()
            {
                Code = AchievementCodes.TrailCollector,
                Name = "Trail Collector",
                Description = "Complete adventures on 5 different trails.",
                Category = AchievementCategory.Exploration,
                TargetValue = 5,
                DisplayOrder = 5,
                IconClass = "fa-solid fa-layer-group"
            },
            new()
            {
                Code = AchievementCodes.SteadySteps,
                Name = "Steady Steps",
                Description = "Complete adventures across 3 different calendar months.",
                Category = AchievementCategory.Consistency,
                TargetValue = 3,
                DisplayOrder = 6,
                IconClass = "fa-solid fa-calendar-check"
            },
            new()
            {
                Code = AchievementCodes.SeasonedExplorer,
                Name = "Seasoned Explorer",
                Description = "Complete adventures across 6 different calendar months.",
                Category = AchievementCategory.Consistency,
                TargetValue = 6,
                DisplayOrder = 7,
                IconClass = "fa-solid fa-calendar-days"
            },
            new()
            {
                Code = AchievementCodes.TechnicalExplorer,
                Name = "Technical Explorer",
                Description = "Complete adventures across 3 different technical Trail Classes.",
                Category = AchievementCategory.Variety,
                TargetValue = 3,
                DisplayOrder = 8,
                IconClass = "fa-solid fa-mountain-sun"
            }
        };
    }
}
