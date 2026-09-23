namespace TrailGuard.Models
{















    public sealed class WeatherSnapshot
    {



        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;





        public int TrailId { get; set; }
        public DateTime ForecastDate { get; set; }

        public string? Condition { get; set; }
        public int? WeatherCode { get; set; }
        public double? TemperatureMinC { get; set; }
        public double? TemperatureMaxC { get; set; }
        public double? ExpectedRainfallMm { get; set; }
        public double? WindSpeedKmh { get; set; }
        public string? WindDescription { get; set; }
        public string? RiskLevel { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
