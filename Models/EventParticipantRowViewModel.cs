namespace TrailGuard.Models
{
    // Purpose-built row for the Registered Participants card on the shared
    // Admin/Organizer Event Details page (EventController.Details / Views/Event/Details.cshtml).
    // Carries only what that card already rendered (name, photo, status) plus the two
    // properties needed for the row's optional Profile link - never the participant's
    // internal Identity Id, which this type has no property for at all.
    public class EventParticipantRowViewModel
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string Status { get; set; } = string.Empty;

        public Guid PublicProfileId { get; set; }

        // Computed server-side in EventController.Details from a bounded, bulk role-
        // integrity lookup (RoleAssignmentService.GetRoleIntegrityStatusesAsync) plus
        // the viewer's own role and (for an Organizer viewer) ProfileAccessPolicy -
        // never re-derived in the view, and never by calling
        // ProfileAccessService.ResolveAsync once per row.
        public bool CanViewProfile { get; set; }
    }
}
