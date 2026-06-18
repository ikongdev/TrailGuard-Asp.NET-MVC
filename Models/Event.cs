using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TrailGuard.Models
{
    public class Event
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Event title is required.")]
        [Display(Name = "Event Title")]
        public string EventTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Event date is required.")]
        [Display(Name = "Event Date")]
        [DataType(DataType.Date)]
        public DateTime EventDate { get; set; }

        [Required(ErrorMessage = "Event time is required.")]
        [Display(Name = "Event Time")]
        [DataType(DataType.Time)]
        public TimeSpan EventTime { get; set; }

        [Required(ErrorMessage = "Trail is required.")]
        [Display(Name = "Select Trail")]
        public int TrailId { get; set; }

        [ForeignKey("TrailId")]
        public virtual Trail? Trail { get; set; }

        [Display(Name = "Location")]
        public string Location { get; set; } = string.Empty;

        [Display(Name = "Difficulty")]
        public string Difficulty { get; set; } = string.Empty; // Auto-computed, hindi ini-input

        [Required(ErrorMessage = "Estimated duration is required.")]
        [Display(Name = "Estimated Duration (hours)")]
        public double EstimatedDuration { get; set; }

        [Display(Name = "Capacity")]
        public int Capacity { get; set; }

        [Display(Name = "Organized By")]
        public string? OrganizedBy { get; set; }

        [Display(Name = "Status")]
        public string Status { get; set; } = "Upcoming";

        [Display(Name = "MASL (Elevation)")]
        public int MASL { get; set; }

        [Display(Name = "Weather Forecast Advisory")]
        public string? WeatherForecastAdvisory { get; set; }

        [Display(Name = "Announcements")]
        public string? Announcements { get; set; }

        [Display(Name = "Payment Details")]
        public string? PaymentDetails { get; set; }

        [Display(Name = "Pickup Points")]
        public string? PickupPoints { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public DateTime DateUpdated { get; set; } = DateTime.Now;

        [NotMapped]
        public string FormattedEventTime
        {
            get
            {
                try
                {
                    return $"{EventTime.Hours:D2}:{EventTime.Minutes:D2} {(EventTime.Hours >= 12 ? "PM" : "AM")}";
                }
                catch
                {
                    return "N/A";
                }
            }
        }

        [NotMapped]
        public int RegisteredCount { get; set; }
    }
}