using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class FinalSuitabilityLabel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RegistrationId { get; set; }

        [ForeignKey("RegistrationId")]
        public virtual EventRegistration? Registration { get; set; }

        [Required]
        public int EventId { get; set; }

        [ForeignKey("EventId")]
        public virtual Event? Event { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public int AssessmentId { get; set; }

        [ForeignKey("AssessmentId")]
        public virtual Assessment? Assessment { get; set; }

        [Required]
        public string PreHikeLabel { get; set; } = string.Empty;

        public string? ParticipantFeedback { get; set; }
        public string? OrganizerAssessment { get; set; }

        [Required]
        public string FinalLabel { get; set; } = string.Empty;

        public DateTime ResolvedAt { get; set; } = DateTime.Now;
    }
}
