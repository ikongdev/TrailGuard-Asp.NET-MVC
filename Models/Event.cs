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

        // Location and Difficulty are the two original Trail Trail-derived
        // snapshot fields on Event, predating the full snapshot below - see
        // CLAUDE.md, "Event Trail Snapshot". Both are captured once (Add Event,
        // or a deliberate Trail change on Edit Event) via
        // Services/EventTrailSnapshotHelper.CaptureSnapshot and never
        // recalculated from the live Trail afterward - not even when the Trail
        // itself is later edited (see TrailController.EditTrail, which no
        // longer cascades a recompute onto linked Events).
        [Display(Name = "Location")]
        public string Location { get; set; } = string.Empty;

        [Display(Name = "Difficulty")]
        public string Difficulty { get; set; } = string.Empty; // Auto-computed, hindi ini-input

        // Trail Snapshot - immutable copy of the selected Trail's
        // display/calculation-relevant fields, captured once by
        // EventTrailSnapshotHelper.CaptureSnapshot at the same moments as
        // Location/Difficulty above (Add Event, or a deliberate TrailId change
        // on Edit Event). TrailId/Trail above remain the stable relationship
        // for identity, grouping, analytics, and referential integrity - these
        // fields are what every Event-history display, difficulty sort, and
        // participant progress/Achievement calculation must read instead of
        // the live Trail navigation, so that editing the source Trail later
        // never rewrites what an already-created Event shows or has already
        // counted toward a participant's history. See CLAUDE.md, "Event Trail
        // Snapshot".
        [MaxLength(200)]
        [Display(Name = "Trail Name (Snapshot)")]
        public string TrailNameSnapshot { get; set; } = string.Empty;

        [Display(Name = "Trail Distance km (Snapshot)")]
        public double TrailDistanceKmSnapshot { get; set; }

        [Display(Name = "Trail Elevation Gain Meters (Snapshot)")]
        public int TrailElevationGainMetersSnapshot { get; set; }

        [MaxLength(500)]
        [Display(Name = "Trail Terrain (Snapshot)")]
        public string TrailTerrainSnapshot { get; set; } = string.Empty;

        [Display(Name = "Trail Class (Snapshot)")]
        public int TrailClassSnapshot { get; set; }

        // The exact adjusted (terrain-multiplied) NPS rating used to compute
        // Difficulty above at capture time - see
        // Services/DifficultyCalculator.ComputeAdjustedRating. Difficulty
        // sorting must use this stored value, never a live recalculation from
        // the current Trail (see EventController.Index /
        // ParticipantController.Events).
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

        // Stable Organizer ownership key - the actual authorization identity
        // for Organizer-only Event actions (see EventController/OrganizerController).
        // OrganizedBy above is a separate, unrelated display-name snapshot
        // already rendered throughout the UI; a display name is never a safe
        // ownership check (it can change, collide across accounts, or be
        // formatted differently), so it must never be used to authorize
        // anything. Null means "unresolved ownership" (every pre-existing
        // Event, until an Admin explicitly assigns one through Edit Event) -
        // never treated as owned by whichever Organizer happens to ask.
        // Deliberately a plain scalar FK with no navigation property here -
        // see the Fluent API configuration in ApplicationDbContext, which is
        // what actually wires this to the Identity users table without ever
        // giving an Event a path to pull in an ApplicationUser (and its
        // PasswordHash/SecurityStamp/etc.) through the change tracker.
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

        // Structured copy of a successful forecast (see Models/WeatherSnapshot.cs),
        // serialized as JSON text - lets Edit Event rebuild the full modern
        // weather card without re-fetching or parsing WeatherForecastAdvisory.
        // Null for legacy events and for any event whose weather was never
        // successfully looked up. Read/written only through
        // Services/WeatherSnapshotHelper, never deserialized directly.
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
                    // Non-padded hour, matching the system-wide 12-hour display convention
                    // ("5:00 AM", not "05:00 AM") - same "h:mm tt" pattern already used by
                    // OrganizerUpcomingEventData.FormattedEventTime.
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