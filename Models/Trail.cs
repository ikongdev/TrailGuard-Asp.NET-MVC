using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;

namespace TrailGuard.Models
{
    public class Trail
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Trail name is required.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Location is required.")]
        public string Location { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Total Distance (km)")]
        public double DistanceKm { get; set; } 

        [Required]
        [Display(Name = "Elevation Gain (masl)")]
        public int ElevationGainMeters { get; set; } 

        [Required]
        public string Terrain { get; set; } = string.Empty;

        // PinoyMountaineer Trail Class (1-4): 1 Walking, 2 Hiking, 3 Scrambling,
        // 4 Simple Climbing. Classes 5/6 (technical rock/aid climbing) are excluded -
        // no organized hiking event runs those for general participants.
        [Required]
        public int TrailClass { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; } 

        [NotMapped]
        public IFormFile? ThumbnailImage { get; set; }

        [NotMapped]
        public List<IFormFile>? AdditionalImages { get; set; }

        public bool IsActive { get; set; } = true;
        
        public DateTime DateAdded { get; set; } = DateTime.Now;

        public virtual ICollection<TrailPhoto>? TrailPhotos { get; set; }
    }
}