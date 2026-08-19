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

        // The model's own output before the ACSM gate was applied. Differs from
        // PredictedLabel only when the gate overrode the model.
        public string ModelLabel { get; set; } = string.Empty;

        public double ConfidenceScore { get; set; }

        public bool GateApplied { get; set; }

        public string GateReason { get; set; } = string.Empty;

        public double NpsScore { get; set; }

        public string NpsBand { get; set; } = string.Empty;

        public string ModelVersion { get; set; } = "v1-synthetic";

        public DateTime PredictedAt { get; set; } = DateTime.Now;

        public virtual ICollection<ShapValue> ShapValues { get; set; } = new List<ShapValue>();
    }
}