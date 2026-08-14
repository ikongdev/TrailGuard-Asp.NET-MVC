using System.Net.Http.Json;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    public class SuitabilityApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SuitabilityApiClient> _logger;

        public SuitabilityApiClient(HttpClient httpClient, ILogger<SuitabilityApiClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<SuitabilityPredictionResponse?> PredictAsync(SuitabilityPredictionRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/predict", request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("ML API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<SuitabilityPredictionResponse>();
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("ML API request timed out.");
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Could not reach ML API.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calling ML API.");
                return null;
            }
        }
    }
}