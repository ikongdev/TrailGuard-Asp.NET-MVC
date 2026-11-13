using System.Collections;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using TrailGuard.Controllers;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

// Simulated controller checks: real controller, ownership rule, filters and weather
// service; LINQ-to-Objects async DbSets, controlled identity and HTTP responses.
// No database, MVC HTTP pipeline, external weather request or browser is used.
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}

Event MakeEvent(int id, string? owner, string status = "Upcoming", int trailId = 1) => new()
{
    Id = id, OrganizerId = owner, EventTitle = $"Event {id}", Status = status,
    TrailId = trailId, TrailNameSnapshot = $"Trail {trailId}", Location = "Batangas",
    Difficulty = "Minor Hike", EventDate = new DateTime(2026, 11, id), Capacity = 20,
    WeatherRiskLevel = "Low", WeatherReminder = $"Saved reminder {id}"
};
var events = new[]
{
    MakeEvent(1, "A"), MakeEvent(2, "A", "Completed"), MakeEvent(3, "B"),
    MakeEvent(4, "B", "Cancelled", 2), MakeEvent(5, null), MakeEvent(6, "A")
};
using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options)
{
    Events = new MemorySet<Event>(events),
    Trails = new MemorySet<Trail>([new() { Id = 1, Name = "Trail 1", Location = "Batangas", IsActive = true },
        new() { Id = 2, Name = "Trail 2", Location = "Laguna", IsActive = false }]),
    EventRegistrations = new MemorySet<EventRegistration>([
        new() { Id = 1, EventId = 1, Status = "Accepted" },
        new() { Id = 2, EventId = 1, Status = "Voided" }]),
    Assessments = new MemorySet<Assessment>([])
};
using var users = new ControlledUsers(context);
using var handler = new WeatherHandler();
using var httpClient = new HttpClient(handler);
var weather = new WeatherService(httpClient, NullLogger<WeatherService>.Instance);

EventController Controller(string? userId, bool admin = false)
{
    users.Current = userId == null ? null : new ApplicationUser { Id = userId };
    users.Admin = admin;
    var http = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(userId == null ? [] :
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, admin ? "Admin" : "Organizer")], "verification"))
    };
    return new EventController(context, null!, users, weather, NullLogger<EventController>.Instance, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = http },
        TempData = new TempDataDictionary(http, new MemoryTempData())
    };
}
async Task<EventManagementViewModel> Index(EventController controller, string? visibility = null,
    string search = "", string status = "All", string trail = "All", string difficulty = "All", string sort = "date_asc")
    => (EventManagementViewModel)((ViewResult)await controller.Index(search, status, trail, difficulty, sort, visibility)).Model!;
async Task<EventManagementViewModel> FollowStatusFallback(EventController controller, string visibility = "my",
    string search = "", string trail = "All", string difficulty = "All", string sort = "date_asc", string status = "Cancelled")
{
    var result = await controller.Index(search, status, trail, difficulty, sort, visibility);
    Check(result is RedirectToActionResult { ActionName: "Index", Permanent: false, PreserveMethod: false }, "Unavailable status uses a temporary canonical Index redirect.");
    var route = ((RedirectToActionResult)result).RouteValues!;
    var expected = new Dictionary<string, string>
    {
        ["visibility"] = visibility, ["searchString"] = search, ["status"] = "All",
        ["trailId"] = trail, ["difficulty"] = difficulty, ["sortOrder"] = sort
    };
    Check(route.Count == expected.Count && expected.All(pair => (string?)route[pair.Key] == pair.Value), "Canonical redirect changes only unavailable status, preserving every other filter.");
    Check((string?)controller.ViewData["CurrentStatus"] == "All", "Redirect ViewData contains the effective All status.");
    // Simulate following the returned route values through an encoded query.
    // Actual MVC URL generation and HTTP redirect handling are not exercised.
    var query = QueryHelpers.ParseQuery(QueryString.Create(route.Select(pair => new KeyValuePair<string, string?>(pair.Key, (string?)pair.Value))).Value);
    Check(expected.All(pair => query[pair.Key] == pair.Value), "Canonical route values survive query encoding without dropping visibility or other filters.");
    var model = await Index(controller, query["visibility"], query["searchString"].ToString(), query["status"].ToString(),
        query["trailId"].ToString(), query["difficulty"].ToString(), query["sortOrder"].ToString());
    Check((string?)controller.ViewData["CurrentStatus"] == "All" && model.Visibility == visibility, "Following the canonical values renders All Status in the preserved scope without another redirect.");
    return model;
}
int[] Ids(EventManagementViewModel model) => model.StatusGroups.SelectMany(g => g.Events).Select(e => e.Id).Order().ToArray();
JsonElement Json(JsonResult result) => JsonSerializer.SerializeToElement(result.Value);
void Denied(JsonResult result, string scenario)
{
    var json = Json(result);
    Check(!json.GetProperty("success").GetBoolean() && json.GetProperty("message").GetString() == "Event not found", scenario);
}

