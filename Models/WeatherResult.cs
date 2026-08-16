namespace TrailGuard.Models
{
    public class WeatherResult
    {
        public string ForecastDetails { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string SuggestedReminder { get; set; } = string.Empty;
        public string? UnavailableReason { get; set; }
    }
}
