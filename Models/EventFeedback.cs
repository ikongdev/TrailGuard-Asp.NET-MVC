using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class EventFeedback
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
        [Range(1, 5)]
        public int Rating { get; set; }

        public string? DifficultyExperience { get; set; }

        public string? TrailCondition { get; set; }
        public string? TrailSignage { get; set; }
        public string? WaterSourceAvailability { get; set; }
        public string? HazardsEncountered { get; set; }

        public string? PreEventCommunication { get; set; }
        public string? SafetyManagement { get; set; }
        public string? GroupManagement { get; set; }

        public string? Comment { get; set; } // ✅ Pinalitan from Comments to Comment

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}