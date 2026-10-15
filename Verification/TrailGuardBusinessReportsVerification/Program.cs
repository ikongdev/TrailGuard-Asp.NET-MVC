using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using TrailGuard.Controllers;
using TrailGuard.Models;
using TrailGuard.Services;

var assertions = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }

var options = VerificationOptions.Parse(args);
var filter = new BusinessReportFilter { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), TrailId = 1, OrganizerId = "org-a" };
var events = new[]
{
    new BusinessReportEvent { Id = 1, TrailId = 1, TrailNameSnapshot = "Mt. Ñido", EventTitle = "Long Filipino name event", EventDate = new DateTime(2026, 10, 10), Status = "Completed", Capacity = 2, OrganizerId = "org-a", OrganizerName = "Aira Santos" },
    new BusinessReportEvent { Id = 2, TrailId = 1, TrailNameSnapshot = "Mt. Ñido", EventTitle = "Cancelled event", EventDate = new DateTime(2026, 10, 12), Status = "Cancelled", Capacity = 8, OrganizerId = "org-a", OrganizerName = "Aira Santos" },
    new BusinessReportEvent { Id = 3, TrailId = 1, TrailNameSnapshot = "Mt. Ñido", EventTitle = "Upcoming zero-capacity", EventDate = new DateTime(2026, 10, 28), Status = "Upcoming", Capacity = 0, OrganizerId = "org-a", OrganizerName = null },
    new BusinessReportEvent { Id = 4, TrailId = 2, TrailNameSnapshot = "Other", EventTitle = "Different scope", EventDate = new DateTime(2026, 10, 8), Status = "Completed", Capacity = 4, OrganizerId = "org-b", OrganizerName = "Ben Reyes" }
};
var registrations = new[]
{
    new BusinessReportRegistration { Id = 1, EventId = 1, UserId = "voided", RegisteredAt = new DateTime(2026, 10, 1, 8, 0, 0), Status = "Accepted", IsPaid = true },
    new BusinessReportRegistration { Id = 2, EventId = 1, UserId = "voided", RegisteredAt = new DateTime(2026, 10, 2, 8, 0, 0), Status = "Voided", IsPaid = false },
    new BusinessReportRegistration { Id = 3, EventId = 1, UserId = "tie", RegisteredAt = new DateTime(2026, 10, 3, 8, 0, 0), Status = "Pending", IsPaid = false },
    new BusinessReportRegistration { Id = 4, EventId = 1, UserId = "tie", RegisteredAt = new DateTime(2026, 10, 3, 8, 0, 0), Status = "Awaiting Payment", IsPaid = false },
    new BusinessReportRegistration { Id = 5, EventId = 2, UserId = "cancelled-event", RegisteredAt = new DateTime(2026, 10, 4, 8, 0, 0), Status = "Accepted", IsPaid = true },
    new BusinessReportRegistration { Id = 6, EventId = 3, UserId = "verify", RegisteredAt = new DateTime(2026, 10, 5, 8, 0, 0), Status = "For Payment Verification", IsPaid = false },
    new BusinessReportRegistration { Id = 7, EventId = 3, UserId = "legacy", RegisteredAt = new DateTime(2026, 10, 6, 8, 0, 0), Status = "Legacy Hold", IsPaid = false },
    new BusinessReportRegistration { Id = 8, EventId = 1, UserId = "cancelled-latest", RegisteredAt = new DateTime(2026, 10, 1, 7, 0, 0), Status = "Accepted", IsPaid = true },
    new BusinessReportRegistration { Id = 9, EventId = 1, UserId = "cancelled-latest", RegisteredAt = new DateTime(2026, 10, 7, 7, 0, 0), Status = "Cancelled", IsPaid = false },
    new BusinessReportRegistration { Id = 10, EventId = 1, UserId = "rejected-latest", RegisteredAt = new DateTime(2026, 10, 1, 6, 0, 0), Status = "Accepted", IsPaid = true },
    new BusinessReportRegistration { Id = 11, EventId = 1, UserId = "rejected-latest", RegisteredAt = new DateTime(2026, 10, 8, 6, 0, 0), Status = "Rejected", IsPaid = false }
};

