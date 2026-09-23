namespace TrailGuard.Models
{





    public class EventParticipantRowViewModel
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string Status { get; set; } = string.Empty;

        public Guid PublicProfileId { get; set; }






        public bool CanViewProfile { get; set; }
    }
}