var a = Controller("A");
var mine = await Index(a);
Check(Ids(mine).SequenceEqual([1, 2, 6]) && mine.Visibility == "my", "A defaults to A's persisted assignments.");
Check(mine.UpcomingEventsCount == 2 && mine.AvailableStatuses.SequenceEqual(["Upcoming", "Completed"]), "A's count and fixed status ordering use My Events scope.");
Check(mine.StatusGroups.SelectMany(g => g.Events).Single(e => e.Id == 1).RegisteredCount == 1, "Existing active slot counting is preserved.");
foreach (var invalid in new[] { "", "ALL", "other", "B", " my " })
    Check(Ids(await Index(a, invalid)).SequenceEqual([1, 2, 6]), $"Invalid visibility '{invalid}' defaults to My Events.");
var all = await Index(a, "all");
Check(Ids(all).SequenceEqual([1, 2, 3, 4, 5, 6]) && all.Visibility == "all", "All Events includes other organizers and unassigned cards.");
Check(all.UpcomingEventsCount == 4 && all.AvailableStatuses.SequenceEqual(["Upcoming", "Completed", "Cancelled"]), "All Events counts and derived statuses use its scope.");
Check(events.Where(all.CanManageEvent).Select(e => e.Id).SequenceEqual([1, 2, 6]), "Only A's cards receive management permission in All Events.");
Check(!all.CanManageEvent(events[4]), "Unassigned cards are browse-only.");
var combined = await Index(a, "all", "Event 3", "Upcoming", "1", "Minor Hike", "date_desc");
Check(Ids(combined).SequenceEqual([3]) && combined.UpcomingEventsCount == 4 && combined.StatusGroups.Single().Events.Count == 1,
    "Combined search/status/trail/difficulty/sort filters retain scoped header and final section counts.");
var allCancelled = await Index(a, "all", status: "Cancelled");
Check(Ids(allCancelled).SequenceEqual([4]) && (string?)a.ViewData["CurrentStatus"] == "Cancelled" && allCancelled.AvailableStatuses.Contains("Cancelled"), "All Events preserves Cancelled when available in that scope.");
var switched = await FollowStatusFallback(a);
Check(Ids(switched).SequenceEqual([1, 2, 6]) && switched.AvailableStatuses.SequenceEqual(["Upcoming", "Completed"]), "All Events + Cancelled switching to My Events falls back to A's full scoped results and fixed status order.");
Check(switched.UpcomingEventsCount == 2 && switched.StatusGroups.Select(g => g.Events.Count).SequenceEqual([2, 1]), "Fallback preserves scoped Upcoming and final section counts.");
var fallbackCombined = await FollowStatusFallback(a, search: "Event 1", trail: "1", difficulty: "Minor Hike", sort: "date_desc");
Check(Ids(fallbackCombined).SequenceEqual([1]) && fallbackCombined.UpcomingEventsCount == 2 && fallbackCombined.StatusGroups.Single().Events.Count == 1, "Other filters still apply after status fallback; the header remains scoped before filtering.");
Check((string?)a.ViewData["CurrentFilter"] == "Event 1" && (string?)a.ViewData["CurrentTrailId"] == "1" &&
    (string?)a.ViewData["CurrentDifficulty"] == "Minor Hike" && (string?)a.ViewData["CurrentSort"] == "date_desc", "Rendered filter ViewData preserves search, trail, difficulty and sort after navigation.");