var eventMatches = BusinessReportFilterPolicy.EventCohortPredicate(filter).Compile();
var submissionScopeMatches = BusinessReportFilterPolicy.SubmissionEventScopePredicate(filter).Compile();
var submissionTimeMatches = BusinessReportFilterPolicy.SubmissionTimestampPredicate(filter).Compile();
var entityEvents = events.Select(eventItem => new Event { Id = eventItem.Id, EventDate = eventItem.EventDate, TrailId = eventItem.TrailId, OrganizerId = eventItem.OrganizerId }).ToList();
var entityRegistrations = registrations.Select(registration => new EventRegistration { EventId = registration.EventId, RegisteredAt = registration.RegisteredAt }).ToList();
var scopedEvents = events.Where((eventItem, index) => eventMatches(entityEvents[index])).ToList();
var scopedSubmissions = registrations.Where((registration, index) => submissionTimeMatches(entityRegistrations[index]) && submissionScopeMatches(entityEvents.Single(eventItem => eventItem.Id == registration.EventId))).ToList();
var report = BusinessReportCalculator.Build(filter, scopedEvents, registrations, scopedSubmissions, new DateOnly(2026, 10, 16), new DateTime(2026, 10, 16, 9, 0, 0));

Check(report.AcceptedRegistrations == 1, "Later Voided, Cancelled, and Rejected attempts must not resurrect earlier Accepted attempts.");
Check(report.UniqueRegisteredParticipants == 3, "Unique participant count must use distinct users with a latest active status, including retained historical attempts on cancelled events.");
Check(report.StatusBreakdown.Single(x => x.Status == "Voided").Count == 1 && report.StatusBreakdown.Single(x => x.Status == "Cancelled").Count == 1, "All persisted statuses, including latest Cancelled and Voided, must be shown.");
Check(report.EventPerformance.Single(x => x.EventId == 1).ActiveOccupiedSlots == 1, "Cancelled and Voided attempts on a non-cancelled event must not consume active slots.");
Check(report.EventPerformance.Single(x => x.EventId == 2).ActiveOccupiedSlots == 0, "Cancelled events must not consume active slots.");
Check(report.EventPerformance.Single(x => x.EventId == 3).OccupancyPercent is null, "Zero capacity must not silently produce an occupancy percentage.");
Check(report.ClosedEventCompletionRate == 50, "Closed-event completion rate must use Completed divided by Completed plus Cancelled.");
Check(report.TrailPerformance.Single().AverageAcceptedPerCompletedEvent == 0, "Completed events with zero accepted latest attempts must remain in the average denominator.");
Check(report.StatusBreakdown.Any(x => x.IsUnexpected && x.Status == "Unexpected: Legacy Hold"), "Unexpected legacy statuses must be explicit.");
Check(report.AwaitingPayment == 1 && report.AwaitingPaymentVerification == 1 && report.VerifiedPaid == 1, "Payment workflow counts must use consistent latest attempt definitions.");
Check(report.EventPerformance.Single(x => x.EventId == 3).Organizer == "Organizer unavailable", "Missing organizer references must be explicit.");
Check(report.SubmissionTrend.Count == 31 && report.SubmissionTrend.First().Label == "Oct 1" && report.SubmissionTrend.First().Count == 3, "A single calendar month must use filled daily RegisteredAt buckets.");
Check(BusinessReportCalculator.TrailLabel(99, "") == "Trail #99 (snapshot unavailable)", "Missing frozen trail names must be explicit.");

