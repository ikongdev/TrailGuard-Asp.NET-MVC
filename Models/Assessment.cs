using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class Assessment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public int TrailId { get; set; }

        [ForeignKey("TrailId")]
        public virtual Trail? Trail { get; set; }

        [Required]
        public int EventId { get; set; }

        [ForeignKey("EventId")]
        public virtual Event? Event { get; set; }

        public DateTime DateTaken { get; set; } = DateTime.Now;

        // Participant Factors
        public int Age { get; set; }
        public double Height { get; set; } // cm
        public double Weight { get; set; } // kg
        public double BMI { get; set; }

        public string FitnessLevel { get; set; } = string.Empty;
        public string ExerciseFrequency { get; set; } = string.Empty;
        public string EnduranceLevel { get; set; } = string.Empty;

        public string HealthConditions { get; set; } = string.Empty;
        public string MedicalNotes { get; set; } = string.Empty;

        public string HikingExperience { get; set; } = string.Empty;
        public int PreviousClimbs { get; set; }
        public string FamiliarTerrain { get; set; } = string.Empty;

        public string GearItems { get; set; } = string.Empty;
        public double GearScore { get; set; }

        public string SafetyKnowledge { get; set; } = string.Empty;
        public string EmergencyReadiness { get; set; } = string.Empty;

        // Assessment Results
        public int SuitabilityScore { get; set; }
        public string SuitabilityResult { get; set; } = string.Empty; // Good-Match, Borderline, Not Recommended
        public string? Recommendations { get; set; }
        public string? DetailedFeedback { get; set; }
    }
}