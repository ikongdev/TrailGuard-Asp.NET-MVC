namespace TrailGuard.Services
{





    public sealed record RecentAdventureResult
    {
        public required string EventTitle { get; init; }
        public required string TrailName { get; init; }
        public required DateTime EventDate { get; init; }
        public string? Difficulty { get; init; }





        public required string OrganizerDisplayName { get; init; }
    }
}
