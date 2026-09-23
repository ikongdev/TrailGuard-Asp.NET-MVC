using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class FinalSuitabilityLabel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssessmentId { get; set; }

        [ForeignKey("AssessmentId")]
        public virtual Assessment? Assessment { get; set; }

        public bool Completed { get; set; }
        [Required]
        public string NonCompletionReason { get; set; } = string.Empty;



        public string? ParticipantFeedback { get; set; }
        public string? OrganizerAssessment { get; set; }
        public string? DifficultyExperience { get; set; }
        public DateTime RecordedAt { get; set; } = DateTime.Now;
    }
}
