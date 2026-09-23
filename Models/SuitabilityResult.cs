using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class SuitabilityResult
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AssessmentId { get; set; }

        [ForeignKey("AssessmentId")]
        public virtual Assessment? Assessment { get; set; }

        [Required]
        public string PredictedLabel { get; set; } = string.Empty;



        public double CompletionProbability { get; set; }

        public string ModelVersion { get; set; } = "v3-real-outcomes";

        public DateTime PredictedAt { get; set; } = DateTime.Now;

        public virtual ICollection<ShapValue> ShapValues { get; set; } = new List<ShapValue>();
    }
}