Check(!(await FollowStatusFallback(a, search: "Event & 1", trail: "2", difficulty: "Minor Hike", sort: "date_desc")).HasAnyResults, "Fallback preserves an encoded search and a nonmatching trail rather than resetting them to produce results.");
Check((await Index(a, "my", status: "Upcoming")).StatusGroups.Single().Status == "Upcoming" && (string?)a.ViewData["CurrentStatus"] == "Upcoming", "Available selected status stays selected without a redirect.");
Check(Ids(await Index(a, "my", status: "All")).SequenceEqual([1, 2, 6]) && (string?)a.ViewData["CurrentStatus"] == "All", "Explicit All remains selected without a redirect.");
Check(Ids(await FollowStatusFallback(a, "all", status: "Unknown")).Length == events.Length, "Unknown status falls back to All while retaining All Events visibility.");
Check((await Index(a, sort: "date_desc")).StatusGroups[0].Events.Select(e => e.Id).SequenceEqual([6, 1]), "Existing descending date ordering is preserved.");
Check((await Index(a, sort: "invalid")).StatusGroups[0].Events.Select(e => e.Id).SequenceEqual([1, 6]), "Invalid sort keeps established default.");
Check(Ids(await Index(Controller("B"))).SequenceEqual([3, 4]), "B defaults to B's events.");
var b = Controller("B");
Check(Ids(await Index(b, "my", status: "Cancelled")).SequenceEqual([4]) && (string?)b.ViewData["CurrentStatus"] == "Cancelled", "Cancelled remains selected in My Events when the organizer has that status.");
var noAssignments = Controller("C");
var empty = await FollowStatusFallback(noAssignments);
Check(!empty.HasAnyResults && empty.StatusGroups.Count == 0 && empty.UpcomingEventsCount == 0 && empty.AvailableStatuses.SequenceEqual(["Upcoming", "Completed"]), "Organizer with no assignments has a scoped empty state and fixed statuses.");
Check((string?)noAssignments.ViewData["CurrentStatus"] == "All", "No-assignment fallback renders the generic All Status empty state.");
Check(!(await Index(noAssignments, "my", status: "Completed")).HasAnyResults && (string?)noAssignments.ViewData["CurrentStatus"] == "Completed", "A fixed available status is preserved even with no matching assignments.");
var admin = await Index(Controller("Admin", true), "my");
Check(admin.IsAdmin && admin.Visibility == "all" && Ids(admin).Length == events.Length && events.All(admin.CanManageEvent), "Admin ignores organizer visibility and manages every assignment.");
Check(await Controller(null).Index("", "", "", "", "", "all") is ForbidResult, "Unresolved identity fails closed even for All Events.");

a = Controller("A");
Denied(await a.GetEvent(3), "Direct GetEvent for another organizer is denied.");
Check(await a.Details(3) is RedirectToActionResult { ActionName: "Index" } && (string?)a.TempData["Error"] == "Event not found", "Direct Details for another organizer is denied.");
Denied(await a.GetEvent(5), "Unassigned GetEvent remains denied.");
Denied(await a.EditEvent(new EventEditModel { Id = 3, OrganizerId = "A", TrailId = 1 }), "Forged organizer assignment cannot authorize EditEvent.");
Denied(await a.CompleteEvent(new() { Id = 3 }), "Cross-organizer completion remains denied.");
Denied(await a.CancelEvent(new() { Id = 3, Reason = "Forged cancellation" }), "Cross-organizer cancellation remains denied.");
Denied(await a.RescheduleEvent(new() { Id = 3, NewDate = new DateTime(2027, 1, 1) }), "Cross-organizer rescheduling remains denied.");
Check(events[2].OrganizerId == "B" && events[2].Status == "Upcoming", "Denied mutations preserve assignment and lifecycle data.");
Check(Json(await a.GetEvent(1)).GetProperty("success").GetBoolean(), "Owned edit data stays accessible.");
Check(!Json(await a.GetEvent(1)).GetProperty("canChangeTrail").GetBoolean(), "Participation freezes the owned Event trail in edit state.");
Check(!Json(await a.GetEvent(2)).GetProperty("success").GetBoolean(), "Completed events remain read-only.");
Check(Json(await Controller("Admin", true).GetEvent(3)).GetProperty("success").GetBoolean(), "Admin retains cross-organizer edit-data access.");
Check(Json(await Controller("Admin", true).GetEvent(3)).GetProperty("canChangeTrail").GetBoolean(), "No-record Event trail stays editable for Admin.");

