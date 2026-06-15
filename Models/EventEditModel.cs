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
        public string? OrganizedBy { get; set; }
        public string? WeatherForecastAdvisory { get; set; }
        public string? Announcements { get; set; }
        public string? PaymentDetails { get; set; }
        public string? PickupPoints { get; set; }
        public string? Status { get; set; }
    }
}