namespace TrailGuard.Models
{
    // Minimal safe read model for the Joined Participants list on Participant
    // Event Details (ParticipantController.Details / Views/Participant/Details.cshtml).
    // Carries only what that list renders for another participant - name and
    // avatar - for an Accepted-only registration. Unlike the Organizer-facing
    // EventParticipantRowViewModel, there is no Profile link from this page, so
    // this type has no PublicProfileId/CanViewProfile, and (unlike that type)
    // no Status either, since every row here is already known to be Accepted.
    // Never the participant's internal Identity Id, registration Id, email,
    // phone, emergency contact, pickup schedule, payment reference/receipt
    // URL, medical-clearance URL, preparation plan, assessment, or decision
    // reason - none of that is queried into this projection at all.
    public class ParticipantEventJoinedRowViewModel
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
    }
}
