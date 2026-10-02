using System.Net;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

var root = Path.Combine(Path.GetTempPath(), "trailguard-hosting-verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var webRoot = Path.Combine(root, "wwwroot");
    var uploadsRoot = Path.Combine(root, "uploads");
    var keyRing = Path.Combine(root, "keys");
    Directory.CreateDirectory(webRoot);
    Directory.CreateDirectory(uploadsRoot);

    var environment = new TestEnvironment(root, webRoot);
    var uploadStorage = new UploadStorageOptions
    {
        Namespace = "hosting-test",
        LocalRoot = uploadsRoot,
        WebRoot = webRoot
    };

    Check(HostedDataProtectionOptions.Resolve(new ConfigurationBuilder().Build(), environment, uploadStorage) is null,
        "Unconfigured Data Protection must retain the existing local-development provider behavior.");

    var dataProtectionConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification",
            ["DataProtection:KeyRingPath"] = keyRing
        })
        .Build();
    var hostedOptions = HostedDataProtectionOptions.Resolve(dataProtectionConfiguration, environment, uploadStorage)
        as FilesystemDataProtectionOptions
        ?? throw new InvalidOperationException("Filesystem Data Protection configuration was not selected.");
    Check(hostedOptions.ApplicationName == "TrailGuard.HostingVerification" && hostedOptions.KeyRingPath == keyRing,
        "Configured Data Protection options must preserve the stable application name and key-ring path.");

    var dataProtectionServices = new ServiceCollection();
    hostedOptions.Configure(dataProtectionServices);
    var firstProvider = dataProtectionServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    var protectedPayload = firstProvider.CreateProtector("hosting-verification").Protect("survives-recreation");
    var recreatedProvider = DataProtectionProvider.Create(new DirectoryInfo(keyRing), protector =>
        protector.SetApplicationName(hostedOptions.ApplicationName));
    Check(recreatedProvider.CreateProtector("hosting-verification").Unprotect(protectedPayload) == "survives-recreation",
        "A payload must survive provider recreation when the key ring and application name are unchanged.");

    var wrongNameProvider = DataProtectionProvider.Create(new DirectoryInfo(keyRing), protector =>
        protector.SetApplicationName("TrailGuard.HostingVerification.Other"));
    var uploadPathRejected = false;
    try
    {
        wrongNameProvider.CreateProtector("hosting-verification").Unprotect(protectedPayload);
        throw new InvalidOperationException("A different Data Protection application name decrypted a payload.");
    }
    catch (CryptographicException)
    {
        assertions++;
    }

    try
    {
        HostedDataProtectionOptions.Resolve(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification",
                ["DataProtection:KeyRingPath"] = uploadsRoot
            })
            .Build(), environment, uploadStorage);
    }
    catch (InvalidOperationException)
    {
        uploadPathRejected = true;
    }
    Check(uploadPathRejected, "A Data Protection key ring inside upload storage was accepted.");

    var azureDataProtectionConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Provider"] = "AzureBlobKeyVault",
            ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure",
            ["DataProtection:AzureBlobUri"] = "https://fakeaccount.blob.core.windows.net/data-protection/keyring.xml",
            ["DataProtection:KeyVaultKeyIdentifier"] = "https://fakevault.vault.azure.net/keys/trailguard-data-protection",
            ["DataProtection:ManagedIdentityClientId"] = "11111111-1111-1111-1111-111111111111"
        })
        .Build();
    var azureOptions = HostedDataProtectionOptions.Resolve(azureDataProtectionConfiguration, environment, uploadStorage)
        as AzureBlobKeyVaultDataProtectionOptions
        ?? throw new InvalidOperationException("Azure Data Protection configuration did not select AzureBlobKeyVault.");
    Check(azureOptions.ApplicationName == "TrailGuard.HostingVerification.Azure",
        "Azure Data Protection did not preserve the configured application name.");

    var azureStore = new InMemoryBlobStore();
    var azureKeyResolver = new DeterministicKeyEncryptionKeyResolver(azureOptions.KeyVaultKeyIdentifier.AbsoluteUri);
    var azureServices = new ServiceCollection();
    azureOptions.Configure(azureServices, new FakeAzureDataProtectionRegistration(azureStore, azureKeyResolver));
    var azureProvider = azureServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    var azurePayload = azureProvider.CreateProtector("azure-hosting-verification").Protect("survives-azure-provider-recreation");
    var storedAzureXml = azureStore.Content;
    Check(storedAzureXml is not null && storedAzureXml.Contains("encryptedSecret", StringComparison.Ordinal)
        && storedAzureXml.Contains(azureKeyResolver.KeyId, StringComparison.Ordinal),
        "The Azure key-ring XML was not stored through the Key Vault encryptor.");
    Check(!storedAzureXml!.Contains("survives-azure-provider-recreation", StringComparison.Ordinal),
        "The Azure key-ring XML stored protected payload plaintext.");
    Check(azureStore.RequestCount > 0 && !azureStore.NetworkAttempted,
        "Azure Data Protection verification did not stay inside the fake Blob transport.");

    var recreatedAzureServices = new ServiceCollection();
    azureOptions.Configure(recreatedAzureServices,
        new FakeAzureDataProtectionRegistration(azureStore, new DeterministicKeyEncryptionKeyResolver(azureKeyResolver.KeyId)));
    var recreatedAzureProvider = recreatedAzureServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    Check(recreatedAzureProvider.CreateProtector("azure-hosting-verification").Unprotect(azurePayload)
        == "survives-azure-provider-recreation",
        "Azure Data Protection payload did not survive provider recreation from the persisted Blob key ring.");

    var wrongAzureNameServices = new ServiceCollection();
    var wrongAzureName = azureOptions with { ApplicationName = "TrailGuard.HostingVerification.Azure.Other" };
    wrongAzureName.Configure(wrongAzureNameServices,
        new FakeAzureDataProtectionRegistration(azureStore, new DeterministicKeyEncryptionKeyResolver(azureKeyResolver.KeyId)));
    var wrongAzureNameProvider = wrongAzureNameServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    var wrongAzureNameRejected = false;
    try
    {
        wrongAzureNameProvider.CreateProtector("azure-hosting-verification").Unprotect(azurePayload);
    }
    catch (CryptographicException)
    {
        wrongAzureNameRejected = true;
    }
    Check(wrongAzureNameRejected, "A different Azure Data Protection application name decrypted a payload.");

    var wrappingFailureServices = new ServiceCollection();
    var wrappingFailureStore = new InMemoryBlobStore();
    azureOptions.Configure(wrappingFailureServices,
        new FakeAzureDataProtectionRegistration(wrappingFailureStore,
            new DeterministicKeyEncryptionKeyResolver(azureKeyResolver.KeyId, throwOnWrap: true)));
    var wrappingFailureProvider = wrappingFailureServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    var wrappingFailureRejected = false;
    try
    {
        wrappingFailureProvider.CreateProtector("azure-hosting-verification").Protect("must-not-fallback");
    }
    catch (CryptographicException)
    {
        wrappingFailureRejected = true;
    }
    Check(wrappingFailureRejected && wrappingFailureStore.Content is null,
        "A Key Vault wrapping failure wrote plaintext or fell back to another provider.");

    var unwrapFailureServices = new ServiceCollection();
    azureOptions.Configure(unwrapFailureServices,
        new FakeAzureDataProtectionRegistration(azureStore,
            new DeterministicKeyEncryptionKeyResolver(azureKeyResolver.KeyId, throwOnUnwrap: true)));
    var unwrapFailureProvider = unwrapFailureServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    var unwrapFailureRejected = false;
    try
    {
        unwrapFailureProvider.CreateProtector("azure-hosting-verification").Unprotect(azurePayload);
    }
    catch (CryptographicException)
    {
        unwrapFailureRejected = true;
    }
    Check(unwrapFailureRejected, "A Key Vault unwrapping failure fell back to plaintext or another provider.");

    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "AzureBlobKeyVault",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure"
    }, environment, uploadStorage, "Incomplete Azure Data Protection configuration was accepted.");
    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "AzureBlobKeyVault",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure",
        ["DataProtection:AzureBlobUri"] = "https://fakeaccount.blob.core.windows.net/data-protection/keyring.xml?sig=not-allowed",
        ["DataProtection:KeyVaultKeyIdentifier"] = "https://fakevault.vault.azure.net/keys/trailguard-data-protection",
        ["DataProtection:ManagedIdentityClientId"] = "11111111-1111-1111-1111-111111111111"
    }, environment, uploadStorage, "A Blob URI with query credentials was accepted.");
    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "AzureBlobKeyVault",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure",
        ["DataProtection:AzureBlobUri"] = "https://fakeaccount.blob.core.windows.net/data-protection/keyring.xml",
        ["DataProtection:KeyVaultKeyIdentifier"] = "https://fakevault.vault.azure.net/keys/trailguard-data-protection/version-not-allowed",
        ["DataProtection:ManagedIdentityClientId"] = "11111111-1111-1111-1111-111111111111"
    }, environment, uploadStorage, "A versioned Key Vault key URI was accepted.");
    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "AzureBlobKeyVault",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure",
        ["DataProtection:KeyRingPath"] = keyRing,
        ["DataProtection:AzureBlobUri"] = "https://fakeaccount.blob.core.windows.net/data-protection/keyring.xml",
        ["DataProtection:KeyVaultKeyIdentifier"] = "https://fakevault.vault.azure.net/keys/trailguard-data-protection",
        ["DataProtection:ManagedIdentityClientId"] = "11111111-1111-1111-1111-111111111111"
    }, environment, uploadStorage, "Conflicting filesystem and Azure Data Protection settings were accepted.");
    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "UnknownProvider",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Azure",
        ["DataProtection:KeyRingPath"] = keyRing
    }, environment, uploadStorage, "An unknown Data Protection provider was accepted.");
    CheckRejectedDataProtectionConfiguration(new Dictionary<string, string?>
    {
        ["DataProtection:Provider"] = "Filesystem",
        ["DataProtection:ApplicationName"] = "TrailGuard.HostingVerification.Filesystem",
        ["DataProtection:KeyRingPath"] = keyRing,
        ["DataProtection:Unexpected"] = "must-be-rejected"
    }, environment, uploadStorage, "An unknown Data Protection provider setting was accepted.");

    var forwardedConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_HTTPS_PORT"] = "443",
            ["ForwardedHeaders:TrustedProxies:0"] = "10.10.0.4"
        })
        .Build();
    var verificationTime = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
    var hostingOptions = TrailGuardHostingOptions.Resolve(forwardedConfiguration, verificationTime);
    Check(hostingOptions.ForwardedHeaders is not null,
        "An explicit trusted proxy did not enable forwarded-header processing.");
    Check(hostingOptions.HttpsRedirectPort == 443,
        "ASPNETCORE_HTTPS_PORT was not loaded through the shared hosting configuration.");

    var openNetworkRejected = false;
    try
    {
        TrustedForwardedHeaders.Resolve(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:TrustedNetworks:0"] = "0.0.0.0/0"
            })
            .Build());
    }
    catch (InvalidOperationException)
    {
        openNetworkRejected = true;
    }
    Check(openNetworkRejected, "A whole-address-family forwarded-header trust boundary was accepted.");

    var unrestrictedSwitchRejected = false;
    try
    {
        TrustedForwardedHeaders.Resolve(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                ["ForwardedHeaders:TrustedProxies:0"] = "10.10.0.4"
            })
            .Build());
    }
    catch (InvalidOperationException)
    {
        unrestrictedSwitchRejected = true;
    }
    Check(unrestrictedSwitchRejected,
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED bypassed the explicit trusted-proxy policy.");

    var productionEnvironment = new TestEnvironment(root, webRoot)
    {
        EnvironmentName = Environments.Production
    };

    // This intentionally reproduces the former order: HSTS registered before forwarded headers.
    // HSTS observes the original HTTP scheme, then forwarding changes the request to HTTPS before
    // redirection, leaving an otherwise successful response without an HSTS header.
    var legacyPipeline = BuildPipeline(productionEnvironment, hostingOptions, forwardedConfiguration, verificationTime, useLegacyOrdering: true);
    var legacyTrustedHttps = await InvokeAsync(legacyPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true);
    Check(legacyTrustedHttps.Response.StatusCode == StatusCodes.Status200OK,
        "The pre-fix pipeline did not reach HTTPS liveness through a trusted proxy.");
    Check(!legacyTrustedHttps.Response.Headers.ContainsKey("Strict-Transport-Security"),
        "The pre-fix HSTS regression was not reproduced.");
    Console.WriteLine("Pre-fix regression reproduced: trusted forwarded HTTPS reached /healthz without Strict-Transport-Security.");

    var productionPipeline = BuildPipeline(productionEnvironment, hostingOptions, forwardedConfiguration, verificationTime, useLegacyOrdering: false);
    var trustedHttps = await InvokeAsync(productionPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true);
    Check(trustedHttps.Response.StatusCode == StatusCodes.Status200OK,
        "Trusted forwarded HTTPS liveness did not return 200.");
    Check(trustedHttps.Response.Headers.ContainsKey("Strict-Transport-Security"),
        "Trusted forwarded HTTPS did not receive Strict-Transport-Security.");
    Check(!IsRedirect(trustedHttps.Response.StatusCode) && !trustedHttps.Response.Headers.ContainsKey("Location"),
        "Trusted forwarded HTTPS was redirected repeatedly.");
    Check(trustedHttps.Response.ContentLength is null or 0 && trustedHttps.Response.Body.Length == 0,
        "HTTPS liveness disclosed a response body.");
    Check(trustedHttps.Items["authentication"] is null,
        "HTTPS liveness ran after authentication.");

    var spoofedHttps = await InvokeAsync(productionPipeline, IPAddress.Parse("203.0.113.10"), forwardedHttps: true);
    Check(IsRedirect(spoofedHttps.Response.StatusCode),
        "An untrusted forwarded HTTPS header was honored instead of being redirected.");
    Check(!spoofedHttps.Response.Headers.ContainsKey("Strict-Transport-Security"),
        "An untrusted forwarded HTTPS header caused HSTS to be emitted.");

    var plainHttp = await InvokeAsync(productionPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: false);
    Check(IsRedirect(plainHttp.Response.StatusCode),
        "Plain HTTP liveness must redirect to HTTPS rather than return liveness success.");
    Check(plainHttp.Response.Headers.Location.ToString() == "https://trailguard.example.test/healthz",
        "Plain HTTP liveness did not redirect to the expected HTTPS host and path.");
    Console.WriteLine("Corrected pipeline passed: trusted HTTPS liveness returned empty 200 with HSTS; plain HTTP liveness redirected to HTTPS.");

    var disabledObservationConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_HTTPS_PORT"] = "443",
            ["ForwardedHeaders:TrustedProxies:0"] = "10.10.0.4",
            ["ProxyHeaderObservation:Enabled"] = "false"
        })
        .Build();
    var disabledObservationOptions = TrailGuardHostingOptions.Resolve(disabledObservationConfiguration, verificationTime);
    Check(disabledObservationOptions.ProxyHeaderObservation is null,
        "Disabled proxy observation was registered.");
    var disabledLogs = new CapturedLoggerProvider();
    var disabledPipeline = BuildPipeline(productionEnvironment, disabledObservationOptions,
        disabledObservationConfiguration, verificationTime, useLegacyOrdering: false, disabledLogs);
    await InvokeAsync(disabledPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true);
    Check(disabledLogs.Messages.Count == 0, "Disabled proxy observation emitted a log event.");

    var expiredObservationConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_HTTPS_PORT"] = "443",
            ["ForwardedHeaders:TrustedProxies:0"] = "10.10.0.4",
            ["ProxyHeaderObservation:Enabled"] = "true",
            ["ProxyHeaderObservation:ExpiresAtUtc"] = "2026-10-01T23:59:59Z",
            ["ProxyHeaderObservation:MaxEventCount"] = "2"
        })
        .Build();
    var expiredObservationOptions = TrailGuardHostingOptions.Resolve(expiredObservationConfiguration, verificationTime);
    var expiredLogs = new CapturedLoggerProvider();
    var expiredPipeline = BuildPipeline(productionEnvironment, expiredObservationOptions,
        expiredObservationConfiguration, verificationTime, useLegacyOrdering: false, expiredLogs);
    await InvokeAsync(expiredPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true);
    Check(expiredLogs.Messages.Count == 0,
        "An expired proxy observation configuration emitted an event after a simulated restart.");

    var observationConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_HTTPS_PORT"] = "443",
            ["ForwardedHeaders:TrustedProxies:0"] = "10.10.0.4",
            ["ProxyHeaderObservation:Enabled"] = "true",
            ["ProxyHeaderObservation:ExpiresAtUtc"] = "2026-10-02T00:30:00Z",
            ["ProxyHeaderObservation:MaxEventCount"] = "1"
        })
        .Build();
    var observationOptions = TrailGuardHostingOptions.Resolve(observationConfiguration, verificationTime);
    var observationLogs = new CapturedLoggerProvider();
    var observationPipeline = BuildPipeline(productionEnvironment, observationOptions,
        observationConfiguration, verificationTime, useLegacyOrdering: false, observationLogs);
    var observedContext = await InvokeAsync(observationPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true,
        forwardedFor: "198.51.100.20, 203.0.113.23", forwardedProto: "https, https",
        queryString: "?access_token=not-for-logs", cookie: "session=not-for-logs", authorization: "Bearer not-for-logs");
    Check(observedContext.Request.Scheme == "https",
        "The trusted forwarded HTTPS header was not transformed after observation.");
    Check(observationLogs.Messages.Count == 1, "Proxy observation did not emit exactly one capped event.");
    var observationMessage = observationLogs.Messages.Single();
    Check(observationMessage.Contains("PeerIp=10.10.0.4", StringComparison.Ordinal)
        && observationMessage.Contains("RequestScheme=http", StringComparison.Ordinal)
        && observationMessage.Contains("XForwardedForCount=2", StringComparison.Ordinal)
        && observationMessage.Contains("XForwardedProtoCount=2", StringComparison.Ordinal)
        && observationMessage.Contains("ForwardedScheme=https", StringComparison.Ordinal),
        "Proxy observation did not capture the sanitized pre-forwarded-header shape.");
    Check(!observationMessage.Contains("198.51.100.20", StringComparison.Ordinal)
        && !observationMessage.Contains("not-for-logs", StringComparison.Ordinal)
        && !observationMessage.Contains("access_token", StringComparison.Ordinal),
        "Proxy observation logged a forwarded-header value, credential, cookie, or query string.");
    await InvokeAsync(observationPipeline, IPAddress.Parse("10.10.0.4"), forwardedHttps: true);
    Check(observationLogs.Messages.Count == 1, "Exhausted proxy observation emitted more than its configured cap.");

    Console.WriteLine($"TrailGuard hosting verification passed ({assertions} assertions).");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static TestPipeline BuildPipeline(
    IWebHostEnvironment environment,
    TrailGuardHostingOptions hostingOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    bool useLegacyOrdering,
    CapturedLoggerProvider? capturedLogs = null)
{
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddLogging(logging =>
    {
        if (capturedLogs is not null)
        {
            logging.ClearProviders();
            logging.AddProvider(capturedLogs);
        }
    });
    serviceCollection.AddMetrics();
    serviceCollection.AddExceptionHandler(_ => { });
    serviceCollection.AddHsts(_ => { });
    hostingOptions.ConfigureServices(serviceCollection);
    serviceCollection.AddSingleton<IServerAddressesFeature>(new ServerAddressesFeature());
    serviceCollection.AddSingleton(configuration);
    serviceCollection.AddSingleton(new DiagnosticListener("TrailGuard.HostingVerification"));
    serviceCollection.AddSingleton(environment);
    serviceCollection.AddSingleton<IHostEnvironment>(environment);
    serviceCollection.AddSingleton<IWebHostEnvironment>(environment);
    var services = serviceCollection.BuildServiceProvider();
    var application = new ApplicationBuilder(services);

    if (useLegacyOrdering)
    {
        application.UseExceptionHandler("/Home/Error");
        application.UseHsts();
        application.UseForwardedHeaders(hostingOptions.ForwardedHeaders!);
        application.UseHttpsRedirection();
        application.UseWhen(TrailGuardLiveness.IsRequest, health => health.Run(TrailGuardLiveness.WriteAsync));
    }
    else
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TrailGuard.HostingVerification");
        TrailGuardHostingPipeline.Configure(application, environment, hostingOptions, logger, timeProvider);
    }

    application.Run(context =>
    {
        context.Items["authentication"] = "would-have-run";
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    });

    return new TestPipeline(application.Build(), services);
}

