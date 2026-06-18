using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class EventRegistration
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        [ForeignKey("EventId")]
        public virtual Event? Event { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public string ParticipantName { get; set; } = string.Empty;

        public string? PickupPoint { get; set; }

        public string? PaymentReference { get; set; }

        public bool IsPaid { get; set; } = false;

        public DateTime RegisteredAt { get; set; } = DateTime.Now;

        public string Status { get; set; } = "Pending";

        public int? AlternativeEventId { get; set; }

        [ForeignKey("AlternativeEventId")]
        public virtual Event? AlternativeEvent { get; set; }

        public int? AssessmentId { get; set; }

        [ForeignKey("AssessmentId")]
        public virtual Assessment? Assessment { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactNumber { get; set; }
        public string? PaymentReceiptUrl { get; set; }
    }
}