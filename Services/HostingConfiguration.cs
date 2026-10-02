using System.Net;
using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Primitives;

namespace TrailGuard.Services;

public abstract record HostedDataProtectionOptions(string ApplicationName)
{
    private static readonly HashSet<string> KnownSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Provider",
        "ApplicationName",
        "KeyRingPath",
        "AzureBlobUri",
        "KeyVaultKeyIdentifier",
        "ManagedIdentityClientId"
    };

    public static HostedDataProtectionOptions? Resolve(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        UploadStorageOptions uploadStorage)
    {
        var settings = configuration.GetSection("DataProtection")
            .GetChildren()
            .ToDictionary(child => child.Key, child => child.Value, StringComparer.OrdinalIgnoreCase);
        if (settings.Count == 0)
        {
            return null;
        }

        var unknownSetting = settings.Keys.FirstOrDefault(key => !KnownSettings.Contains(key));
        if (unknownSetting is not null)
        {
            throw new InvalidOperationException("DataProtection contains an unsupported provider setting.");
        }

        var providerIsConfigured = settings.TryGetValue("Provider", out var configuredProvider);
        if (providerIsConfigured && string.IsNullOrWhiteSpace(configuredProvider))
        {
            throw new InvalidOperationException("DataProtection:Provider must be Filesystem or AzureBlobKeyVault.");
        }

        var provider = configuredProvider?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            if (settings.Keys.Any(key => key.Equals("AzureBlobUri", StringComparison.OrdinalIgnoreCase)
                || key.Equals("KeyVaultKeyIdentifier", StringComparison.OrdinalIgnoreCase)
                || key.Equals("ManagedIdentityClientId", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Azure Data Protection settings require DataProtection:Provider=AzureBlobKeyVault.");
            }

            return ResolveFilesystem(settings, environment, uploadStorage);
        }

        return provider.ToUpperInvariant() switch
        {
            "FILESYSTEM" => ResolveFilesystem(settings, environment, uploadStorage),
            "AZUREBLOBKEYVAULT" => ResolveAzureBlobKeyVault(settings),
            _ => throw new InvalidOperationException("DataProtection:Provider must be Filesystem or AzureBlobKeyVault.")
        };
    }

    public abstract void Configure(
        IServiceCollection services,
        IAzureDataProtectionRegistration? azureRegistration = null);

    private static FilesystemDataProtectionOptions ResolveFilesystem(
        IReadOnlyDictionary<string, string?> settings,
        IWebHostEnvironment environment,
        UploadStorageOptions uploadStorage)
    {
        if (settings.Keys.Any(key => key.Equals("AzureBlobUri", StringComparison.OrdinalIgnoreCase)
            || key.Equals("KeyVaultKeyIdentifier", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ManagedIdentityClientId", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Filesystem Data Protection cannot include Azure provider settings.");
        }

        var applicationName = Required(settings, "ApplicationName");
        var configuredPath = Required(settings, "KeyRingPath");
        string keyRingPath;
        try
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                throw new InvalidOperationException();
            }

            keyRingPath = Path.GetFullPath(configuredPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            throw new InvalidOperationException("DataProtection:KeyRingPath must be an absolute path.");
        }

        if (string.IsNullOrWhiteSpace(keyRingPath) || Path.GetPathRoot(keyRingPath) == keyRingPath)
        {
            throw new InvalidOperationException("DataProtection:KeyRingPath must name a directory below the filesystem root.");
        }

        if (PathsOverlap(keyRingPath, environment.WebRootPath) || PathsOverlap(keyRingPath, uploadStorage.LocalRoot))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath must be outside wwwroot and Storage:Local:RootPath.");
        }

        return new FilesystemDataProtectionOptions(applicationName, keyRingPath);
    }

    private static AzureBlobKeyVaultDataProtectionOptions ResolveAzureBlobKeyVault(
        IReadOnlyDictionary<string, string?> settings)
    {
        if (settings.ContainsKey("KeyRingPath"))
        {
            throw new InvalidOperationException("AzureBlobKeyVault Data Protection cannot include DataProtection:KeyRingPath.");
        }

        var applicationName = Required(settings, "ApplicationName");
        var blobUri = ValidateBlobUri(Required(settings, "AzureBlobUri"));
        var keyVaultKeyIdentifier = ValidateVersionlessKeyVaultKeyUri(Required(settings, "KeyVaultKeyIdentifier"));
        var managedIdentityClientId = Required(settings, "ManagedIdentityClientId");
        if (!Guid.TryParse(managedIdentityClientId, out _))
        {
            throw new InvalidOperationException("DataProtection:ManagedIdentityClientId must be a managed identity client ID.");
        }

        return new AzureBlobKeyVaultDataProtectionOptions(
            applicationName,
            blobUri,
            keyVaultKeyIdentifier,
            managedIdentityClientId);
    }

    private static string Required(IReadOnlyDictionary<string, string?> settings, string name)
    {
        if (!settings.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"DataProtection:{name} must be configured.");
        }

        return value.Trim();
    }

    private static Uri ValidateBlobUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            || uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            throw new InvalidOperationException("DataProtection:AzureBlobUri must be an HTTPS Azure Blob URI without a query, fragment, or credentials.");
        }

        return uri;
    }

    private static Uri ValidateVersionlessKeyVaultKeyUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.Host.EndsWith(".vault.azure.net", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("DataProtection:KeyVaultKeyIdentifier must be a versionless HTTPS Azure Key Vault key URI without a query, fragment, or credentials.");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !segments[0].Equals("keys", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(segments[1]))
        {
            throw new InvalidOperationException("DataProtection:KeyVaultKeyIdentifier must be a versionless Azure Key Vault key URI.");
        }

        return uri;
    }

    internal static void EnsureWritableDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var probePath = Path.Combine(path, ".trailguard-write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException("DataProtection:KeyRingPath is not writable by the application user.");
        }
        finally
        {
            File.Delete(probePath);
        }
    }

    internal static bool PathsOverlap(string first, string second)
    {
        var firstPath = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var secondPath = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return firstPath.Equals(secondPath, StringComparison.OrdinalIgnoreCase)
            || firstPath.StartsWith(secondPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || secondPath.StartsWith(firstPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FilesystemDataProtectionOptions(string ApplicationName, string KeyRingPath)
    : HostedDataProtectionOptions(ApplicationName)
{
    public override void Configure(
        IServiceCollection services,
        IAzureDataProtectionRegistration? azureRegistration = null)
    {
        if (azureRegistration is not null)
        {
            throw new InvalidOperationException("Azure registration cannot be used with filesystem Data Protection.");
        }

        EnsureWritableDirectory(KeyRingPath);
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(KeyRingPath));
    }
}

public sealed record AzureBlobKeyVaultDataProtectionOptions(
    string ApplicationName,
    Uri AzureBlobUri,
    Uri KeyVaultKeyIdentifier,
    string ManagedIdentityClientId)
    : HostedDataProtectionOptions(ApplicationName)
{
    public override void Configure(
        IServiceCollection services,
        IAzureDataProtectionRegistration? azureRegistration = null)
    {
        (azureRegistration ?? ManagedIdentityAzureDataProtectionRegistration.Instance)
            .Configure(services.AddDataProtection().SetApplicationName(ApplicationName), this);
    }
}

public interface IAzureDataProtectionRegistration
{
    void Configure(IDataProtectionBuilder builder, AzureBlobKeyVaultDataProtectionOptions options);
}

public sealed class ManagedIdentityAzureDataProtectionRegistration : IAzureDataProtectionRegistration
{
    public static ManagedIdentityAzureDataProtectionRegistration Instance { get; } = new();

    private ManagedIdentityAzureDataProtectionRegistration()
    {
    }

    public void Configure(IDataProtectionBuilder builder, AzureBlobKeyVaultDataProtectionOptions options)
    {
        TokenCredential credential = new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        builder.PersistKeysToAzureBlobStorage(options.AzureBlobUri, credential)
            .ProtectKeysWithAzureKeyVault(options.KeyVaultKeyIdentifier, credential);
    }
}

public sealed record TrailGuardHostingOptions(
    ForwardedHeadersOptions? ForwardedHeaders,
    ProxyHeaderObservationOptions? ProxyHeaderObservation,
    int? HttpsRedirectPort)
{
    public static TrailGuardHostingOptions Resolve(IConfiguration configuration, TimeProvider timeProvider)
    {
        return new TrailGuardHostingOptions(
            TrustedForwardedHeaders.Resolve(configuration),
            ProxyHeaderObservationOptions.Resolve(configuration, timeProvider),
            ResolveHttpsRedirectPort(configuration));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpsRedirection(options =>
        {
            if (HttpsRedirectPort is { } port)
            {
                options.HttpsPort = port;
            }
        });
    }

    private static int? ResolveHttpsRedirectPort(IConfiguration configuration)
    {
        var configuredPort = configuration["ASPNETCORE_HTTPS_PORT"];
        if (string.IsNullOrWhiteSpace(configuredPort))
        {
            return null;
        }

        if (!int.TryParse(configuredPort, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("ASPNETCORE_HTTPS_PORT must be a TCP port from 1 through 65535.");
        }

        return port;
    }
}

public sealed record ProxyHeaderObservationOptions(DateTimeOffset ExpiresAtUtc, int MaxEventCount)
{
    private static readonly HashSet<string> KnownSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enabled",
        "ExpiresAtUtc",
        "MaxEventCount"
    };

    private static readonly TimeSpan MaximumWindow = TimeSpan.FromHours(1);

    public static ProxyHeaderObservationOptions? Resolve(IConfiguration configuration, TimeProvider timeProvider)
    {
        var settings = configuration.GetSection("ProxyHeaderObservation")
            .GetChildren()
            .ToDictionary(child => child.Key, child => child.Value, StringComparer.OrdinalIgnoreCase);
        if (settings.Count == 0)
        {
            return null;
        }

        if (settings.Keys.Any(key => !KnownSettings.Contains(key)))
        {
            throw new InvalidOperationException("ProxyHeaderObservation contains an unsupported setting.");
        }

        if (!settings.TryGetValue("Enabled", out var configuredEnabled)
            || !bool.TryParse(configuredEnabled, out var enabled))
        {
            throw new InvalidOperationException("ProxyHeaderObservation:Enabled must be true or false.");
        }

        if (!enabled)
        {
            return null;
        }

        if (!settings.TryGetValue("ExpiresAtUtc", out var configuredExpiry)
            || !DateTimeOffset.TryParse(configuredExpiry, out var expiresAtUtc)
            || expiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("ProxyHeaderObservation:ExpiresAtUtc must be an absolute UTC timestamp.");
        }

        if (expiresAtUtc - timeProvider.GetUtcNow() > MaximumWindow)
        {
            throw new InvalidOperationException("ProxyHeaderObservation:ExpiresAtUtc must be within one hour of startup.");
        }

        if (!settings.TryGetValue("MaxEventCount", out var configuredMaximum)
            || !int.TryParse(configuredMaximum, out var maximumEventCount)
            || maximumEventCount is < 1 or > 50)
        {
            throw new InvalidOperationException("ProxyHeaderObservation:MaxEventCount must be from 1 through 50.");
        }

        return new ProxyHeaderObservationOptions(expiresAtUtc, maximumEventCount);
    }
}

public sealed class ProxyHeaderObserver(ProxyHeaderObservationOptions options, ILogger logger, TimeProvider timeProvider)
{
    private int _emittedCount;

    public void Observe(HttpContext context)
    {
        if (timeProvider.GetUtcNow() >= options.ExpiresAtUtc
            || Interlocked.Increment(ref _emittedCount) > options.MaxEventCount
            || timeProvider.GetUtcNow() >= options.ExpiresAtUtc)
        {
            return;
        }

        var forwardedFor = SummarizeHeader(context.Request.Headers["X-Forwarded-For"]);
        var forwardedProto = SummarizeHeader(context.Request.Headers["X-Forwarded-Proto"]);
        logger.LogInformation(
            "Proxy header observation: PeerIp={PeerIp}; RequestScheme={RequestScheme}; XForwardedForPresent={XForwardedForPresent}; XForwardedForCount={XForwardedForCount}; XForwardedProtoPresent={XForwardedProtoPresent}; XForwardedProtoCount={XForwardedProtoCount}; ForwardedScheme={ForwardedScheme}.",
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            SchemeClassification(context.Request.Scheme),
            forwardedFor.IsPresent,
            forwardedFor.ElementCount,
            forwardedProto.IsPresent,
            forwardedProto.ElementCount,
            ForwardedSchemeClassification(forwardedProto.Elements));
    }

    private static HeaderSummary SummarizeHeader(StringValues values)
    {
        var elements = values
            .SelectMany(value => value?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                ?? Array.Empty<string>())
            .ToArray();
        return new HeaderSummary(values.Count > 0, elements);
    }

    private static string ForwardedSchemeClassification(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "absent";
        }

        var classifications = values.Select(SchemeClassification).Distinct(StringComparer.Ordinal).ToArray();
        return classifications.Length == 1 ? classifications[0] : "mixed";
    }

    private static string SchemeClassification(string? scheme) => scheme?.ToLowerInvariant() switch
    {
        "http" => "http",
        "https" => "https",
        _ => "other"
    };

    private sealed record HeaderSummary(bool IsPresent, string[] Elements)
    {
        public int ElementCount => Elements.Length;
    }
}

public static class TrustedForwardedHeaders
{
    public static ForwardedHeadersOptions? Resolve(IConfiguration configuration)
    {
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ASPNETCORE_FORWARDEDHEADERS_ENABLED must not be enabled when TrailGuard uses explicit trusted proxies.");
        }

        var proxies = Values(configuration, "ForwardedHeaders:TrustedProxies");
        var networks = Values(configuration, "ForwardedHeaders:TrustedNetworks");
        if (proxies.Count == 0 && networks.Count == 0)
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException("ForwardedHeaders:TrustedProxies contains an invalid IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(ParseNetwork(network));
        }

        return options;
    }

    private static IReadOnlyList<string> Values(IConfiguration configuration, string key) => configuration
        .GetSection(key)
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .ToArray();

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out var prefix))
        {
            throw new InvalidOperationException("ForwardedHeaders:TrustedNetworks contains an invalid CIDR network.");
        }

        var maximumPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefix <= 0 || prefix > maximumPrefix)
        {
            throw new InvalidOperationException("ForwardedHeaders:TrustedNetworks must not trust an entire address family.");
        }

        return new System.Net.IPNetwork(address, prefix);
    }
}

public static class TrailGuardHostingPipeline
{
    public static void Configure(
        IApplicationBuilder app,
        IWebHostEnvironment environment,
        TrailGuardHostingOptions options,
        ILogger logger,
        TimeProvider timeProvider)
    {
        if (options.ProxyHeaderObservation is { } proxyHeaderObservation)
        {
            var observer = new ProxyHeaderObserver(proxyHeaderObservation, logger, timeProvider);
            app.Use(async (context, next) =>
            {
                observer.Observe(context);
                await next();
            });
        }

        if (options.ForwardedHeaders is not null)
        {
            app.UseForwardedHeaders(options.ForwardedHeaders);
        }

        if (!environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseWhen(TrailGuardLiveness.IsRequest, health => health.Run(TrailGuardLiveness.WriteAsync));
    }
}

public static class TrailGuardLiveness
{
    public static bool IsRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/healthz";

    public static Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    }
}
