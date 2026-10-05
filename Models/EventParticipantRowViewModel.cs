namespace TrailGuard.Models
{





    public class EventParticipantRowViewModel
    {
        public string ParticipantName { get; set; } = string.Empty;
        public string Initials { get; set; } = "?";
        public string? ProfilePictureUrl { get; set; }
        public string Status { get; set; } = string.Empty;

        public string StatusLabel => Status switch
        {
            "Accepted" => "Accepted",
            "Pending" => "Pending",
            "Awaiting Payment" => "Awaiting Payment",
            "For Payment Verification" => "Payment Verification",
            _ => Status
        };

        public string StatusClasses => Status switch
        {
            "Accepted" => "text-green-400",
            "Pending" => "text-yellow-400",
            "Awaiting Payment" => "text-amber-400",
            "For Payment Verification" => "text-blue-400",
            _ => "text-gray-400"
        };

        public string StatusIconClass => Status switch
        {
            "Accepted" => "fa-check-circle",
            "Pending" => "fa-clock",
            "Awaiting Payment" => "fa-credit-card",
            "For Payment Verification" => "fa-receipt",
            _ => "fa-circle-info"
        };

        public Guid PublicProfileId { get; set; }






        public bool CanViewProfile { get; set; }
    }
}