var noClosed = BusinessReportCalculator.Build(filter, [events[2]], [], [], new DateOnly(2026, 10, 16), DateTime.Now);
Check(noClosed.ClosedEventCompletionRate is null, "A zero closed-event denominator must remain explicit.");
var longer = BusinessReportCalculator.Build(new BusinessReportFilter { StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 10, 31) }, [], [], registrations, new DateOnly(2026, 10, 16), DateTime.Now);
Check(longer.SubmissionTrend.Select(x => x.Label).SequenceEqual(["Sep 2026", "Oct 2026"]), "Longer periods must use filled monthly submission buckets.");

var defaulted = BusinessReportFilterPolicy.ApplyDefaults(new BusinessReportFilter(), new DateOnly(2026, 10, 16));
Check(defaulted.StartDate == new DateOnly(2026, 10, 1) && defaulted.EndDate == new DateOnly(2026, 10, 31), "PhilippineClock's supplied calendar date must define the default month.");
Check(BusinessReportFilterPolicy.ValidateDates(defaulted, true).Single() == "Enter valid calendar dates.", "Malformed date binding must be rejected by shared filter validation.");
Check(BusinessReportFilterPolicy.ValidateDates(new BusinessReportFilter { StartDate = new DateOnly(2026, 10, 2), EndDate = new DateOnly(2026, 10, 1) }, false).Single() == "The end date must not be before the start date.", "Reversed dates must be rejected by shared filter validation.");
var boundaryFilter = new BusinessReportFilter { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), TrailId = 1, OrganizerId = "org-a" };
var eventBoundary = BusinessReportFilterPolicy.EventCohortPredicate(boundaryFilter).Compile();
var registrationBoundary = BusinessReportFilterPolicy.SubmissionTimestampPredicate(boundaryFilter).Compile();
Check(eventBoundary(new Event { EventDate = new DateTime(2026, 10, 1, 0, 0, 0), TrailId = 1, OrganizerId = "org-a" }) && eventBoundary(new Event { EventDate = new DateTime(2026, 10, 31, 23, 59, 59), TrailId = 1, OrganizerId = "org-a" }) && !eventBoundary(new Event { EventDate = new DateTime(2026, 11, 1), TrailId = 1, OrganizerId = "org-a" }), "The shared event-cohort predicate must include both calendar endpoints and exclude the next midnight.");
Check(registrationBoundary(new EventRegistration { RegisteredAt = new DateTime(2026, 10, 1, 0, 0, 0) }) && registrationBoundary(new EventRegistration { RegisteredAt = new DateTime(2026, 10, 31, 23, 59, 59) }) && !registrationBoundary(new EventRegistration { RegisteredAt = new DateTime(2026, 11, 1) }), "The shared RegisteredAt predicate must use inclusive start and exclusive next-midnight boundaries.");
Check(!eventBoundary(new Event { EventDate = new DateTime(2026, 10, 10), TrailId = 2, OrganizerId = "org-a" }) && !eventBoundary(new Event { EventDate = new DateTime(2026, 10, 10), TrailId = 1, OrganizerId = "org-b" }), "The shared event-cohort predicate must apply trail and organizer scope together.");

