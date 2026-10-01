using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace TrailGuard.Services;

public sealed class SupabaseFileStore(HttpClient client, UploadStorageOptions options, UploadReferences references)
{
    public static HttpMessageHandler CreateHandler() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };

    private HttpRequestMessage Request(HttpMethod method, UploadReference reference, bool download = false)
    {
        if (!reference.Cloud || references.Parse(reference.Value, reference.Category) != reference
            || options.Origin == null || options.SecretKey == null
            || !System.Text.RegularExpressions.Regex.IsMatch(options.SecretKey, "^sb_secret_[A-Za-z0-9_-]+\\z"))
            throw new IOException("Storage is unavailable.");
        var path = method == HttpMethod.Delete ? references.Bucket(reference.Category)
            : (download ? "authenticated/" : "") + references.Bucket(reference.Category) + "/" + reference.Key;
        var request = new HttpRequestMessage(method, options.Origin + "/storage/v1/object/" + path);
        // sb_secret keys are opaque API keys, NOT JWT bearer tokens. Never put one in Authorization.
        request.Headers.Add("apikey", options.SecretKey);
        return request;
    }

    public async Task UploadAsync(UploadReference reference, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, reference);
        request.Headers.Add("x-upsert", "false");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        EnsureSuccess(response);
    }

    public async Task<byte[]?> ReadAsync(UploadReference reference, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, reference, download: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        if (response.Content.Headers.ContentLength > UploadStorageOptions.MaxFileBytes) throw new IOException("File exceeds 5 MiB.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await UploadBytes.ReadAsync(stream, timeout.Token);
    }

    public async Task DeleteAsync(UploadReference reference, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Delete, reference);
        request.Content = JsonContent.Create(new { prefixes = new[] { reference.Key } });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode != HttpStatusCode.NotFound) EnsureSuccess(response);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        // Do not include provider response bodies, request headers, or credentials in errors/logs.
        if (!response.IsSuccessStatusCode) throw new IOException($"Storage request failed ({(int)response.StatusCode}).");
    }
}
