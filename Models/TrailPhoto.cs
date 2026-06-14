using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class TrailPhoto
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TrailId { get; set; }

        [Required]
        public string ImageUrl { get; set; } = string.Empty;

        public string? Caption { get; set; }

        public int DisplayOrder { get; set; } = 0;

        public DateTime DateAdded { get; set; } = DateTime.Now;

        [ForeignKey("TrailId")]
        public virtual Trail? Trail { get; set; }
    }
}