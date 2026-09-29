using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    assertions++;
}

var request = new TrailGuardV2PredictionRequest
{
    ExerciseFrequency = "3\u20134",
    CardioDuration = "15\u201329",
    ExerciseConsistency = "3+ months",
    HikingExperience = "4\u201310",
    HikingRecency = "Within 3 months",
    HardestTrailCompleted = "Class 3",
    GearScore = 6,
    DistanceKm = 8,
    ElevationGainM = 600,
    TrailClass = 3,
    TypicalDurationHours = 5
};

var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "adapter-recorded-example.json");
var recordedAdapterResponse = await File.ReadAllTextAsync(fixturePath);

var valid = await InvokeAsync(HttpStatusCode.OK, recordedAdapterResponse, request, httpRequest =>
{
    Check(httpRequest.Method == HttpMethod.Post, "The v2 client must POST predictions.");
    Check(httpRequest.RequestUri?.AbsolutePath == "/predict", "The v2 client must use /predict.");
    using var document = JsonDocument.Parse(httpRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    var keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Check(keys.Length == 11, "The v2 request must contain exactly 11 properties.");
    Check(keys.SequenceEqual(TrailGuardV2ResponseValidator.FrozenFeatureOrder), "The v2 request feature order or names changed.");
    Check(!keys.Any(key => key.Contains("bmi", StringComparison.OrdinalIgnoreCase)
        || key.Contains("medical", StringComparison.OrdinalIgnoreCase)
        || key.Contains("id", StringComparison.OrdinalIgnoreCase)
        || key.Contains("metadata", StringComparison.OrdinalIgnoreCase)),
        "The v2 request includes prohibited data.");
});
Check(valid.IsSuccess && valid.Prediction is not null, "The recorded adapter response was rejected.");

await CheckInvalidResponseAsync(Remove("model_score"), "Missing required response properties must fail.");
await CheckInvalidResponseAsync(Set("model_score", 1.1), "Out-of-range model score must fail.");
await CheckInvalidResponseAsync(Set("model_score", "NaN"), "Non-finite model score must fail.");
await CheckInvalidResponseAsync(Set("model_version", "trailguard-v2.0.1"), "Wrong model version must fail.");
await CheckInvalidResponseAsync(Set("frozen_model_sha256", "0" + new string('0', 63)), "Wrong model checksum must fail.");
await CheckInvalidResponseAsync(Set("binary_prediction", "No"), "Inconsistent binary prediction must fail.");
await CheckInvalidResponseAsync(Set("ui_label", "Borderline"), "Inconsistent UI label must fail.");
await CheckInvalidResponseAsync(SetPath(["ui_label_policy", "good_match", "threshold"], 0.81), "Wrong UI policy threshold must fail.");
await CheckInvalidResponseAsync(DuplicateContribution(), "Duplicate or out-of-order SHAP features must fail.");
await CheckInvalidResponseAsync(RemoveContribution(), "Missing SHAP features must fail.");
await CheckInvalidResponseAsync(SetPath(["shap", "verification", "within_tolerance"], false), "Failed SHAP verification must fail.");
await CheckInvalidResponseAsync(SetPath(["shap", "raw_margin"], 1.0), "Failed native additivity must fail.");

await CheckInvalidResponseAsync(SetContributionInput(0, JsonValue.Create("3\\u20134")), "Literal backslash-u categorical values must fail.");
await CheckInvalidResponseAsync(SetContributionInput(0, JsonValue.Create("No regular exercise")), "Wrong categorical values must fail.");
await CheckInvalidResponseAsync(SetContributionInput(6, JsonValue.Create(7)), "Wrong numeric values must fail.");
await CheckInvalidResponseAsync(SetContributionInput(0, null), "Null original inputs must fail.");
await CheckInvalidResponseAsync(SetContributionInput(0, JsonValue.Create(true)), "Boolean original inputs must fail.");
await CheckInvalidResponseAsync(SetContributionInput(0, new JsonArray("unexpected")), "Array original inputs must fail.");
await CheckInvalidResponseAsync(SetContributionInput(0, new JsonObject { ["unexpected"] = true }), "Object original inputs must fail.");
await CheckValidResponseAsync(SetContributionInput(7, JsonValue.Create(8)), "Equivalent numeric representations must succeed.");

var invalidInput = await InvokeAsync(HttpStatusCode.UnprocessableEntity, "{\"detail\":\"Invalid prediction request.\"}", request);
Check(invalidInput.Failure == TrailGuardV2PredictionFailure.InvalidInput && invalidInput.Prediction is null,
    "HTTP 422 must be classified as invalid input.");

var unavailable = await InvokeAsync(HttpStatusCode.ServiceUnavailable, "{\"detail\":\"Unavailable\"}", request);
Check(unavailable.Failure == TrailGuardV2PredictionFailure.ServiceUnavailable && unavailable.Prediction is null,
    "HTTP 503 must be classified as unavailable.");

using (var transportClient = new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("offline")))
       { BaseAddress = new Uri("http://v2-adapter.test") })
{
    var transportFailure = await new TrailGuardV2ApiClient(transportClient, NullLogger<TrailGuardV2ApiClient>.Instance)
        .PredictAsync(request);
    Check(transportFailure.Failure == TrailGuardV2PredictionFailure.ServiceUnavailable && transportFailure.Prediction is null,
        "Transport failures must be classified as unavailable.");
}

