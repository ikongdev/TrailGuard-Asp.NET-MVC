using System.Text.Json;
using TrailGuard.Models;
using TrailGuard.Services;

var (populatedPath, emptyPath) = ParsePaths(args);
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

var filter = new BusinessReportFilter { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) };
var events = Enumerable.Range(1, 16).Select(index => new BusinessReportEvent
{
    Id = index,
    TrailId = (index - 1) % 10 + 1,
    TrailNameSnapshot = $"Trail {((index - 1) % 10 + 1):00}",
    EventTitle = $"Populated chart event {index}",
    EventDate = new DateTime(2026, 10, 1).AddDays(index - 1),
    Status = index <= 2 ? "Upcoming" : index <= 12 ? "Completed" : "Cancelled",
    Capacity = 24,
    OrganizerId = "org-a",
    OrganizerName = "Aira Santos"
}).ToList();

var registrations = new List<BusinessReportRegistration>();
var registrationId = 1;
foreach (var eventItem in events)
{
    var acceptedCount = 13 - ((eventItem.Id - 1) % 10);
    for (var participant = 0; participant < acceptedCount; participant++)
    {
        registrations.Add(new BusinessReportRegistration
        {
            Id = registrationId++, EventId = eventItem.Id, UserId = $"participant-{eventItem.Id}-{participant}",
            RegisteredAt = new DateTime(2026, 10, 1).AddDays((eventItem.Id * 2 + participant) % 19).AddHours(8 + participant % 7),
            Status = "Accepted", IsPaid = participant % 2 == 0
        });
    }
}

var report = BusinessReportCalculator.Build(filter, events, registrations, registrations, new DateOnly(2026, 10, 16), new DateTime(2026, 10, 16, 9, 0, 0));
var payload = BusinessReportChartPayload.From(report);
var json = JsonSerializer.Serialize(payload);
using (var document = JsonDocument.Parse(json))
{
    var root = document.RootElement;
    Check(root.TryGetProperty("submissions", out var submissions) && submissions.GetArrayLength() == 31, "Chart JSON must expose 31 ordered daily submission buckets.");
    Check(submissions.EnumerateArray().Any(point => point.GetProperty("count").GetInt32() > 0) && submissions.EnumerateArray().All(point => !string.IsNullOrWhiteSpace(point.GetProperty("label").GetString())), "Submission JSON must carry readable labels and nonzero daily values.");
    Check(root.TryGetProperty("trails", out var trails) && trails.GetArrayLength() == 9 && trails[8].GetProperty("isOther").GetBoolean() && trails[8].GetProperty("count").GetInt32() > 0, "Chart JSON must preserve deterministic Top 8 plus nonzero Other.");
    Check(root.GetProperty("trails").EnumerateArray().Sum(point => point.GetProperty("count").GetInt32()) == root.GetProperty("trailChartTotal").GetInt32(), "Trail chart JSON must reconcile with its full-scope accepted total.");
    Check(root.TryGetProperty("statuses", out var statuses) && statuses.EnumerateArray().ToDictionary(point => point.GetProperty("status").GetString()!, point => point.GetProperty("count").GetInt32())["Upcoming"] == 2 && statuses.EnumerateArray().ToDictionary(point => point.GetProperty("status").GetString()!, point => point.GetProperty("count").GetInt32())["Completed"] == 10 && statuses.EnumerateArray().ToDictionary(point => point.GetProperty("status").GetString()!, point => point.GetProperty("count").GetInt32())["Cancelled"] == 4, "Status JSON must match the populated operational summary.");
    Check(!root.GetProperty("submissions")[0].TryGetProperty("Label", out _) && !root.GetProperty("trails")[0].TryGetProperty("Count", out _) && !root.GetProperty("statuses")[0].TryGetProperty("Status", out _), "The explicit browser payload must not rely on Pascal-case serializer defaults.");
}

var emptyReport = BusinessReportCalculator.Build(filter, [], [], [], new DateOnly(2026, 10, 16), new DateTime(2026, 10, 16, 9, 0, 0));
var emptyJson = JsonSerializer.Serialize(BusinessReportChartPayload.From(emptyReport));
using (var emptyDocument = JsonDocument.Parse(emptyJson))
{
    var root = emptyDocument.RootElement;
    Check(root.GetProperty("submissions").GetArrayLength() == 31 && root.GetProperty("submissions").EnumerateArray().All(point => point.GetProperty("count").GetInt32() == 0) && root.GetProperty("trails").GetArrayLength() == 0 && root.GetProperty("statuses").GetArrayLength() == 0, "Zero-data scopes must retain ordered buckets while exposing empty trail and status series.");
}

Directory.CreateDirectory(Path.GetDirectoryName(populatedPath)!);
Directory.CreateDirectory(Path.GetDirectoryName(emptyPath)!);
await File.WriteAllTextAsync(populatedPath, json);
await File.WriteAllTextAsync(emptyPath, emptyJson);
Console.WriteLine($"Populated chart payload: {populatedPath}");
Console.WriteLine($"Empty chart payload: {emptyPath}");
Console.WriteLine($"TrailGuard business report chart payload verification passed ({assertions} assertions).");

static (string PopulatedPath, string EmptyPath) ParsePaths(string[] args)
{
    string? populated = null;
    string? empty = null;
    for (var index = 0; index < args.Length; index++)
    {
        if (args[index] == "--populated-payload" && index + 1 < args.Length) populated = Path.GetFullPath(args[++index]);
        else if (args[index] == "--empty-payload" && index + 1 < args.Length) empty = Path.GetFullPath(args[++index]);
        else throw new ArgumentException("Use --populated-payload <outside-repository-file> --empty-payload <outside-repository-file>.");
    }

    if (populated is null || empty is null) throw new ArgumentException("Both payload paths are required.");
    var repositoryRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
    if (populated.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase) || empty.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Payload output must be outside the repository.");
    return (populated, empty);
}
