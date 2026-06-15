using System.Text.Json;

namespace TrailGuard.Services
{
    public class WeatherService
    {
        private readonly HttpClient _httpClient;

        public WeatherService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetWeatherForecastAsync(string location, DateTime eventDate)
        {
            try
            {
                // Geocoding - convert location name to coordinates
                var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(location)}&count=1&language=en&format=json";
                var geoResponse = await _httpClient.GetAsync(geoUrl);
                
                if (!geoResponse.IsSuccessStatusCode)
                    return "Weather forecast unavailable at this time.";

                var geoJson = await geoResponse.Content.ReadAsStringAsync();
                using var geoDoc = JsonDocument.Parse(geoJson);
                
                var root = geoDoc.RootElement;
                if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    return $"Weather forecast not available for '{location}'.";

                var firstResult = results[0];
                var latitude = firstResult.GetProperty("latitude").GetDouble();
                var longitude = firstResult.GetProperty("longitude").GetDouble();

                // Get forecast for the specific date with wind speed included
                var targetDate = eventDate.ToString("yyyy-MM-dd");
                var forecastUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode,windspeed_10m_max&timezone=auto&start_date={targetDate}&end_date={targetDate}";
                
                var forecastResponse = await _httpClient.GetAsync(forecastUrl);
                if (!forecastResponse.IsSuccessStatusCode)
                    return "Weather forecast temporarily unavailable.";

                var forecastJson = await forecastResponse.Content.ReadAsStringAsync();
                using var forecastDoc = JsonDocument.Parse(forecastJson);
                
                var daily = forecastDoc.RootElement.GetProperty("daily");
                var tempMax = daily.GetProperty("temperature_2m_max")[0].GetDouble();
                var tempMin = daily.GetProperty("temperature_2m_min")[0].GetDouble();
                var precipitation = daily.GetProperty("precipitation_sum")[0].GetDouble();
                var weatherCode = daily.GetProperty("weathercode")[0].GetInt32();
                
                // Get wind speed (try to get it, default to 0 if not available)
                double windSpeed = 0;
                if (daily.TryGetProperty("windspeed_10m_max", out var windElement))
                {
                    windSpeed = windElement[0].GetDouble();
                }

                var weatherDescription = GetWeatherDescription(weatherCode);
                var rainChance = precipitation > 0 ? $"{Math.Min(100, (int)(precipitation * 10))}%" : "0%";
                var riskLevel = GetRiskLevel(precipitation, weatherCode);
                var windSpeedText = GetWindSpeedDescription(windSpeed);

                return $"Expected Weather: {weatherDescription}\n" +
                       $"Temperature: {tempMin:F0}°C ~ {tempMax:F0}°C\n" +
                       $"Chance of Rain: {rainChance}\n" +
                       $"Wind Speed: {windSpeedText}\n" +
                       $"Weather Risk Level: {riskLevel}\n" +
                       $"Last Updated: {DateTime.Now:MMMM dd, yyyy, h:mm tt}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather API error: {ex.Message}");
                return "Weather forecast temporarily unavailable. Please check manually.";
            }
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

        private string GetWindSpeedDescription(double windSpeedKmh)
        {
            if (windSpeedKmh <= 0)
                return "Check local forecast";
            if (windSpeedKmh <= 10)
                return $"{windSpeedKmh:F0} km/h (Light air)";
            if (windSpeedKmh <= 20)
                return $"{windSpeedKmh:F0} km/h (Gentle breeze)";
            if (windSpeedKmh <= 30)
                return $"{windSpeedKmh:F0} km/h (Moderate breeze)";
            if (windSpeedKmh <= 40)
                return $"{windSpeedKmh:F0} km/h (Fresh breeze)";
            if (windSpeedKmh <= 50)
                return $"{windSpeedKmh:F0} km/h (Strong breeze)";
            return $"{windSpeedKmh:F0} km/h (High wind)";
        }
    }
}