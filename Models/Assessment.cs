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

        // SECTION 1: Personal and Physical Profile
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public double? HeightCm { get; set; }
        public double? WeightKg { get; set; }
        public string? MedicalConditions { get; set; }

        // SECTION 2: Fitness & Endurance
        public string? ExerciseFrequency { get; set; }
        public string? ExerciseType { get; set; }
        public string? CardioEndurance { get; set; }

        // SECTION 3: Hiking Experience
        public string? MountainsClimbed { get; set; }
        public string? RecencyOfHike { get; set; }
        public string? TrailDifficultyCompleted { get; set; }

        // SECTION 4: Gear Preparedness
        public string? GearItems { get; set; }

        // Consent
        public bool ConsentGiven { get; set; }

        // Computed Fields
        public string? Result { get; set; }
        public int? TotalScore { get; set; }
        public int? FitnessScore { get; set; }
        public int? ExperienceScore { get; set; }
        public int? HealthScore { get; set; }
        public int? GearScore { get; set; }

        // ✅ BAGONG PROPERTY: Soft Delete
        public bool IsActive { get; set; } = true;

        public DateTime SubmittedAt { get; set; } = DateTime.Now;
    }
}