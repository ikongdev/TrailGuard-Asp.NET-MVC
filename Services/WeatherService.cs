using System.Text.Json;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    public class WeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<WeatherResult> GetWeatherForecastAsync(string location, DateTime eventDate)
        {
            if (string.IsNullOrWhiteSpace(location))
                return UnavailableResult("No location set for this trail.", "NoLocation");

            try
            {


                var parts = location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var searchTerm = parts[0];
                var provinceHint = parts.Length > 1 ? parts[^1] : null;


                var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(searchTerm)}&count=10&language=en&format=json&countryCode=PH";


                _logger.LogDebug("Weather geocoding request: {GeoUrl}", geoUrl);
                var geoResponse = await _httpClient.GetAsync(geoUrl);

                if (!geoResponse.IsSuccessStatusCode)
                    return UnavailableResult("Weather forecast unavailable at this time.", "ServiceDown");

                var geoJson = await geoResponse.Content.ReadAsStringAsync();
                using var geoDoc = JsonDocument.Parse(geoJson);

                var root = geoDoc.RootElement;
                if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    return UnavailableResult($"Weather forecast not available for '{location}'.", "LocationNotFound");

                var chosenResult = SelectBestMatch(results, provinceHint);
                var latitude = chosenResult.GetProperty("latitude").GetDouble();
                var longitude = chosenResult.GetProperty("longitude").GetDouble();


                var targetDate = eventDate.ToString("yyyy-MM-dd");
                var forecastUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode,windspeed_10m_max&timezone=auto&start_date={targetDate}&end_date={targetDate}";



                _logger.LogDebug("Weather forecast request: {ForecastUrl}", forecastUrl);
                var forecastResponse = await _httpClient.GetAsync(forecastUrl);










                if (forecastResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    var errorBody = await forecastResponse.Content.ReadAsStringAsync();
                    var reason = TryGetErrorReason(errorBody) ?? errorBody;

                    if (reason.Contains("out of allowed range", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "Weather forecast date out of range. Url={ForecastUrl} Reason={Reason}",
                            forecastUrl, reason);
                        return UnavailableResult("This date is beyond the available forecast window.", "TooFarAhead");
                    }

                    _logger.LogWarning(
                        "Weather forecast request rejected (400) for a reason other than date range. Url={ForecastUrl} Reason={Reason}",
                        forecastUrl, reason);
                    return UnavailableResult("Weather forecast temporarily unavailable.", "ServiceDown");
                }

                if (!forecastResponse.IsSuccessStatusCode)
                    return UnavailableResult("Weather forecast temporarily unavailable.", "ServiceDown");

                var forecastJson = await forecastResponse.Content.ReadAsStringAsync();
                using var forecastDoc = JsonDocument.Parse(forecastJson);

                var daily = forecastDoc.RootElement.GetProperty("daily");

                var tempMaxArray = daily.GetProperty("temperature_2m_max");
                if (tempMaxArray.GetArrayLength() == 0 || tempMaxArray[0].ValueKind == JsonValueKind.Null)
                    return UnavailableResult("This date is beyond the available forecast window.", "TooFarAhead");

                var tempMax = tempMaxArray[0].GetDouble();
                var tempMin = daily.GetProperty("temperature_2m_min")[0].GetDouble();
                var precipitation = daily.GetProperty("precipitation_sum")[0].GetDouble();
                var weatherCode = daily.GetProperty("weathercode")[0].GetInt32();


                double windSpeed = 0;
                if (daily.TryGetProperty("windspeed_10m_max", out var windElement))
                {
                    windSpeed = windElement[0].GetDouble();
                }

                var weatherDescription = GetWeatherDescription(weatherCode);
                var riskLevel = GetRiskLevel(precipitation, weatherCode);
                var windDescription = GetWindDescriptionLabel(windSpeed);
                var windSpeedText = GetWindSpeedDescription(windSpeed);




                var updatedAt = DateTimeOffset.Now;





                var forecastDetails = $"Expected Weather: {weatherDescription}\n" +
                       $"Temperature: {tempMin:F0}°C ~ {tempMax:F0}°C\n" +
                       $"Expected Rainfall: {FormatRainfall(precipitation)} mm\n" +
                       $"Wind Speed: {windSpeedText}\n" +
                       $"Last Updated: {updatedAt:MMMM dd, yyyy, h:mm tt}";

                return new WeatherResult
                {
                    ForecastDetails = forecastDetails,
                    RiskLevel = riskLevel,
                    SuggestedReminder = GetSuggestedReminder(riskLevel),
                    Condition = weatherDescription,
                    WeatherCode = weatherCode,
                    TemperatureMinC = tempMin,
                    TemperatureMaxC = tempMax,
                    ExpectedRainfallMm = precipitation,
                    WindSpeedKmh = windSpeed,
                    WindDescription = windDescription,
                    UpdatedAt = updatedAt
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather API error: {ex.Message}");
                return UnavailableResult("Weather forecast temporarily unavailable. Please check manually.", "Error");
            }
        }

        private static WeatherResult UnavailableResult(string message, string reason)
        {
            return new WeatherResult { ForecastDetails = message, UnavailableReason = reason };
        }



        private static string? TryGetErrorReason(string errorBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(errorBody);
                if (doc.RootElement.TryGetProperty("reason", out var reasonElement))
                    return reasonElement.GetString();
            }
            catch (JsonException)
            {
            }

            return null;
        }








        private static JsonElement SelectBestMatch(JsonElement results, string? provinceHint)
        {
            if (!string.IsNullOrWhiteSpace(provinceHint))
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (AdminFieldMatches(result, "admin1", provinceHint) ||
                        AdminFieldMatches(result, "admin2", provinceHint))
                    {
                        return result;
                    }
                }
            }

            return results[0];
        }

        private static bool AdminFieldMatches(JsonElement result, string propertyName, string hint)
        {
            if (!result.TryGetProperty(propertyName, out var element)) return false;

            var value = element.GetString();
            if (string.IsNullOrEmpty(value)) return false;

            return value.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
                   hint.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private string GetSuggestedReminder(string riskLevel)
        {
            return riskLevel switch
            {
                "Low" => "Conditions look favorable. Bring enough water and sun protection, and follow the usual trail safety guidelines.",
                "Moderate" => "Rain is possible. Bring a raincoat and waterproof your electronics. Expect slippery sections and allow extra time.",
                "Moderate to High" => "Heavy rain expected. Trails may be slippery and river crossings may rise. Bring full rain gear and be ready to turn back if conditions worsen.",
                "High (Thunderstorm)" => "Thunderstorms expected. Consider rescheduling. If the event pushes through, avoid exposed ridges and summits, and monitor conditions closely.",
                _ => string.Empty
            };
        }

        private string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear sky",
                1 or 2 or 3 => "Partly cloudy",
                45 or 48 => "Foggy",
                51 or 53 or 55 => "Light drizzle",
                56 or 57 => "Freezing drizzle",
                61 or 63 or 65 => "Rain expected",
                66 or 67 => "Freezing rain",
                71 or 73 or 75 => "Snow expected",
                80 or 81 or 82 => "Rain showers",
                95 => "Thunderstorm",
                96 or 99 => "Thunderstorm with hail",
                _ => "Variable weather"
            };
        }

        private string GetRiskLevel(double precipitation, int weatherCode)
        {
            if (weatherCode == 95 || weatherCode == 96 || weatherCode == 99)
                return "High (Thunderstorm)";
            if (precipitation > 15)
                return "Moderate to High";
            if (precipitation > 5)
                return "Moderate";
            return "Low";
        }





        private string GetWindDescriptionLabel(double windSpeedKmh)
        {
            if (windSpeedKmh <= 0)
                return "Check local forecast";
            if (windSpeedKmh <= 10)
                return "Light air";
            if (windSpeedKmh <= 20)
                return "Gentle breeze";
            if (windSpeedKmh <= 30)
                return "Moderate breeze";
            if (windSpeedKmh <= 40)
                return "Fresh breeze";
            if (windSpeedKmh <= 50)
                return "Strong breeze";
            return "High wind";
        }

        private string GetWindSpeedDescription(double windSpeedKmh)
        {
            if (windSpeedKmh <= 0)
                return GetWindDescriptionLabel(windSpeedKmh);
            return $"{windSpeedKmh:F0} km/h ({GetWindDescriptionLabel(windSpeedKmh)})";
        }




        private static string FormatRainfall(double rainfallMm)
        {
            return rainfallMm.ToString("0.#");
        }
    }
}
