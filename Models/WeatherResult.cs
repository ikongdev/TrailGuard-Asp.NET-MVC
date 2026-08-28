namespace TrailGuard.Models
{
    public class WeatherResult
    {
        public string ForecastDetails { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string SuggestedReminder { get; set; } = string.Empty;
        public string? UnavailableReason { get; set; }

        // Structured fields backing the Add Event forecast result card.
        // Null on an unavailable result (see WeatherService.UnavailableResult) -
        // never a fabricated zero, since a missing measurement and a real
        // reading of zero mean different things to a reader of the card.
        public string? Condition { get; set; }
        public int? WeatherCode { get; set; }
        public double? TemperatureMinC { get; set; }
        public double? TemperatureMaxC { get; set; }
        public double? ExpectedRainfallMm { get; set; }
        public double? WindSpeedKmh { get; set; }
        public string? WindDescription { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