a = Controller("A");
foreach (var id in new[] { 3, 5, 999, 0, -1 })
{
    var before = handler.Calls;
    var trailReads = ((MemorySet<Trail>)context.Trails).FindCalls;
    Denied(await a.GetWeatherForecast(1, new DateTime(2026, 11, 10), id), $"Weather rejects event {id}.");
    Check(handler.Calls == before && ((MemorySet<Trail>)context.Trails).FindCalls == trailReads, "Rejected weather ID stops before trail lookup or external HTTP.");
}
a.ModelState.AddModelError("eventId", "Not an integer");
Denied(await a.GetWeatherForecast(1, new DateTime(2026, 11, 10)), "Malformed bound eventId cannot become a successful generic forecast.");
a.ModelState.Clear();
a.Request.QueryString = new QueryString("?eventId=");
Denied(await a.GetWeatherForecast(1, new DateTime(2026, 11, 10)), "An explicitly empty eventId cannot become a successful generic forecast.");
a.Request.QueryString = QueryString.Empty;
var ownedWeather = Json(await a.GetWeatherForecast(1, new DateTime(2026, 11, 10), 1));
Check(ownedWeather.GetProperty("success").GetBoolean() && ownedWeather.GetProperty("suggestedReminder").GetString() == "Saved reminder 1", "Owned weather can reuse its saved reminder.");
var adminWeather = Json(await Controller("Admin", true).GetWeatherForecast(1, new DateTime(2026, 11, 10), 3));
Check(adminWeather.GetProperty("success").GetBoolean() && adminWeather.GetProperty("suggestedReminder").GetString() == "Saved reminder 3", "Admin can retrieve another organizer's saved weather reminder.");
var beforeGeneric = handler.Calls;
var generic = Json(await Controller("A").GetWeatherForecast(1, new DateTime(2026, 11, 10)));
Check(generic.GetProperty("success").GetBoolean() && handler.Calls == beforeGeneric + 2 && generic.GetProperty("suggestedReminder").GetString() != "Saved reminder 1", "Add Event's no-eventId trail/date flow remains intact.");
Check(!Json(await Controller("A").GetWeatherForecast(999, new DateTime(2026, 11, 10))).GetProperty("success").GetBoolean(), "No-eventId flow retains missing-trail validation.");
Denied(await Controller(null).GetWeatherForecast(1, new DateTime(2026, 11, 10), 1), "Unresolved identity cannot read event weather.");

// Source-contract checks cover Razor gating and preserved endpoint attributes;
// these are intentionally distinguished from the executed controller scenarios.
var root = Directory.GetCurrentDirectory();
var view = File.ReadAllText(Path.Combine(root, "Views/Event/Index.cshtml"));
var actionsStart = view.IndexOf("@if (Model.CanManageEvent(eventItem))", StringComparison.Ordinal);
var actionsEnd = view.IndexOf("Assigned to another organizer", actionsStart, StringComparison.Ordinal);
var controls = view[actionsStart..actionsEnd];
Check(controls.Contains("asp-action=\"Details\"") && controls.Contains("onclick=\"openEditModal") && controls.Contains("@if (!isCompleted)") && controls.Contains("No organizer assigned"), "Razor server-gates both management controls and preserves completed/unassigned wording.");
Check(view.Contains("@if (!Model.IsAdmin)") && view.Contains("<label for=\"visibilityFilter\"") && view.Contains("name=\"visibility\" data-custom-select data-custom-select-portal") && view.Contains("Model.Visibility == \"my\"") && view.Contains("Model.Visibility == \"all\""), "Organizer-only visibility control uses shared select and server-selected options.");
Check(view.Contains("const url = new URL(window.location.href)") && view.Contains("url.searchParams.set('searchString', term)"), "Existing search updates only its own URL parameter, preserving visibility.");
// Source-only rendering contract: no HTML or enhanced listbox is rendered here.
Check(view.Contains("var currentStatus = ViewData[\"CurrentStatus\"] as string ?? \"All\"") &&
    view.Contains("<option value=\"All\" selected=\"@(currentStatus == \"All\" ? \"selected\" : null)\"") &&
    view.Contains("@foreach (var s in Model.AvailableStatuses)") && view.Contains("selected=\"@(currentStatus == s ? \"selected\" : null)\"") &&
    view.Contains("id=\"statusFilter\" name=\"status\" data-custom-select"), "Razor sources native selection from effective ViewData and options from the selected scope for shared enhancement.");