var healthyHealth = await InvokeHealthAsync(HttpStatusCode.OK,
    "{\"status\":\"ok\",\"service\":\"TrailGuard ML v2 adapter\",\"model_version\":\"trailguard-v2.0.0\",\"selected_tree_count\":985}",
    httpRequest => Check(httpRequest.Method == HttpMethod.Get && httpRequest.RequestUri?.AbsolutePath == "/health",
        "The startup v2 health probe must use GET /health."));
Check(healthyHealth.Status == TrailGuardV2HealthCheckStatus.Healthy,
    "The expected v2 adapter health identity must be accepted.");

using (var healthTransportClient = new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("offline")))
       { BaseAddress = new Uri("http://v2-adapter.test") })
{
    var unreachableHealth = await new TrailGuardV2ApiClient(healthTransportClient, NullLogger<TrailGuardV2ApiClient>.Instance)
        .CheckHealthAsync();
    Check(unreachableHealth.Status == TrailGuardV2HealthCheckStatus.Unreachable,
        "An unreachable v2 health endpoint must be classified as unreachable.");
}

var unexpectedHealth = await InvokeHealthAsync(HttpStatusCode.OK,
    "{\"status\":\"ok\",\"service\":\"Unexpected service\",\"model_version\":\"trailguard-v2.0.0\",\"selected_tree_count\":985}");
Check(unexpectedHealth.Status == TrailGuardV2HealthCheckStatus.UnexpectedResponse,
    "An unexpected v2 health service or model identity must be rejected.");

Console.WriteLine($"PASS: {assertions} assertions. Fake-handler only; no database, controller, Python service, or final Test data access.");

if (args.Contains("--live", StringComparer.Ordinal))
{
    var fakeAssertionCount = assertions;
    await RunLiveVerificationAsync();
    Console.WriteLine($"LIVE PASS: {assertions - fakeAssertionCount} assertions against the loopback Python adapter.");
}

async Task CheckInvalidResponseAsync(string body, string message)
{
    var result = await InvokeAsync(HttpStatusCode.OK, body, request);
    Check(result.Failure == TrailGuardV2PredictionFailure.InvalidServiceResponse && result.Prediction is null, message);
}

async Task CheckValidResponseAsync(string body, string message)
{
    var result = await InvokeAsync(HttpStatusCode.OK, body, request);
    Check(result.IsSuccess && result.Prediction is not null, message);
}

async Task<TrailGuardV2PredictionCallResult> InvokeAsync(
    HttpStatusCode statusCode,
    string body,
    TrailGuardV2PredictionRequest predictionRequest,
    Action<HttpRequestMessage>? inspectRequest = null)
{
    using var client = new HttpClient(new StubHttpMessageHandler(httpRequest =>
    {
        inspectRequest?.Invoke(httpRequest);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }))
    {
        BaseAddress = new Uri("http://v2-adapter.test")
    };

    return await new TrailGuardV2ApiClient(client, NullLogger<TrailGuardV2ApiClient>.Instance).PredictAsync(predictionRequest);
}

async Task<TrailGuardV2HealthCheckResult> InvokeHealthAsync(
    HttpStatusCode statusCode,
    string body,
    Action<HttpRequestMessage>? inspectRequest = null)
{
    using var client = new HttpClient(new StubHttpMessageHandler(httpRequest =>
    {
        inspectRequest?.Invoke(httpRequest);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }))
    {
        BaseAddress = new Uri("http://v2-adapter.test")
    };

    return await new TrailGuardV2ApiClient(client, NullLogger<TrailGuardV2ApiClient>.Instance).CheckHealthAsync();
}

JsonObject FixtureNode() => JsonNode.Parse(recordedAdapterResponse)!.AsObject();

string Remove(string property)
{
    var root = FixtureNode();
    root.Remove(property);
    return root.ToJsonString();
}

string Set(string property, object? value)
{
    var root = FixtureNode();
    root[property] = JsonSerializer.SerializeToNode(value);
    return root.ToJsonString();
}

string SetPath(string[] path, object? value)
{
    var root = FixtureNode();
    JsonObject current = root;
    for (var index = 0; index < path.Length - 1; index++)
    {
        current = current[path[index]]!.AsObject();
    }

    current[path[^1]] = JsonSerializer.SerializeToNode(value);
    return root.ToJsonString();
}