static async Task<DefaultHttpContext> InvokeAsync(
    TestPipeline pipeline,
    IPAddress remoteAddress,
    bool forwardedHttps,
    string? forwardedFor = null,
    string? forwardedProto = null,
    string? queryString = null,
    string? cookie = null,
    string? authorization = null)
{
    var context = new DefaultHttpContext { RequestServices = pipeline.Services };
    context.Connection.RemoteIpAddress = remoteAddress;
    context.Request.Scheme = "http";
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = "/healthz";
    context.Request.Host = new HostString("trailguard.example.test");
    if (queryString is not null)
    {
        context.Request.QueryString = new QueryString(queryString);
    }
    if (cookie is not null)
    {
        context.Request.Headers.Cookie = cookie;
    }
    if (authorization is not null)
    {
        context.Request.Headers.Authorization = authorization;
    }
    if (forwardedHttps)
    {
        context.Request.Headers["X-Forwarded-For"] = forwardedFor ?? "198.51.100.8";
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProto ?? "https";
    }
    context.Response.Body = new MemoryStream();
    await pipeline.Application(context);
    return context;
}

static bool IsRedirect(int statusCode) => statusCode is >= 300 and < 400;

static void CheckRejectedDataProtectionConfiguration(
    IReadOnlyDictionary<string, string?> values,
    IWebHostEnvironment environment,
    UploadStorageOptions uploadStorage,
    string failureMessage)
{
    try
    {
        HostedDataProtectionOptions.Resolve(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), environment, uploadStorage);
        throw new InvalidOperationException(failureMessage);
    }
    catch (InvalidOperationException exception) when (exception.Message != failureMessage)
    {
    }
}

