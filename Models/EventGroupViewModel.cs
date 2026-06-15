using TrailGuard.Models;

namespace TrailGuard.Models
{
    public class EventGroupViewModel
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public string TrailLocation { get; set; } = string.Empty;
        public List<Event> Events { get; set; } = new List<Event>();
    }
}