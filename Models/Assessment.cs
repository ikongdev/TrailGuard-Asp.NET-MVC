using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class Assessment
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


        public int? Age { get; set; }
        public double? HeightCm { get; set; }
        public double? WeightKg { get; set; }
        public string? MedicalConditions { get; set; }

        public bool MedicalClearanceRequired { get; set; } = false;


        public string? ExerciseFrequency { get; set; }
        public string? ExerciseType { get; set; }
        public string? CardioEndurance { get; set; }
        public string? ExerciseConsistency { get; set; }


        public string? MountainsClimbed { get; set; }
        public string? RecencyOfHike { get; set; }
        public string? TrailDifficultyCompleted { get; set; }


        public string? GearItems { get; set; }


        public bool ConsentGiven { get; set; }


        public string? Result { get; set; }


        public bool IsActive { get; set; } = true;

        public DateTime SubmittedAt { get; set; } = DateTime.Now;
    }
}
