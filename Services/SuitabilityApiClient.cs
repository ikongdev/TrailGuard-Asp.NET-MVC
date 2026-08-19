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

        // Startup-only liveness probe against GET / - lets us log loudly before a
        // participant ever hits a submission that silently can't get a prediction.
        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ML API health check failed.");
                return false;
            }
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