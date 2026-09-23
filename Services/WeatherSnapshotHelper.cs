using System.Text.Json;
using Microsoft.Extensions.Logging;
using TrailGuard.Models;

namespace TrailGuard.Services
{





    public static class WeatherSnapshotHelper
    {
        private const int MaxTextLength = 200;
        private const int MinWeatherCode = 0;
        private const int MaxWeatherCode = 99;
        private const double MinReasonableTempC = -60;
        private const double MaxReasonableTempC = 60;
        private const double MinReasonableRainfallMm = 0;
        private const double MaxReasonableRainfallMm = 2000;
        private const double MinReasonableWindKmh = 0;
        private const double MaxReasonableWindKmh = 400;





        public static string Serialize(WeatherSnapshot snapshot)
        {
            return JsonSerializer.Serialize(snapshot);
        }








        public static WeatherSnapshot? TryDeserialize(string? json, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            WeatherSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<WeatherSnapshot>(json);
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Discarded a malformed stored weather snapshot.");
                return null;
            }

            if (snapshot == null) return null;

            if (!IsValid(snapshot, out var reason))
            {
                logger?.LogWarning("Discarded a stored weather snapshot that failed validation: {Reason}", reason);
                return null;
            }

            return snapshot;
        }







        public static bool TryValidateForSubmission(WeatherSnapshot? snapshot, int expectedTrailId, DateTime expectedForecastDate, out string? reason)
        {
            reason = null;
            if (snapshot == null) return false;

            if (!IsValid(snapshot, out reason)) return false;

            if (snapshot.TrailId != expectedTrailId || snapshot.ForecastDate.Date != expectedForecastDate.Date)
            {
                reason = "Snapshot trail/date does not match the submitted event.";
                return false;
            }

            return true;
        }

        private static bool IsValid(WeatherSnapshot snapshot, out string? reason)
        {
            reason = null;

            if (snapshot.Version != WeatherSnapshot.CurrentVersion)
            {
                reason = $"Unsupported snapshot version {snapshot.Version}.";
                return false;
            }

            if (snapshot.TrailId <= 0)
            {
                reason = "Missing or invalid TrailId.";
                return false;
            }

            if (!IsBoundedText(snapshot.Condition) || !IsBoundedText(snapshot.WindDescription) || !IsBoundedText(snapshot.RiskLevel))
            {
                reason = "A text field exceeded the allowed length.";
                return false;
            }

            if (snapshot.WeatherCode.HasValue && (snapshot.WeatherCode.Value < MinWeatherCode || snapshot.WeatherCode.Value > MaxWeatherCode))
            {
                reason = "WeatherCode is outside the known range.";
                return false;
            }

            if (!IsFiniteInRange(snapshot.TemperatureMinC, MinReasonableTempC, MaxReasonableTempC) ||
                !IsFiniteInRange(snapshot.TemperatureMaxC, MinReasonableTempC, MaxReasonableTempC) ||
                !IsFiniteInRange(snapshot.ExpectedRainfallMm, MinReasonableRainfallMm, MaxReasonableRainfallMm) ||
                !IsFiniteInRange(snapshot.WindSpeedKmh, MinReasonableWindKmh, MaxReasonableWindKmh))
            {
                reason = "A numeric field was non-finite or outside its reasonable range.";
                return false;
            }

            return true;
        }









        public static string GetIconClass(int? weatherCode)
        {
            return weatherCode switch
            {
                0 => "fa-sun",
                1 => "fa-cloud-sun",
                2 or 3 => "fa-cloud",
                45 or 48 => "fa-smog",
                51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 => "fa-cloud-rain",
                80 or 81 or 82 => "fa-cloud-showers-heavy",
                71 or 73 or 75 => "fa-snowflake",
                95 or 96 or 99 => "fa-cloud-bolt",
                _ => "fa-cloud"
            };
        }

        private static bool IsBoundedText(string? value)
        {
            return value == null || value.Length <= MaxTextLength;
        }




        private static bool IsFiniteInRange(double? value, double min, double max)
        {
            if (value == null) return true;
            return double.IsFinite(value.Value) && value.Value >= min && value.Value <= max;
        }
    }
}