var chartEvents = Enumerable.Range(1, 10).Select(index => new BusinessReportEvent { Id = 100 + index, TrailId = index, TrailNameSnapshot = $"Trail {index:00}", EventTitle = $"Chart event {index}", EventDate = new DateTime(2026, 10, index), Status = "Completed", Capacity = 20, OrganizerId = "org-a", OrganizerName = "Aira Santos" }).ToList();
var chartRegistrations = chartEvents.SelectMany((eventItem, index) => Enumerable.Range(1, 10 - index).Select(number => new BusinessReportRegistration { Id = 1000 + index * 20 + number, EventId = eventItem.Id, UserId = $"chart-{index}-{number}", RegisteredAt = new DateTime(2026, 10, 1, 12, 0, 0), Status = "Accepted", IsPaid = true })).ToList();
var chartReport = BusinessReportCalculator.Build(filter, chartEvents, chartRegistrations, chartRegistrations, new DateOnly(2026, 10, 16), DateTime.Now);
var reversedChartReport = BusinessReportCalculator.Build(filter, chartEvents.AsEnumerable().Reverse(), chartRegistrations.AsEnumerable().Reverse(), chartRegistrations, new DateOnly(2026, 10, 16), DateTime.Now);
Check(chartReport.TrailChart.IsAggregated && chartReport.TrailChart.Entries.Count == 9 && chartReport.TrailChart.Entries[^1].IsOther && chartReport.TrailChart.OmittedTrailCount == 2, "More than eight trails must render as deterministic Top 8 plus Other.");
Check(chartReport.TrailChart.Entries.Sum(entry => entry.Count) == chartReport.TrailPerformance.Sum(row => row.AcceptedRegistrations) && chartReport.TrailChart.FullScopeAcceptedRegistrations == chartReport.AcceptedRegistrations, "Shared chart values must reconcile with the complete trail table and report total.");
Check(chartReport.TrailChart.Entries.Select(entry => $"{entry.Label}:{entry.Count}").SequenceEqual(reversedChartReport.TrailChart.Entries.Select(entry => $"{entry.Label}:{entry.Count}")), "Trail-chart aggregation must be deterministic regardless of source ordering.");

