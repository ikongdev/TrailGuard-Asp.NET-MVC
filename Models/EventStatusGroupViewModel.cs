namespace TrailGuard.Models
{



    public class EventStatusGroupViewModel
    {
        public string Status { get; set; } = string.Empty;
        public List<Event> Events { get; set; } = new List<Event>();
    }
}
