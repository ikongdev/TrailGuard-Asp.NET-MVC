namespace TrailGuard.Services
{














    public static class ProfileAccessPolicy
    {






        private static readonly HashSet<string> OrganizerRelationshipStatuses = new(StringComparer.Ordinal)
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Alternative Recommended", "Accepted"
        };














        public static bool AllowsOrganizerRelationship(string? status) =>
            !string.IsNullOrEmpty(status) && OrganizerRelationshipStatuses.Contains(status);
    }
}