sealed record TestPipeline(RequestDelegate Application, IServiceProvider Services);

sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

sealed class CapturedLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturedLogger(Messages);

    public void Dispose()
    {
    }

    private sealed class CapturedLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}

sealed class FakeAzureDataProtectionRegistration(
    InMemoryBlobStore store,
    IKeyEncryptionKeyResolver keyResolver) : IAzureDataProtectionRegistration
{
    public void Configure(IDataProtectionBuilder builder, AzureBlobKeyVaultDataProtectionOptions options)
    {
        var client = new BlobClient(
            options.AzureBlobUri,
            new NoNetworkTokenCredential(),
            new BlobClientOptions
            {
                Transport = new HttpClientTransport(new HttpClient(new InMemoryBlobHandler(store)))
            });
        builder.PersistKeysToAzureBlobStorage(client)
            .ProtectKeysWithAzureKeyVault(options.KeyVaultKeyIdentifier, keyResolver);
    }
}

sealed class NoNetworkTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new("offline-test-token", DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

sealed class InMemoryBlobStore
{
    public byte[]? Bytes { get; set; }
    public int RequestCount { get; set; }
    public bool NetworkAttempted { get; set; }
    public string? Content => Bytes is null ? null : Encoding.UTF8.GetString(Bytes);
}

sealed class InMemoryBlobHandler(InMemoryBlobStore store) : HttpMessageHandler
{
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendCoreAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendCoreAsync(request, cancellationToken);

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        store.RequestCount++;
        if (request.RequestUri?.Host != "fakeaccount.blob.core.windows.net")
        {
            store.NetworkAttempted = true;
            throw new InvalidOperationException("The test attempted to access a non-fake Blob endpoint.");
        }

        if (request.Method == HttpMethod.Get)
        {
            if (store.Bytes is null)
            {
                return Response(HttpStatusCode.NotFound);
            }

            var response = Response(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(store.Bytes);
            response.Content.Headers.ContentLength = store.Bytes.Length;
            return response;
        }

        if (request.Method == HttpMethod.Put)
        {
            store.Bytes = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return Response(HttpStatusCode.Created);
        }

        throw new InvalidOperationException("The fake Blob transport received an unexpected request method.");
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.TryAddWithoutValidation("x-ms-request-id", "offline-test-request");
        response.Headers.TryAddWithoutValidation("x-ms-version", "2024-11-04");
        response.Headers.TryAddWithoutValidation("ETag", "\"offline-test-etag\"");
        response.Headers.TryAddWithoutValidation("Last-Modified", "Wed, 01 Oct 2026 00:00:00 GMT");
        return response;
    }
}

sealed class DeterministicKeyEncryptionKeyResolver(string keyId, bool throwOnWrap = false, bool throwOnUnwrap = false)
    : IKeyEncryptionKeyResolver
{
    public string KeyId { get; } = keyId;

    public IKeyEncryptionKey Resolve(string keyId, CancellationToken cancellationToken)
    {
        if (!string.Equals(keyId, KeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unexpected fake Key Vault key identifier.");
        }

        return new DeterministicKeyEncryptionKey(KeyId, throwOnWrap, throwOnUnwrap);
    }

    public Task<IKeyEncryptionKey> ResolveAsync(string keyId, CancellationToken cancellationToken) =>
        Task.FromResult(Resolve(keyId, cancellationToken));
}

sealed class DeterministicKeyEncryptionKey(string keyId, bool throwOnWrap, bool throwOnUnwrap) : IKeyEncryptionKey
{
    public string KeyId { get; } = keyId;

    public byte[] WrapKey(string algorithm, ReadOnlyMemory<byte> key, CancellationToken cancellationToken)
    {
        if (throwOnWrap)
        {
            throw new CryptographicException("Fake Key Vault wrapping failure.");
        }

        return Transform(key.Span);
    }

    public Task<byte[]> WrapKeyAsync(string algorithm, ReadOnlyMemory<byte> key, CancellationToken cancellationToken) =>
        Task.FromResult(WrapKey(algorithm, key, cancellationToken));

    public byte[] UnwrapKey(string algorithm, ReadOnlyMemory<byte> encryptedKey, CancellationToken cancellationToken)
    {
        if (throwOnUnwrap)
        {
            throw new CryptographicException("Fake Key Vault unwrapping failure.");
        }

        return Transform(encryptedKey.Span);
    }

    public Task<byte[]> UnwrapKeyAsync(string algorithm, ReadOnlyMemory<byte> encryptedKey, CancellationToken cancellationToken) =>
        Task.FromResult(UnwrapKey(algorithm, encryptedKey, cancellationToken));

    private static byte[] Transform(ReadOnlySpan<byte> value)
    {
        var transformed = value.ToArray();
        for (var index = 0; index < transformed.Length; index++)
        {
            transformed[index] ^= 0xA5;
        }

        return transformed;
    }
}

sealed class TestEnvironment(string contentRoot, string webRoot) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "TrailGuard.HostingVerification";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = webRoot;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
