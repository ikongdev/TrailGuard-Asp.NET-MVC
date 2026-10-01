using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TrailGuard.Controllers;
using TrailGuard.Models;
using TrailGuard.Services;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
async Task Throws(Func<Task> action, string message)
{
    var thrown = false;
    try { await action(); } catch { thrown = true; }
    Check(thrown, message);
}
var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TrailGuardStorageVerification-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
var env = new TestEnvironment(root);
UploadStorageOptions Options(string provider = "Local", params (string Key, string? Value)[] overrides)
{
    var values = new Dictionary<string, string?> { ["Storage:Namespace"] = "fixture", ["Storage:Provider"] = provider,
        ["Storage:Supabase:Url"] = "https://example.supabase.co", ["Storage:Supabase:SecretKey"] = "sb_secret_fixture_only" };
    foreach (var pair in overrides) values[pair.Key] = pair.Value;
    return UploadStorageOptions.Resolve(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), env);
}
try
{
    foreach (var invalid in new (string, string?)[] { ("Storage:Provider", ""), ("Storage:Provider", "Other"),
        ("Storage:Namespace", null), ("Storage:Namespace", "../other"), ("Storage:Namespace", "Uppercase"), ("Storage:Namespace", "fixture\n"),
        ("Storage:Local:RootPath", root),
        ("Storage:Local:RootPath", Path.Combine(root, "wwwroot", "private")),
        ("Storage:Supabase:Url", "http://example.supabase.co"), ("Storage:Supabase:Url", "https://example.supabase.co/path"),
        ("Storage:Supabase:Url", "https://user@example.supabase.co"), ("Storage:Supabase:Url", "https://example.supabase.co?q=1"),
        ("Storage:Supabase:SecretKey", "not-a-secret-key"), ("Storage:Supabase:PublicImagesBucket", "wrong"),
        ("Storage:Supabase:TimeoutSeconds", "0") })
        await Throws(() => { Options("Supabase", invalid); return Task.CompletedTask; }, "Invalid config accepted: " + invalid.Item1);
    var defaultConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Storage:Namespace"] = "fixture" }).Build();
    Check(UploadStorageOptions.Resolve(defaultConfig, env).Provider == "Local", "Default provider must remain Local.");
    Check(Options("Local", ("Database:Target", "Supabase")).Provider == "Local", "Database target selected storage.");
    Check(Options("Supabase", ("Database:Target", "Local")).Provider == "Supabase", "Database target overrode storage.");

    foreach (var provider in new[] { "Local", "Supabase" })
    {
        var refs = new UploadReferences(Options(provider));
        foreach (var category in Enum.GetValues<UploadCategory>())
        {
            var reference = refs.Create(category, ".png");
            Check(reference.Value.Length <= 300, "Reference exceeds snapshot column.");
            Check(refs.Parse(reference.Value, category) == reference, "Reference did not round trip.");
            Check(new UploadReferences(Options(provider == "Local" ? "Supabase" : "Local")).Parse(reference.Value, category) == reference,
                "Switching new-upload provider changed reference semantics.");
            foreach (var bad in new[] { reference.Value + "\n", reference.Value + "?x=1", reference.Value + "#x", reference.Value.Replace("fixture/", "other/"),
                reference.Value.Replace("fixture/", "fixture/../"), reference.Value.Replace("fixture/", "fixture%2f"),
                reference.Value.Replace(".png", ".svg"), reference.Value.Replace(".png", ".png/child"),
                reference.Value.Replace("fixture/", "fixture\\"), reference.Value.Replace("fixture/", "fixture//") })
                Check(refs.Parse(bad, category) == null, "Unsafe reference accepted.");
            foreach (var other in Enum.GetValues<UploadCategory>().Where(c => c != category))
                Check(refs.Parse(reference.Value, other) == null, "Category isolation failed.");
        }
        var publicRef = refs.Create(UploadCategory.Trails, ".jpg");
        Check(refs.Parse(publicRef.Value.Replace("example.supabase.co", "attacker.test"), UploadCategory.Trails) == (provider == "Local" ? publicRef : null), "Host allowlist failed.");
    }

    var opts = Options("Supabase");
    var references = new UploadReferences(opts);
    var privateRef = references.Create(UploadCategory.Receipts, ".pdf");
    var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 fixture only");
    var handler = new RecordingHandler { Response = () => new(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) } };
    using var client = new HttpClient(handler);
    var remote = new SupabaseFileStore(client, opts, references);
    await remote.UploadAsync(privateRef, pdf, "application/pdf", default);
    Check(handler.LastMethod == HttpMethod.Post && handler.LastPath!.StartsWith("/storage/v1/object/trailguard-private-documents/fixture/receipts/"), "Wrong REST upload route.");
    Check(handler.HasApiKey && !handler.HasAuthorization && handler.Upsert == "false", "Opaque-key authentication or immutable upload incorrect.");
    Check((await remote.ReadAsync(privateRef, default))!.SequenceEqual(pdf), "Remote document changed.");
    Check(handler.LastPath!.Contains("/object/authenticated/"), "Private download route incorrect.");
    await remote.DeleteAsync(privateRef, default);
    Check(handler.LastMethod == HttpMethod.Delete && handler.LastBody!.Contains(privateRef.Key), "Delete must target exactly this key.");
    using (var transport = (HttpClientHandler)SupabaseFileStore.CreateHandler())
        Check(!transport.AllowAutoRedirect && !transport.UseCookies, "Credentials could follow redirects/cookies.");
    foreach (var status in new[] { HttpStatusCode.Found, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
    {
        handler.Response = () => new(status) { Headers = { Location = new Uri("https://attacker.test") }, Content = new StringContent("sensitive provider response") };
        var previousCalls = handler.Calls;
        await Throws(() => remote.ReadAsync(privateRef, default), "Provider error was hidden.");
        Check(handler.Calls == previousCalls + 1, "Provider error retried or redirected.");
    }
    handler.Response = () => new(HttpStatusCode.NotFound);
    Check(await remote.ReadAsync(privateRef, default) == null, "Missing object must be unavailable.");
    var calls = handler.Calls;
    await Throws(() => remote.ReadAsync(privateRef with { Value = "https://attacker.test/object" }, default), "Untrusted locator reached HTTP.");
    Check(handler.Calls == calls, "Invalid locator sent credentials.");
    handler.Response = () => throw new TaskCanceledException();
    var cloudStorage = new UploadStorage(opts, references, remote, NullLogger<UploadStorage>.Instance);
    Check(await cloudStorage.ReadDocumentAsync(RegistrationDocumentKind.Receipt, privateRef.Value) == null, "Outage must be unavailable.");
    await Throws(() => cloudStorage.UploadAsync(UploadCategory.Receipts, pdf, ".pdf"), "Cloud failure fell back to local.");
    Check(!Directory.Exists(opts.LocalRoot), "Cloud failure wrote a local upload.");

    var localOptions = Options();
    var localRefs = new UploadReferences(localOptions);
    var local = new UploadStorage(localOptions, localRefs, remote, NullLogger<UploadStorage>.Instance);
    var localReference = await local.UploadAsync(UploadCategory.Receipts, pdf, ".pdf");
    Check(localReference.StartsWith("local:v1/fixture/receipts/"), "Private local locator incorrect.");
    Check(!localRefs.LocalPath(localRefs.Parse(localReference, UploadCategory.Receipts)!).StartsWith(env.WebRootPath), "Private file in web root.");
    Check((await local.ReadDocumentAsync(RegistrationDocumentKind.Receipt, localReference))?.Type == VerifiedFileType.Pdf, "Local document unavailable.");
    Check(await local.ReadPublicLocalAsync(localReference) == null, "Private object served by public endpoint.");
    Check(await local.ReadDocumentAsync(RegistrationDocumentKind.Clearance, localReference) == null, "Cross-category document read.");
    var legacyPath = Path.Combine(env.WebRootPath, "uploads", "receipts", "legacy.pdf");
    Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
    await File.WriteAllBytesAsync(legacyPath, pdf);
    Check(await local.ReadDocumentAsync(RegistrationDocumentKind.Receipt, "/uploads/receipts/legacy.pdf") != null, "Legacy read lost.");
    await local.DeleteOwnedAsync(UploadCategory.Receipts, "/uploads/receipts/legacy.pdf");
    Check(File.Exists(legacyPath), "Legacy file deleted.");
    Check(await local.ReadDocumentAsync(RegistrationDocumentKind.Receipt, "/uploads/receipts/../legacy.pdf") == null, "Legacy traversal accepted.");
    Check(await local.ReadDocumentAsync(RegistrationDocumentKind.Receipt, "/uploads/receipts/missing.pdf") == null, "Missing legacy file not unavailable.");
    await local.DeleteOwnedAsync(UploadCategory.Receipts, localReference);
    Check(await local.ReadDocumentAsync(RegistrationDocumentKind.Receipt, localReference) == null, "Managed deletion failed.");
    var display = new PublicImageDisplay(localOptions, localRefs);
    Check(display.Url("/images/profiles/missing.jpg") == null && display.Url("https://attacker.test/photo.jpg") == null, "Missing/unsafe image failed to select fallback.");
    Check(display.Url(references.Create(UploadCategory.Trails, ".png").Value) != null, "Cloud image hidden after provider switch.");

    IFormFile Form(byte[] bytes, string filename, long? length = null) => new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "upload", filename);
    Check(await DocumentUploadValidator.ValidateAsync(Form(pdf, "clearance.pdf")) == VerifiedFileType.Pdf, "PDF rejected.");
    Check(await DocumentUploadValidator.ValidateAsync(Form(pdf, "clearance.png")) == null, "Mismatched signature accepted.");
    Check(await DocumentUploadValidator.ValidateAsync(Form(pdf, "clearance.pdf", UploadStorageOptions.MaxFileBytes + 1L)) == null, "Oversized document accepted.");
    Check(await DocumentUploadValidator.ValidateAsync(Form([], "empty.pdf")) == null, "Empty document accepted.");
    foreach (var sample in new (byte[] Bytes, string Name, VerifiedFileType Type)[] {
        ([255, 216, 255], "old.jfif", VerifiedFileType.Jpeg),
        ([137, 80, 78, 71, 13, 10, 26, 10], "photo.png", VerifiedFileType.Png),
        (System.Text.Encoding.ASCII.GetBytes("RIFF1234WEBP"), "photo.webp", VerifiedFileType.Webp) })
        Check(await DocumentUploadValidator.ValidateAsync(Form(sample.Bytes, sample.Name)) == sample.Type, "Existing document format lost.");
    await Throws(() => UploadBytes.ReadAsync(new MemoryStream(new byte[UploadStorageOptions.MaxFileBytes + 1])), "Bounded read exceeded limit.");
    using (var picture = new Image<Rgba32>(2, 2))
    {
        using var buffer = new MemoryStream();
        picture.SaveAsPng(buffer);
        Check((await TrailImageUploadValidator.ValidateAsync(Form(buffer.ToArray(), "../../bad.exe"))).Image?.Extension == ".png", "Decoded format must determine image extension.");
    }
    Check((await TrailImageUploadValidator.ValidateAsync(Form([0x89, 0x50, 0x4e, 0x47], "fake.png"))).Image == null, "Corrupt trail image accepted.");

    Check(typeof(DocumentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length == 1, "Document controller lost authorization filter.");
    var registration = new EventRegistration { Id = 1, UserId = "owner", Event = new Event { OrganizerId = "organizer" }, PaymentReceiptUrl = localReference };
    using var mvcServices = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
    foreach (var identity in new string?[] { null, "owner", "organizer", "stranger", "admin" })
    foreach (var download in new[] { false, true })
    {
        var fake = new FakeStorage();
        var controller = new TestDocuments(fake, identity == null ? null : new ApplicationUser { Id = identity }, registration);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.RequestServices = mvcServices;
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, identity == "admin" ? "Admin" : "Participant")], "fixture"));
        var result = download ? await controller.RegistrationDownload(1, "receipt") : await controller.Registration(1, "receipt");
        var allowed = identity is "owner" or "organizer";
        Check(fake.Reads == (allowed ? 1 : 0), "Unauthorized identity reached private storage: " + identity);
        if (allowed)
        {
            Check(result is FileStreamResult { EnableRangeProcessing: true, ContentType: "application/pdf" }, "Document result/range behavior changed.");
            Check(controller.Response.Headers.CacheControl == "private, no-store" && controller.Response.Headers["X-Content-Type-Options"] == "nosniff"
                && controller.Response.Headers["Content-Security-Policy"] == "default-src 'none'" && controller.Response.Headers["X-Frame-Options"] == "DENY", "Document security headers missing.");
            Check(controller.Response.Headers.ContentDisposition.ToString().StartsWith(download ? "attachment" : "inline"), "Disposition incorrect.");
            controller.Request.Method = "GET";
            controller.Request.Headers.Range = "bytes=0-1";
            controller.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(controller.ControllerContext);
            Check(controller.Response.StatusCode == 206 && controller.Response.Headers.ContentRange == "bytes 0-1/5"
                && controller.Response.Body.Length == 2, "Private byte-range download failed.");
        }
        else Check(result is NotFoundResult, "Denied document must be indistinguishable from missing.");
    }

    foreach (var outcome in new[] { "before-save", "rollback", "uncertain", "committed", "cleanup-failure" })
    {
        var fake = new FakeStorage { FailDelete = outcome == "cleanup-failure" };
        await using (var attempt = new UploadAttempt(fake, NullLogger.Instance))
        {
            await attempt.UploadAsync(UploadCategory.Profiles, pdf, ".pdf");
            if (outcome != "before-save") attempt.PersistenceStarted();
            if (outcome is "rollback" or "cleanup-failure") attempt.RollbackConfirmed();
            if (outcome == "committed") attempt.Committed();
        }
        Check(fake.Deleted.Count == (outcome is "uncertain" or "committed" ? 0 : 1), "Wrong compensation: " + outcome);
    }
    {
        var fake = new FakeStorage { FailUploadNumber = 2 };
        await using (var attempt = new UploadAttempt(fake, NullLogger.Instance))
        {
            await attempt.UploadAsync(UploadCategory.Trails, pdf, ".pdf");
            await Throws(() => attempt.UploadAsync(UploadCategory.Trails, pdf, ".pdf"), "Partial batch failure hidden.");
        }
        Check(fake.Deleted.Count == 1, "Partial batch did not compensate exactly its successful upload.");
    }
    foreach (var phase in new[] { "save", "commit", "rollback" })
    {
        var fake = new FakeStorage();
        var tx = new FakeTransaction { FailRollback = phase == "rollback", FailCommit = phase == "commit" };
        await using (var attempt = new UploadAttempt(fake, NullLogger.Instance))
        {
            await attempt.UploadAsync(UploadCategory.Receipts, pdf, ".pdf");
            await Throws(() => UploadPersistence.CommitAsync(new FakeDatabase(tx), attempt,
                () => phase == "commit" ? Task.CompletedTask : Task.FromException(new IOException("database failed"))), "Database failure suppressed.");
        }
        Check(fake.Deleted.Count == (phase == "save" ? 1 : 0), "Uncertain database outcome deleted an object.");
    }
    foreach (var phase in new[] { "save", "rollback", "commit", "success" })
    {
        var fake = new FakeStorage();
        var logger = new RecordingLogger();
        var saveFailure = new IOException("Original save failure");
        var tx = new FakeTransaction { FailDispose = true, FailRollback = phase == "rollback", FailCommit = phase == "commit" };
        Exception? observed = null;
        string uploaded;
        await using (var attempt = new UploadAttempt(fake, logger))
        {
            uploaded = await attempt.UploadAsync(UploadCategory.Receipts, pdf, ".pdf");
            try
            {
                await UploadPersistence.CommitAsync(new FakeDatabase(tx), attempt,
                    () => phase is "save" or "rollback" ? Task.FromException(saveFailure) : Task.CompletedTask);
            }
            catch (Exception ex) { observed = ex; }
        }
        Check(ReferenceEquals(observed, phase switch { "save" or "rollback" => saveFailure, "commit" => tx.CommitFailure, _ => null }),
            "Disposal replaced the save/commit outcome: " + phase);
        Check(tx.DisposeCalls == 1, "Transaction disposal was not attempted exactly once: " + phase);
        Check(tx.RollbackCalls == (phase is "save" or "rollback" ? 1 : 0), "Disposal changed rollback classification: " + phase);
        Check(tx.CommitCalls == (phase is "commit" or "success" ? 1 : 0), "Unexpected commit attempt: " + phase);
        Check(fake.Deleted.SequenceEqual(phase == "save" ? new[] { uploaded } : Array.Empty<string>()),
            "Disposal changed upload compensation: " + phase);
        Check(logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message == "Upload transaction disposal failed; persistence outcome is unchanged.") == 1,
            "Disposal failure was not logged safely: " + phase);
        Check(logger.Entries.All(e => e.Exception == null && !e.Message.Contains(FakeTransaction.DisposalDetail)),
            "Disposal exception details leaked to logs.");
    }
    // Exercise the controllers' try/finally return pattern without executing their database queries/locks.
    foreach (var stagedUpload in new[] { false, true })
    foreach (IActionResult configuredResult in new IActionResult[] {
        new RedirectToActionResult("Register", null, new { eventId = 1, assessmentId = 2 }),
        new JsonResult(new { success = false, message = "Registration changed." }) })
    {
        var fake = new FakeStorage();
        var logger = new RecordingLogger();
        var tx = new FakeTransaction { FailDispose = true };
        async Task<IActionResult> ReturnEarlyAsync()
        {
            await using var attempt = new UploadAttempt(fake, logger);
            try
            {
                if (stagedUpload) await attempt.UploadAsync(UploadCategory.Receipts, pdf, ".pdf");
                return configuredResult;
            }
            finally { await attempt.DisposeTransactionAsync(tx); }
        }
        Check(ReferenceEquals(await ReturnEarlyAsync(), configuredResult), "Disposal replaced an early action result.");
        Check(tx.DisposeCalls == 1 && tx.CommitCalls == 0 && tx.RollbackCalls == 0, "Early return skipped disposal or changed transaction outcome.");
        Check(fake.Deleted.Count == (stagedUpload ? 1 : 0), "Early return lost pre-persistence compensation.");
        Check(logger.Entries is [{ Level: LogLevel.Warning, Exception: null }]
            && !logger.Entries[0].Message.Contains(FakeTransaction.DisposalDetail), "Early-return disposal was not logged safely.");
    }
    // Two in-flight replacements captured the same original reference; simulate the authoritative
    // database concurrency check rejecting the second after the first has committed.
    {
        var fake = new FakeStorage();
        var current = "old";
        await using var winner = new UploadAttempt(fake, NullLogger.Instance);
        await using (var loser = new UploadAttempt(fake, NullLogger.Instance))
        {
            var a = await winner.UploadAsync(UploadCategory.Profiles, pdf, ".pdf");
            var b = await loser.UploadAsync(UploadCategory.Profiles, pdf, ".pdf");
            await UploadPersistence.CommitAsync(new FakeDatabase(new()), winner, () => { current = a; return Task.CompletedTask; });
            await Throws(() => UploadPersistence.CommitAsync(new FakeDatabase(new()), loser, () =>
                current != "old" ? Task.FromException(new DbUpdateConcurrencyException()) : Task.CompletedTask), "Stale replacement succeeded.");
            await loser.DeleteReplacedAsync(UploadCategory.Profiles, current);
            Check(!fake.Deleted.Contains(current), "Loser deleted winner before compensation.");
        }
        Check(fake.Deleted.Count == 1 && !fake.Deleted.Contains(current), "Compensation deleted the winner.");
        await winner.DeleteReplacedAsync(UploadCategory.Trails, "shared-cover");
        Check(!fake.Deleted.Contains("shared-cover"), "Shared cover deleted.");
    }
    Console.WriteLine($"PASS: {checks} database-free storage checks. No network or database connections.");
}
finally
{
    // This test owns only this newly generated fixture tree.
    if (root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(root).StartsWith("TrailGuardStorageVerification-", StringComparison.Ordinal)) Directory.Delete(root, true);
}

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "StorageVerification";
    public string EnvironmentName { get; set; } = "Verification";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class RecordingHandler : HttpMessageHandler
{
    public required Func<HttpResponseMessage> Response { get; set; }
    public int Calls; public HttpMethod? LastMethod; public string? LastPath, LastBody, Upsert; public bool HasApiKey, HasAuthorization;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; LastMethod = request.Method; LastPath = request.RequestUri!.AbsolutePath;
        HasApiKey = request.Headers.Contains("apikey"); HasAuthorization = request.Headers.Contains("Authorization");
        Upsert = request.Headers.TryGetValues("x-upsert", out var values) ? values.Single() : null;
        LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return Response();
    }
}
sealed class FakeStorage : IUploadStorage
{
    public List<string> Deleted = []; public int Reads, Uploads, FailUploadNumber; public bool FailDelete;
    public Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default)
        => ++Uploads == FailUploadNumber ? Task.FromException<string>(new IOException("upload failed")) : Task.FromResult(Guid.NewGuid().ToString());
    public Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default)
    { Reads++; return Task.FromResult<StoredDocument?>(new([37, 80, 68, 70, 45], VerifiedFileType.Pdf)); }
    public Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
    public Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default)
    { Deleted.Add(reference); return FailDelete ? Task.FromException(new IOException("cleanup failed")) : Task.CompletedTask; }
}
sealed class TestDocuments(FakeStorage storage, ApplicationUser? user, EventRegistration registration)
    : DocumentsController(null!, null!, storage, NullLogger<DocumentsController>.Instance)
{
    protected override Task<ApplicationUser?> FindCurrentUserAsync() => Task.FromResult(user);
    protected override Task<EventRegistration?> FindRegistrationAsync(int id) => Task.FromResult<EventRegistration?>(registration);
}
sealed class FakeDatabase(FakeTransaction transaction) : DatabaseFacade(new DbContext(new DbContextOptions<DbContext>()))
{
    public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IDbContextTransaction>(transaction);
}
sealed class FakeTransaction : IDbContextTransaction
{
    public const string DisposalDetail = "Synthetic sensitive disposal detail";
    public bool FailCommit, FailRollback, FailDispose;
    public int CommitCalls, RollbackCalls, DisposeCalls;
    public IOException CommitFailure { get; } = new("Lost commit acknowledgement");
    public Guid TransactionId { get; } = Guid.NewGuid();
    public void Commit() { CommitCalls++; if (FailCommit) throw CommitFailure; }
    public Task CommitAsync(CancellationToken cancellationToken = default) { Commit(); return Task.CompletedTask; }
    public void Rollback() { RollbackCalls++; if (FailRollback) throw new IOException("Lost rollback acknowledgement"); }
    public Task RollbackAsync(CancellationToken cancellationToken = default) { Rollback(); return Task.CompletedTask; }
    public void Dispose() { }
    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return FailDispose ? ValueTask.FromException(new IOException(DisposalDetail)) : ValueTask.CompletedTask;
    }
}
sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}
