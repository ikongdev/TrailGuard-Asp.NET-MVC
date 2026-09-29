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

        public double ShapContribution { get; set; }

        // Historical rows retain their original plain text. Frozen v2 rows retain the API's
        // original JSON scalar text; consumers parse only when the stored model is recognized.
        public string? OriginalInputValue { get; set; }

        public int? DisplayOrder { get; set; }

        public double? DisplaySharePct { get; set; }

        public string? DisplayFriendlyName { get; set; }

        // Frozen-model feature order; separate from v3's optional display ordering.
        public int? FeatureOrder { get; set; }
    }
}
