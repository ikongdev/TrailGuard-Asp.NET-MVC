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
        public string Difficulty { get; set; } = string.Empty;













        [MaxLength(200)]
        [Display(Name = "Trail Name (Snapshot)")]
        public string TrailNameSnapshot { get; set; } = string.Empty;

        [Display(Name = "Trail Distance km (Snapshot)")]
        public double TrailDistanceKmSnapshot { get; set; }

        [Display(Name = "Trail Duration Hours (Snapshot)")]
        public decimal TrailDurationHoursSnapshot { get; set; }

        [Display(Name = "Trail Elevation Gain Meters (Snapshot)")]
        public int TrailElevationGainMetersSnapshot { get; set; }

        [MaxLength(500)]
        [Display(Name = "Trail Terrain (Snapshot)")]
        public string TrailTerrainSnapshot { get; set; } = string.Empty;

        [Display(Name = "Trail Class (Snapshot)")]
        public int TrailClassSnapshot { get; set; }







        [Display(Name = "Trail Adjusted Rating (Snapshot)")]
        public double TrailAdjustedRatingSnapshot { get; set; }

        [MaxLength(300)]
        [Display(Name = "Trail Thumbnail URL (Snapshot)")]
        public string? TrailThumbnailUrlSnapshot { get; set; }

        [Required(ErrorMessage = "Estimated duration is required.")]
        [Display(Name = "Estimated Duration (hours)")]
        public double EstimatedDuration { get; set; }

        [Display(Name = "Capacity")]
        public int Capacity { get; set; }

        [Display(Name = "Organized By")]
        public string? OrganizedBy { get; set; }















        [Display(Name = "Organizer")]
        public string? OrganizerId { get; set; }

        [Display(Name = "Status")]
        public string Status { get; set; } = "Upcoming";

        [Display(Name = "Weather Forecast Advisory")]
        public string? WeatherForecastAdvisory { get; set; }

        [Display(Name = "Weather Risk Level")]
        public string? WeatherRiskLevel { get; set; }

        [Display(Name = "Weather Reminder")]
        public string? WeatherReminder { get; set; }







        [Display(Name = "Weather Snapshot")]
        public string? WeatherSnapshotJson { get; set; }

        [Display(Name = "Notes & Reminders")]
        public string? NotesAndReminders { get; set; }

        [Display(Name = "Payment Details")]
        public string? PaymentDetails { get; set; }

        [Display(Name = "Pickup Points")]
        public string? PickupPoints { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public DateTime DateUpdated { get; set; } = DateTime.Now;

        public DateTime? CompletedAt { get; set; }
        public string? CompletedBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }

        [NotMapped]
        public string FormattedEventTime
        {
            get
            {
                try
                {



                    return DateTime.Today.Add(EventTime).ToString("h:mm tt");
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
