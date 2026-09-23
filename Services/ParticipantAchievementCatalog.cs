namespace TrailGuard.Services
{
    public enum AchievementCategory
    {
        Milestone,
        Exploration,
        Consistency,
        Variety
    }





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
        public const string VersatileHiker = "versatile_hiker";
    }

    public sealed record AchievementDefinition
    {
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required AchievementCategory Category { get; init; }
        public required int TargetValue { get; init; }
        public required int DisplayOrder { get; init; }






        public required string AssetKey { get; init; }











        public string IconClass { get; init; } = "fa-solid fa-award";
    }













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
                AssetKey = "first-adventure",
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
                AssetKey = "five-adventures",
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
                AssetKey = "double-digits",
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
                AssetKey = "new-ground",
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
                AssetKey = "trail-collector",
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
                AssetKey = "steady-steps",
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
                AssetKey = "seasoned-explorer",
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
                AssetKey = "technical-explorer",
                IconClass = "fa-solid fa-mountain-sun"
            },
            new()
            {
                Code = AchievementCodes.VersatileHiker,
                Name = "Versatile Hiker",
                Description = "Complete TrailGuard adventures across 3 different difficulty levels.",
                Category = AchievementCategory.Variety,
                TargetValue = 3,
                DisplayOrder = 9,
                AssetKey = "versatile-hiker",
                IconClass = "fa-solid fa-signal"
            }
        };
    }
}
