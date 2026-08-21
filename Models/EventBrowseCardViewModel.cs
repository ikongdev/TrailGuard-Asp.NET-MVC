namespace TrailGuard.Models
{
    public class EventBrowseCardViewModel
    {
        public Event Event { get; set; } = null!;

        // The participant's resolved status for this event, or null if they have
        // never registered. See RegistrationButtonHelper for how this drives the
        // Register button.
        public string? RegistrationStatus { get; set; }
    }
}
