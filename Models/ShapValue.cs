using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class ShapValue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SuitabilityResultId { get; set; }

        [ForeignKey("SuitabilityResultId")]
        public virtual SuitabilityResult? SuitabilityResult { get; set; }

        [Required]
        public string FeatureName { get; set; } = string.Empty;

        [Required]
        public string Category { get; set; } = string.Empty;

        public double ImpactValue { get; set; }

        public string? RawValue { get; set; }

        public int? DisplayOrder { get; set; }

        public double? DisplaySharePct { get; set; }

        public string? DisplayFriendlyName { get; set; }
    }
}