var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory()));
var renderer = new BusinessReportPdfRenderer(new TestEnvironment(root));
using var face = SKTypeface.FromFile(Path.Combine(root, "wwwroot", "css", "fonts", "Poppins-Regular.ttf")) ?? throw new InvalidOperationException("Poppins Regular is unavailable.");
using var font = new SKFont(face, 7.2f);
var oversized = string.Concat(Enumerable.Repeat("Ñ漢🙂", 700));
var wrapped = BusinessReportPdfTextLayout.Wrap(oversized, face, 7.2f, 68);
var preparedStressToken = BusinessReportPdfTextLayout.PrepareForTypeface("Ñ漢🙂", face, 7.2f);
var preparedFilipinoName = BusinessReportPdfTextLayout.PrepareForTypeface("Jose\u0301 Niño, Ma. Luz - “Lakbay”", face, 7.2f);
Check(preparedStressToken == "Ñ[U+6F22][U+1F642]" && string.Concat(wrapped) == string.Concat(Enumerable.Repeat(preparedStressToken, 700)) && wrapped.Count > 30 && wrapped.All(line => font.MeasureText(line) <= 68), "Unicode-safe wrapping must preserve supported Ñ and explicitly represent unsupported Han and emoji code points without missing-glyph boxes.");
Check(preparedFilipinoName == "José Niño, Ma. Luz - “Lakbay”", "Composed and decomposed Filipino accents and common punctuation must remain readable Poppins text.");
var pdf = renderer.Render(report);
Check(pdf.Length > 500 && Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF", "PDF export must be generated from the same computed report object.");
var multiPageEvents = Enumerable.Range(10, 18).Select(id => new BusinessReportEvent
{
    Id = id, TrailId = id % 10 + 1, TrailNameSnapshot = $"Mt. Ñido {id % 10} with a long Filipino historical trail name",
    EventTitle = id == 10 ? oversized : $"Long fictional operational event title {id} for table page-break verification", EventDate = new DateTime(2026, 10, 1).AddDays(id % 28),
    Status = id % 3 == 0 ? "Cancelled" : id % 2 == 0 ? "Completed" : "Upcoming", Capacity = 12, OrganizerId = "org-a", OrganizerName = id == 10 ? oversized : "José Niño"
}).ToList();
var workflowStatuses = new[] { "Pending", "Awaiting Payment", "For Payment Verification", "Rejected", "Alternative Recommended", "Cancelled", "Voided" };
var multiPageRegistrations = new List<BusinessReportRegistration>();
var registrationId = 3000;
for (var eventIndex = 0; eventIndex < multiPageEvents.Count; eventIndex++)
{
    var eventItem = multiPageEvents[eventIndex];
    for (var acceptedIndex = 0; acceptedIndex < 10 - eventIndex % 9; acceptedIndex++)
    {
        multiPageRegistrations.Add(new BusinessReportRegistration
        {
            Id = registrationId++, EventId = eventItem.Id, UserId = $"accepted-{eventItem.Id}-{acceptedIndex}",
            RegisteredAt = new DateTime(2026, 10, 1).AddDays((eventIndex + acceptedIndex) % 24).AddHours(8 + acceptedIndex % 8),
            Status = "Accepted", IsPaid = acceptedIndex % 2 == 0
        });
    }
    multiPageRegistrations.Add(new BusinessReportRegistration
    {
        Id = registrationId++, EventId = eventItem.Id, UserId = $"workflow-{eventItem.Id}",
        RegisteredAt = new DateTime(2026, 10, 2).AddDays(eventIndex % 22).AddHours(17),
        Status = workflowStatuses[eventIndex % workflowStatuses.Length], IsPaid = false
    });
}
var multiPageReport = BusinessReportCalculator.Build(filter, multiPageEvents, multiPageRegistrations, multiPageRegistrations, new DateOnly(2026, 10, 16), DateTime.Now);
var multiPagePdf = renderer.Render(multiPageReport);
var emptyPdf = renderer.Render(BusinessReportCalculator.Build(filter, [], [], [], new DateOnly(2026, 10, 16), DateTime.Now));
Check(CountAscii(multiPagePdf, "/Type /Page") > 4, "An oversized single-row value must continue across PDF pages without suppressing the rest of the report.");
Check(emptyPdf.Length > 500 && Encoding.ASCII.GetString(emptyPdf, 0, 4) == "%PDF", "Empty report results must generate a valid PDF.");
Check(multiPageReport.SubmissionTrend.Any(point => point.Count > 0) && multiPageReport.TrailChart.IsAggregated && multiPageReport.TrailChart.Entries[^1].Count > 0 && multiPageReport.EventStatusDistribution.Count == 3 && multiPageReport.PaymentDataExceptions >= 0, "Visual-review data must exercise nonzero trends, varied trail bars, nonzero Other, event statuses, and operational summaries.");

var fontRoot = options.PublishedRoot ?? root;
foreach (var asset in new[] { "Poppins-Regular.ttf", "Poppins-Medium.ttf", "Poppins-Bold.ttf", "Poppins-SemiBold.ttf", "Poppins-OFL.txt" })
    Check(File.Exists(Path.Combine(fontRoot, "wwwroot", "css", "fonts", asset)), $"Required font asset is missing: {asset}");
Check(File.ReadAllText(Path.Combine(fontRoot, "wwwroot", "css", "fonts", "Poppins-OFL.txt")).Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal), "The bundled Poppins license must retain the SIL OFL 1.1 notice.");

await VerifyAuthorizationAsync(report, Check);

if (options.ArtifactDirectory is not null)
{
    Directory.CreateDirectory(options.ArtifactDirectory);
    var multiPath = Path.Combine(options.ArtifactDirectory, "business-reports-fictional-multipage.pdf");
    var emptyPath = Path.Combine(options.ArtifactDirectory, "business-reports-fictional-empty.pdf");
    await File.WriteAllBytesAsync(multiPath, multiPagePdf);
    await File.WriteAllBytesAsync(emptyPath, emptyPdf);
    Console.WriteLine($"Multi-page artifact: {multiPath}");
    Console.WriteLine($"Empty-data artifact: {emptyPath}");
}

Console.WriteLine($"TrailGuard business reports verification passed ({assertions} assertions).");

static int CountAscii(byte[] bytes, string value)
{
    var needle = Encoding.ASCII.GetBytes(value);
    var count = 0;
    for (var index = 0; index <= bytes.Length - needle.Length; index++)
        if (bytes.AsSpan(index, needle.Length).SequenceEqual(needle)) count++;
    return count;
}

