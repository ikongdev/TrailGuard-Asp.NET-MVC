namespace TrailGuard.Models
{
    public class EventEditModel
    {
        public int Id { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan EventTime { get; set; }
        public int TrailId { get; set; }
        public double EstimatedDuration { get; set; }
        public int Capacity { get; set; }

        // Admin-selection only - see EventController.EditEvent. An Organizer
        // caller's request never carries a meaningful value here; the server
        // preserves the event's existing OrganizedBy instead of trusting a
        // client-submitted id or display name.
        public string? OrganizerId { get; set; }

        public string? WeatherForecastAdvisory { get; set; }
        public string? WeatherRiskLevel { get; set; }
        public string? WeatherReminder { get; set; }

        // Structured copy of a successful refresh, present only when Edit
        // Event currently has one in its weather state - i.e. the organizer
        // deliberately changed trail/date and got a successful new forecast
        // since opening the modal. EventController.EditEvent re-validates it
        // (including matching TrailId/ForecastDate against this same
        // request's TrailId/EventDate) before it's allowed to replace the
        // event's existing stored snapshot. Null/absent means "no new
        // successful forecast this submission" - the server then preserves
        // whatever snapshot the event already had, rather than erasing it.
        public WeatherSnapshot? WeatherSnapshot { get; set; }

        public string? NotesAndReminders { get; set; }
        public string? PaymentDetails { get; set; }

        // Structured Pickup Schedules builder - mirrors EventCreateModel's
        // identical field. EventController.EditEvent validates and formats
        // this into the canonical newline-delimited string stored on
        // Event.PickupPoints, the same way Add Event does.
        public List<PickupScheduleInputModel> PickupSchedules { get; set; } = [];

        public string? Status { get; set; }
    }
}
