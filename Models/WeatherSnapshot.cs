namespace TrailGuard.Models
{
    // A persisted, structured copy of a successful GetWeatherForecast result -
    // what lets the Edit Event modal rebuild the full modern weather card
    // (condition, temperature, rainfall, wind, risk, updated time) without
    // re-fetching, and without ever parsing the free-text
    // Event.WeatherForecastAdvisory. Field names deliberately mirror
    // GetWeatherForecast's own JSON response (see EventController) so the
    // same client-side renderer can consume either one directly.
    //
    // Also serves as the nested request-binding type for Add/Edit Event's
    // WeatherSnapshot payload field - the values a client legitimately has
    // (from its own in-memory copy of a real forecast response) are exactly
    // these same properties, so no separate DTO is needed. The server never
    // trusts this on its own: TrailId/ForecastDate are checked against the
    // submitted Event before a request-bound instance is ever persisted -
    // see Services/WeatherSnapshotHelper.TryValidateForSubmission.
    public sealed class WeatherSnapshot
    {
        // Bump only if the stored shape changes in a way older stored JSON
        // can't be read as - WeatherSnapshotHelper rejects any other value
        // outright rather than guessing at its shape.
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        // The trail and date this forecast was actually fetched for -
        // compared against the Event's current TrailId/EventDate to decide
        // whether the snapshot is still current or belongs to a previous
        // trail/date context.
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