Check(view.Contains("@if (currentStatus != \"All\")") && view.Contains("No @currentStatus events found") && view.Contains("<h3 class=\"text-xl font-bold text-gray-300\">No events found</h3>"), "Razor uses effective status for specific versus generic empty-state wording.");
var customSelect = File.ReadAllText(Path.Combine(root, "wwwroot/js/custom-select.js"));
Check(customSelect.Contains("var selectedOption = select.options[select.selectedIndex] || null;") &&
    customSelect.Contains("instance.labelSpan.textContent = hasRealSelection ? optionLabel(selectedOption)"), "Shared enhanced-select label reads the server-selected native option (source-only check).");
var authorize = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(EventController), typeof(AuthorizeAttribute))!;
Check(authorize.Roles == "Admin,Organizer", "Management role boundary is preserved.");
foreach (var method in new[] { "EditEvent", "CompleteEvent", "CancelEvent", "RescheduleEvent" })
    Check(Attribute.IsDefined(typeof(EventController).GetMethod(method)!, typeof(ValidateAntiForgeryTokenAttribute)), $"{method} retains antiforgery.");
Console.WriteLine($"PASS: {assertions} assertions (simulated production-controller behavior plus explicitly labeled source/attribute checks). No database, real HTTP pipeline or browser verification.");

sealed class ControlledUsers(ApplicationDbContext context) : UserManager<ApplicationUser>(
    new UserStore<ApplicationUser>(context), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
    [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance)
{
    public ApplicationUser? Current { get; set; }
    public bool Admin { get; set; }
    public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal) => Task.FromResult(Current);
    public override Task<bool> IsInRoleAsync(ApplicationUser user, string role) => Task.FromResult(role == "Admin" && Admin);
    public override Task<IList<ApplicationUser>> GetUsersInRoleAsync(string role) => Task.FromResult<IList<ApplicationUser>>([]);
}

sealed class MemorySet<T>(IEnumerable<T> rows) : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T> where T : class
{
    private readonly IQueryable<T> _query = new AsyncQuery<T>(rows);
    public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType => throw new NotSupportedException("This verifier does not use EF entity metadata.");
    public int FindCalls { get; private set; }
    public Type ElementType => typeof(T);
    public Expression Expression => _query.Expression;
    public IQueryProvider Provider => _query.Provider;
    public IEnumerator<T> GetEnumerator() => _query.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default) => new AsyncIterator<T>(GetEnumerator());
    public override ValueTask<T?> FindAsync(params object?[]? keyValues)
    {
        FindCalls++;
        var id = (int)keyValues![0]!;
        return ValueTask.FromResult(rows.SingleOrDefault(row => (int)typeof(T).GetProperty("Id")!.GetValue(row)! == id));
    }
}

sealed class AsyncQuery<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public AsyncQuery(IEnumerable<T> rows) : base(rows) { }
    public AsyncQuery(Expression expression) : base(expression) { }
    IQueryProvider IQueryable.Provider => new AsyncProvider(this);
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default) => new AsyncIterator<T>(this.AsEnumerable().GetEnumerator());
}

sealed class AsyncProvider(IQueryProvider inner) : IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) => (IQueryable)Activator.CreateInstance(typeof(AsyncQuery<>).MakeGenericType(expression.Type.GetGenericArguments()[0]), expression)!;
    public IQueryable<T> CreateQuery<T>(Expression expression) => new AsyncQuery<T>(expression);
    public object? Execute(Expression expression) => inner.Execute(expression);
    public T Execute<T>(Expression expression) => inner.Execute<T>(expression);
    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken token = default)
    {
        var resultType = typeof(TResult).GetGenericArguments()[0];
        var result = inner.Execute(expression);
        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [result])!;
    }
}

sealed class AsyncIterator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}

sealed class WeatherHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        var body = request.RequestUri!.Host.StartsWith("geocoding", StringComparison.Ordinal)
            ? """{"results":[{"name":"Batangas","latitude":13.7,"longitude":121.0,"country_code":"PH"}]}"""
            : """{"daily":{"temperature_2m_max":[28],"temperature_2m_min":[22],"precipitation_sum":[0],"weathercode":[0],"windspeed_10m_max":[5]}}""";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

sealed class MemoryTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