string DuplicateContribution()
{
    var root = FixtureNode();
    var contributions = root["shap"]!["contributions"]!.AsArray();
    contributions[1]!["feature"] = contributions[0]!["feature"]!.GetValue<string>();
    return root.ToJsonString();
}

string RemoveContribution()
{
    var root = FixtureNode();
    root["shap"]!["contributions"]!.AsArray().RemoveAt(10);
    return root.ToJsonString();
}

string SetContributionInput(int index, JsonNode? value)
{
    var root = FixtureNode();
    root["shap"]!["contributions"]!.AsArray()[index]!["original_input_value"] = value;
    return root.ToJsonString();
}

async Task RunLiveVerificationAsync()
{
    var liveUrl = Environment.GetEnvironmentVariable("TRAILGUARD_V2_LIVE_URL");
    if (!Uri.TryCreate(liveUrl, UriKind.Absolute, out var adapterUri) || !adapterUri.IsLoopback)
    {
        throw new InvalidOperationException(
            "Live mode requires TRAILGUARD_V2_LIVE_URL to be an absolute loopback URL.");
    }

    var exampleInputPath = Environment.GetEnvironmentVariable("TRAILGUARD_V2_EXAMPLE_INPUT")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "ml-services", "trailguard-v2", "frozen-bundle", "example_input.json");
    var exampleJson = await File.ReadAllTextAsync(exampleInputPath);
    var frozenExampleRequest = JsonSerializer.Deserialize<TrailGuardV2PredictionRequest>(
        exampleJson,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (frozenExampleRequest is null)
    {
        throw new InvalidOperationException("The frozen example input could not be deserialized.");
    }

    using var client = new HttpClient { BaseAddress = adapterUri };
    var result = await new TrailGuardV2ApiClient(client, NullLogger<TrailGuardV2ApiClient>.Instance)
        .PredictAsync(frozenExampleRequest);

    Check(result.IsSuccess && result.Prediction is not null, "The C# v2 client rejected the live adapter response.");
    var prediction = result.Prediction!;
    Check(prediction.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion, "Live model version changed.");
    Check(prediction.FrozenModelSha256 == TrailGuardV2ResponseValidator.ExpectedFrozenModelSha256, "Live model checksum changed.");
    Check(prediction.SelectedTreeCount == TrailGuardV2ResponseValidator.ExpectedSelectedTreeCount, "Live selected tree count changed.");

    var recordedScore = FixtureNode()["model_score"]!.GetValue<double>();
    Check(prediction.ModelScore.HasValue
        && Math.Abs(prediction.ModelScore.Value - recordedScore) <= TrailGuardV2ResponseValidator.NativeTreeShapTolerance,
        "Live score differs from the recorded example beyond the frozen tolerance.");
    Check(prediction.BinaryPrediction == "Yes", "Live binary prediction was not Yes.");
    Check(prediction.UiLabel == "Good Match", "Live UI label was not Good Match.");
    Check(prediction.Shap?.Contributions?.Count == TrailGuardV2ResponseValidator.FrozenFeatureOrder.Count,
        "Live response did not contain all 11 contributions.");
    Check(OriginalInputsMatchFrozenExample(prediction, exampleJson),
        "Live SHAP original inputs did not match the frozen example input.");
    Check(prediction.Shap?.Verification?.PredictionsUnchanged is true
        && prediction.Shap.Verification.WithinTolerance is true
        && prediction.Shap.Verification.FeatureOrderMatchesFrozenSchema is true,
        "Live SHAP verification was not successful.");
}

bool OriginalInputsMatchFrozenExample(TrailGuardV2PredictionResponse prediction, string exampleJson)
{
    using var exampleDocument = JsonDocument.Parse(exampleJson);
    var contributions = prediction.Shap?.Contributions;
    if (contributions is null || contributions.Count != TrailGuardV2ResponseValidator.FrozenFeatureOrder.Count)
    {
        return false;
    }

    foreach (var contribution in contributions)
    {
        if (contribution?.Feature is null
            || !contribution.OriginalInputValue.HasValue
            || !exampleDocument.RootElement.TryGetProperty(contribution.Feature, out var expected))
        {
            return false;
        }

        var actual = contribution.OriginalInputValue.Value;
        if (expected.ValueKind == JsonValueKind.String)
        {
            if (actual.ValueKind != JsonValueKind.String
                || !string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (expected.ValueKind == JsonValueKind.Number)
        {
            if (actual.ValueKind != JsonValueKind.Number
                || !expected.TryGetDouble(out var expectedNumber)
                || !actual.TryGetDouble(out var actualNumber)
                || !double.IsFinite(expectedNumber)
                || !double.IsFinite(actualNumber)
                || actualNumber != expectedNumber)
            {
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    return true;
}

sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}
