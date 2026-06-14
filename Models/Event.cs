using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;

namespace TrailGuard.Models
{
    public class Event
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Event title is required.")]
        [Display(Name = "Event Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Event date is required.")]
        [Display(Name = "Event Date")]
        [DataType(DataType.DateTime)]
        public DateTime EventDate { get; set; }

        [Required(ErrorMessage = "Location is required.")]
        public string Location { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Difficulty Level")]
        public string Difficulty { get; set; } = string.Empty; // Easy, Moderate, Hard, Extreme

        [Required]
        [Display(Name = "Duration (hours)")]
        public double DurationHours { get; set; }

        [Display(Name = "Maximum Participants")]
        public int MaxParticipants { get; set; }

        [Display(Name = "Organized By")]
        public string? OrganizedBy { get; set; }

        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        [Display(Name = "Event Banner")]
        public string? BannerUrl { get; set; }

        [NotMapped]
        [Display(Name = "Upload Banner")]
        public IFormFile? BannerImage { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public DateTime DateUpdated { get; set; } = DateTime.Now;
    }
}