namespace TrailGuard.Models
{
    // One row from the Add Event Pickup Schedules builder. Time is a string,
    // not a TimeSpan/TimeOnly, because the browser's <input type="time">
    // sends "HH:mm" over JSON - PickupScheduleHelper parses it explicitly and
    // safely server-side rather than trusting client-side formatting.
    public sealed class PickupScheduleInputModel
    {
        public string? Location { get; set; }
        public string? Time { get; set; }
    }
}
