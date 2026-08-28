namespace TrailGuard.Models
{
    public class EventCreateModel
    {
        public string EventTitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan EventTime { get; set; }
        public int TrailId { get; set; }
        public double EstimatedDuration { get; set; }
        public int Capacity { get; set; }

        // Admin-selection only - see EventController.AddEvent. An Organizer
        // account never supplies this; the server resolves the organizer from
        // the authenticated user instead of trusting a client-submitted id.
        public string? OrganizerId { get; set; }
        public string? WeatherForecastAdvisory { get; set; }
        public string? WeatherRiskLevel { get; set; }
        public string? WeatherReminder { get; set; }

        // Structured copy of a successful forecast, present only when Add
        // Event currently has one in its weather state. EventController.AddEvent
        // re-validates it (including matching TrailId/ForecastDate against
        // this same request's TrailId/EventDate) before persisting it - never
        // trusted or stored as submitted. Null/absent means no successful
        // forecast exists yet, and the Event is saved with a null snapshot.
        public WeatherSnapshot? WeatherSnapshot { get; set; }

        public string? NotesAndReminders { get; set; }
        public string? PaymentDetails { get; set; }

        // Structured Pickup Schedules builder replaces the old raw PickupPoints
        // string for both Add and Edit Event - EventController validates and
        // formats this into the canonical newline-delimited string actually
        // stored on Event.PickupPoints.
        public List<PickupScheduleInputModel> PickupSchedules { get; set; } = [];
    }
}