static async Task VerifyAuthorizationAsync(BusinessReportData report, Action<bool, string> check)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseTestServer();
    builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
    builder.Services.AddAuthorization();
    builder.Services.AddControllers().AddApplicationPart(typeof(ReportsController).Assembly).AddControllersAsServices();
    builder.Services.AddSingleton<IBusinessReportService>(new StubReportService(report));
    builder.Services.AddSingleton<IBusinessReportPdfRenderer>(new StubPdfRenderer());
    await using var app = builder.Build();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllerRoute("reports", "{controller=Reports}/{action=Index}/{id?}");
    await app.StartAsync();
    var client = app.GetTestClient();
    var anonymous = await client.GetAsync("/Reports/Export?startDate=2026-10-01&endDate=2026-10-31");
    check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "The PDF endpoint must challenge anonymous HTTP requests.");
    using var participantRequest = new HttpRequestMessage(HttpMethod.Get, "/Reports/Export?startDate=2026-10-01&endDate=2026-10-31");
    participantRequest.Headers.Add("X-Report-Role", "Participant");
    var participant = await client.SendAsync(participantRequest);
    check(participant.StatusCode == HttpStatusCode.Forbidden, "The PDF endpoint must forbid non-Admin HTTP requests.");
    using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/Reports/Export?startDate=2026-10-01&endDate=2026-10-31");
    adminRequest.Headers.Add("X-Report-Role", "Admin");
    var admin = await client.SendAsync(adminRequest);
    check(admin.StatusCode == HttpStatusCode.OK && admin.Content.Headers.ContentType?.MediaType == "application/pdf", "The PDF endpoint must allow an Admin through the authorization pipeline.");
    await app.StopAsync();
}

sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Report-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "verification"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

sealed class StubReportService(BusinessReportData report) : IBusinessReportService
{
    public Task<(BusinessReportFilter Filter, IReadOnlyList<string> Errors, IReadOnlyList<BusinessReportFilterOption> Trails, IReadOnlyList<BusinessReportFilterOption> Organizers)> NormalizeAsync(BusinessReportFilter requested, bool malformedDate) => Task.FromResult((new BusinessReportFilter { StartDate = requested.StartDate, EndDate = requested.EndDate, TrailId = requested.TrailId, OrganizerId = requested.OrganizerId }, (IReadOnlyList<string>)[], (IReadOnlyList<BusinessReportFilterOption>)[], (IReadOnlyList<BusinessReportFilterOption>)[]));
    public Task<BusinessReportData> BuildAsync(BusinessReportFilter filter, string? trailScope = null, string? organizerScope = null) => Task.FromResult(report);
}

sealed class StubPdfRenderer : IBusinessReportPdfRenderer
{
    public byte[] Render(BusinessReportData report) => Encoding.ASCII.GetBytes("%PDF-1.4 verification");
}

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "TrailGuard";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public string EnvironmentName { get; set; } = "Verification";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class VerificationOptions
{
    public string? ArtifactDirectory { get; init; }
    public string? PublishedRoot { get; init; }

    public static VerificationOptions Parse(string[] args)
    {
        string? artifactDirectory = null;
        string? publishedRoot = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--artifacts-dir" && index + 1 < args.Length) artifactDirectory = Path.GetFullPath(args[++index]);
            else if (args[index] == "--published-root" && index + 1 < args.Length) publishedRoot = Path.GetFullPath(args[++index]);
            else throw new ArgumentException("Use --artifacts-dir <outside-repository-directory> and/or --published-root <release-publish-directory>.");
        }
        if (artifactDirectory is not null && artifactDirectory.StartsWith(Path.GetFullPath(Directory.GetCurrentDirectory()), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Artifact output must be outside the repository.");
        return new VerificationOptions { ArtifactDirectory = artifactDirectory, PublishedRoot = publishedRoot };
    }
}
