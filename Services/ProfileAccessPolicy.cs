namespace TrailGuard.Services
{
    // Single source for which EventRegistration statuses establish an Organizer's
    // Profile-visibility relationship to a Participant. Shared by ProfileAccessService
    // (the actual authorization boundary for GET /Profile/{publicProfileId:guid}) and
    // EventController.Details (Profile-link display eligibility for Registered
    // Participant rows), so the two can never diverge - a link that's shown but then
    // denied (or vice versa) would either look broken or, worse, imply access that
    // isn't actually granted.
    //
    // Deliberately distinct from RegistrationStatusHelper.ActiveStatuses, which exists
    // for capacity/duplicate-registration purposes and answers a different question -
    // see CLAUDE.md, Participant Progress / Profile. Alternative Recommended is
    // included here (an Organizer who redirected a participant elsewhere still has a
    // legitimate relationship to them) while Rejected/Cancelled/Voided are excluded
    // even though none of those three appear in ActiveStatuses either.
    public static class ProfileAccessPolicy
    {
        // Private and never exposed as a collection, by design - a `public static
        // readonly string[]` still lets any caller overwrite an element in place
        // (`Foo[0] = "Rejected"` silently corrupts this policy for the whole app),
        // and even a `public` read-only-typed view over the same backing array can be
        // cast back to `string[]` and mutated the same way. AllowsOrganizerRelationship
        // below is the only way anything outside this file observes this set.
        private static readonly HashSet<string> OrganizerRelationshipStatuses = new(StringComparer.Ordinal)
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Alternative Recommended", "Accepted"
        };

        // Exact, ordinal, case-sensitive membership test - the same comparison
        // semantics every other status check in this app already uses (see
        // RegistrationStatusHelper.ActiveStatuses, OperationalRolePolicy). Null,
        // empty, whitespace, and any status outside the five above - including
        // Rejected, Cancelled, and Voided - all return false.
        //
        // Pure C# - not translatable inside an EF Core LINQ predicate (a custom
        // static method call in a query expression cannot be pushed to SQL). Callers
        // querying the database (ProfileAccessService.ResolveAsync) must project the
        // candidate statuses out of the database first and evaluate this method
        // in-memory; callers already working against an in-memory collection
        // (EventController.Details, over already-loaded EventRegistration rows) can
        // call it directly per row.
        public static bool AllowsOrganizerRelationship(string? status) =>
            !string.IsNullOrEmpty(status) && OrganizerRelationshipStatuses.Contains(status);
    }
}
