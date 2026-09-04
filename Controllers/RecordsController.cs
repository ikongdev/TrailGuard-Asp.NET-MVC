using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Admin,Organizer")]
    public class RecordsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<RecordsController> _logger;

        // Event History is historical record only - Upcoming (and any other
        // operational status) never belongs here. See CLAUDE.md, Event Lifecycle.
        private static readonly string[] HistoryStatuses = { "Completed", "Cancelled" };

        public RecordsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ILogger<RecordsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string? organizerId)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Challenge();
            }

            var scope = await ResolveScopeAsync(currentUser, organizerId);

            // Fail closed: a supplied organizerId that doesn't resolve to a
            // current Organizer-role user is never treated as "no filter" -
            // that would silently widen the page from a scoped Organizer
            // view to the full system-wide Admin dataset. A generic 404 is
            // returned before any Records data is queried, and is
            // indistinguishable whether the id was unknown, malformed, or
            // belonged to a non-Organizer account.
            if (!scope.IsValid)
            {
                return NotFound();
            }

            var model = new RecordsViewModel
            {
                IsAdmin = scope.IsAdmin,
                OrganizerOptions = scope.OrganizerOptions,
                SelectedOrganizerId = scope.ScopedOrganizerId
            };

            model.EventHistory = await BuildEventHistoryAsync(scope);
            model.Registrations = await BuildRegistrationsAsync(scope);
            model.TrailUsage = await BuildTrailUsageAsync(scope);

            var feedback = await BuildFeedbackAsync(scope);
            model.Feedbacks = feedback;
            model.FeedbackCount = feedback.Count;
            model.FeedbackAverage = feedback.Count > 0 ? feedback.Average(f => f.Rating) : 0d;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Export(string? search, string? historyStatus, string? sort, string? organizerId)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Challenge();
                }

                var scope = await ResolveScopeAsync(currentUser, organizerId);

                // Same fail-closed rule as Index, and the same shared
                // resolver - an invalid organizerId here must never fall
                // back to exporting the unscoped/system-wide dataset.
                if (!scope.IsValid)
                {
                    return NotFound();
                }

                // Explicit allow-lists, not a silent fallback to a default -
                // a tampered historyStatus/sort value must reject the
                // request rather than quietly widen or reorder the export.
                if (!TryNormalizeHistoryStatus(historyStatus, out var normalizedHistoryStatus))
                {
                    return BadRequest();
                }
                if (!TryNormalizeSort(sort, out var oldestFirst))
                {
                    return BadRequest();
                }

                var normalizedSearch = (search ?? string.Empty).Trim();

                var events = await BuildEventHistoryAsync(scope);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    // Organizer name is only ever a searchable field for the
                    // Admin scope - it's the same field the client's own
                    // search string omits for a non-Admin scope (see
                    // EventSearch/RegistrationSearch in Records/Index.cshtml).
                    // Matching it unconditionally here would let a plain
                    // Organizer's search for "you" (their own resolved
                    // display placeholder) match every exported row even
                    // though the page itself never offers that field to
                    // search against.
                    events = events.Where(e => MatchesSearch(normalizedSearch, e.EventTitle, e.TrailName, scope.IsAdmin ? e.OrganizerName : null)).ToList();
                }
                if (normalizedHistoryStatus != "All")
                {
                    events = events.Where(e => e.Status == normalizedHistoryStatus).ToList();
                }
                events = (oldestFirst ? events.OrderBy(e => e.EventDateTime).ThenBy(e => e.Id)
                                       : events.OrderByDescending(e => e.EventDateTime).ThenByDescending(e => e.Id)).ToList();

                var registrations = await BuildRegistrationsAsync(scope);
                if (!string.IsNullOrEmpty(normalizedSearch))
                {
                    registrations = registrations.Where(r => MatchesSearch(normalizedSearch, r.ParticipantName, r.EventTitle, scope.IsAdmin ? r.OrganizerName : null)).ToList();
                }
                registrations = (oldestFirst ? registrations.OrderBy(r => r.RegisteredAt).ThenBy(r => r.Id)
                                              : registrations.OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id)).ToList();

                if (events.Count == 0 && registrations.Count == 0)
                {
                    return NotFound();
                }

                var csv = new System.Text.StringBuilder();
                csv.AppendLine(string.Join(",", "Record Type", "Organizer", "Event", "Trail", "Participant", "Date", "Status", "Capacity", "Registered"));

                foreach (var e in events)
                {
                    csv.AppendLine(string.Join(",",
                        Csv("Event History"),
                        Csv(e.OrganizerName),
                        Csv(e.EventTitle),
                        Csv(e.TrailName),
                        Csv(""),
                        Csv(e.EventDate.ToString("MMM dd, yyyy")),
                        Csv(e.Status),
                        Csv(e.Capacity.ToString()),
                        Csv(e.Registered.ToString())));
                }

                foreach (var r in registrations)
                {
                    csv.AppendLine(string.Join(",",
                        Csv("Registration"),
                        Csv(r.OrganizerName),
                        Csv(r.EventTitle),
                        Csv(""),
                        Csv(r.ParticipantName),
                        Csv(r.RegisteredAt.ToString("MMM dd, yyyy")),
                        Csv(r.Status),
                        Csv(""),
                        Csv("")));
                }

                var fileName = $"trailguard-records-{DateTime.Now:yyyyMMdd-HHmm}.csv";
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export Records for user {UserId}.", _userManager.GetUserId(User));
                return StatusCode(500, "Unable to generate the export. Please try again.");
            }
        }

        // RFC-style CSV quoting (always quoted, embedded quotes doubled) plus
        // spreadsheet formula-injection protection: a value whose first
        // meaningful character is =, +, -, or @ is prefixed with a literal
        // apostrophe so Excel/Sheets render it as text instead of evaluating
        // it as a formula. The visible value itself is never trimmed or
        // otherwise altered - only the leading apostrophe is added, and only
        // when needed.
        private static string Csv(string? value)
        {
            var v = value ?? string.Empty;
            if (StartsWithFormulaTrigger(v))
            {
                v = "'" + v;
            }
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        // A leading run of whitespace/control characters (space, tab, CR,
        // LF, etc.) doesn't stop a spreadsheet app from treating what
        // follows as a formula, so checking only index 0 let a value like
        // " =SUM(A1:A9)" or "\t=cmd|..." slip past the check above. This
        // scans past any such leading characters to find the first one that
        // actually matters.
        private static bool StartsWithFormulaTrigger(string value)
        {
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                return c == '=' || c == '+' || c == '-' || c == '@';
            }
            return false;
        }

        private static bool MatchesSearch(string term, params string?[] fields)
        {
            return fields.Any(f => !string.IsNullOrEmpty(f) && f.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Explicit allow-list, not a permissive default - a value outside
        // this set must reject the export request (see Export), never
        // silently collapse to "All". Keeps the exact canonical strings the
        // History Status <select> in Records/Index.cshtml submits.
        private static bool TryNormalizeHistoryStatus(string? value, out string normalized)
        {
            if (string.IsNullOrEmpty(value) || value == "All")
            {
                normalized = "All";
                return true;
            }
            if (HistoryStatuses.Contains(value))
            {
                normalized = value;
                return true;
            }
            normalized = string.Empty;
            return false;
        }

        // Same allow-list discipline as TryNormalizeHistoryStatus, for the
        // exact "newest"/"oldest" values the Sort <select> submits. Omitted
        // uses the established Newest-first default; anything else rejects
        // the request rather than silently choosing an ordering.
        private static bool TryNormalizeSort(string? value, out bool oldestFirst)
        {
            if (string.IsNullOrEmpty(value) || value == "newest")
            {
                oldestFirst = false;
                return true;
            }
            if (value == "oldest")
            {
                oldestFirst = true;
                return true;
            }
            oldestFirst = false;
            return false;
        }

        // Single place that turns "who is asking, and which organizerId did they
        // request" into the actual scope every query below uses - both Index
        // and Export call this and nothing else, so they can't drift. An
        // Organizer - including a dual-role Admin+Organizer account, which is
        // always routed through the IsAdmin branch instead - only ever gets
        // their own stable OrganizerId; any requestedOrganizerId is ignored
        // outright for that branch, so an Organizer can never gain Admin
        // scope through the query parameter.
        //
        // For Admin, a requested id is one of exactly three outcomes:
        //   - omitted/blank            -> IsValid=true,  ScopedOrganizerId=null  (All Organizers)
        //   - a current Organizer's id -> IsValid=true,  ScopedOrganizerId=id
        //   - anything else            -> IsValid=false                          (fail closed)
        // "Anything else" covers an unknown id, a malformed value, and a
        // valid user id that simply isn't in the Organizer role today -
        // all three produce the identical IsValid=false result so the
        // caller's generic 404 never reveals which case occurred.
        private async Task<RecordsScope> ResolveScopeAsync(ApplicationUser currentUser, string? requestedOrganizerId)
        {
            var isAdmin = await _userManager.IsInRoleAsync(currentUser, "Admin");

            if (!isAdmin)
            {
                // The exported CSV must carry a portable identity, not a
                // UI-relative pronoun - "You" means nothing once the file is
                // downloaded, shared, or opened later. currentUser is already
                // loaded by the caller (no extra Identity query here), and
                // this is the same "FirstName LastName" formatting the Admin
                // Organizer filter/roster uses below, so an Organizer's own
                // name and an Admin's view of that same Organizer always
                // read identically.
                return new RecordsScope
                {
                    IsAdmin = false,
                    IsValid = true,
                    ScopedOrganizerId = currentUser.Id,
                    OrganizerNames = new Dictionary<string, string> { [currentUser.Id] = $"{currentUser.FirstName} {currentUser.LastName}".Trim() },
                    OrganizerOptions = new List<OrganizerOptionViewModel>()
                };
            }

            var organizerUsers = await _userManager.GetUsersInRoleAsync("Organizer");
            var orderedOrganizers = organizerUsers
                .OrderBy(u => u.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.LastName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.Id, StringComparer.Ordinal)
                .ToList();

            var organizerNames = orderedOrganizers.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
            var organizerOptions = orderedOrganizers
                .Select(u => new OrganizerOptionViewModel { Id = u.Id, Name = organizerNames[u.Id] })
                .ToList();

            if (string.IsNullOrWhiteSpace(requestedOrganizerId))
            {
                return new RecordsScope
                {
                    IsAdmin = true,
                    IsValid = true,
                    ScopedOrganizerId = null,
                    OrganizerNames = organizerNames,
                    OrganizerOptions = organizerOptions
                };
            }

            if (!organizerNames.ContainsKey(requestedOrganizerId))
            {
                // Fail closed: never normalize an unrecognized id to "no
                // filter" - that would silently expand a scoped request into
                // the full system-wide dataset.
                return new RecordsScope
                {
                    IsAdmin = true,
                    IsValid = false,
                    ScopedOrganizerId = null,
                    OrganizerNames = organizerNames,
                    OrganizerOptions = organizerOptions
                };
            }

            return new RecordsScope
            {
                IsAdmin = true,
                IsValid = true,
                ScopedOrganizerId = requestedOrganizerId,
                OrganizerNames = organizerNames,
                OrganizerOptions = organizerOptions
            };
        }

        // Two distinct fallbacks, not one: a null OrganizerId is a real,
        // pre-existing "no owner was ever recorded" state (see Event.cs -
        // never treated as owned by whoever happens to ask). A non-null
        // OrganizerId that doesn't resolve to a name in scope.OrganizerNames
        // is a different situation - a stable owner *is* recorded, but their
        // current Identity display name isn't available in this scope's
        // roster (e.g. the account's role changed or the account no longer
        // exists) - collapsing that into "Unassigned" would misreport a
        // known ownership as unknown. Neither fallback is ever treated as
        // proof of ownership by any query above; both are display-only.
        private string OrganizerDisplayName(RecordsScope scope, string? organizerId)
        {
            if (string.IsNullOrEmpty(organizerId)) return "Unassigned";
            return scope.OrganizerNames.TryGetValue(organizerId, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : "Organizer unavailable";
        }

        private async Task<List<EventHistoryRowViewModel>> BuildEventHistoryAsync(RecordsScope scope)
        {
            var query = _context.Events.AsNoTracking()
                .Where(e => HistoryStatuses.Contains(e.Status));

            if (scope.ScopedOrganizerId != null)
            {
                query = query.Where(e => e.OrganizerId == scope.ScopedOrganizerId);
            }

            var events = await query
                .Select(e => new
                {
                    e.Id,
                    e.EventTitle,
                    // Event's own Trail Snapshot - a historical record must show
                    // what the Trail was when this Event was created/last
                    // recaptured, not what it currently is. See CLAUDE.md,
                    // "Event Trail Snapshot".
                    TrailName = e.TrailNameSnapshot,
                    e.OrganizerId,
                    e.EventDate,
                    e.EventTime,
                    e.Status,
                    e.Capacity
                })
                .ToListAsync();

            var eventIds = events.Select(e => e.Id).ToList();

            // Registered pairs with Capacity, so it uses the same
            // RegistrationStatusHelper.ActiveStatuses definition every other
            // Capacity/Registered pairing in the app uses (EventController,
            // OrganizerController, ParticipantController) - a single grouped
            // query, never an Include-based join, so no row is ever double
            // counted.
            var registeredCounts = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId) && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            return events.Select(e => new EventHistoryRowViewModel
            {
                Id = e.Id,
                EventTitle = e.EventTitle,
                TrailName = string.IsNullOrEmpty(e.TrailName) ? "Unknown Trail" : e.TrailName,
                OrganizerId = e.OrganizerId,
                OrganizerName = OrganizerDisplayName(scope, e.OrganizerId),
                EventDate = e.EventDate,
                EventDateTime = e.EventDate.Date + e.EventTime,
                Status = e.Status,
                Capacity = e.Capacity,
                Registered = registeredCounts.TryGetValue(e.Id, out var count) ? count : 0
            })
            .OrderByDescending(e => e.EventDateTime).ThenByDescending(e => e.Id)
            .ToList();
        }

        private async Task<List<RegistrationRowViewModel>> BuildRegistrationsAsync(RecordsScope scope)
        {
            var query = _context.EventRegistrations.AsNoTracking()
                .Where(r => r.Event != null);

            if (scope.ScopedOrganizerId != null)
            {
                query = query.Where(r => r.Event!.OrganizerId == scope.ScopedOrganizerId);
            }

            var registrations = await query
                .Select(r => new
                {
                    r.Id,
                    r.ParticipantName,
                    EventTitle = r.Event!.EventTitle,
                    OrganizerId = r.Event.OrganizerId,
                    r.RegisteredAt,
                    r.Status
                })
                .ToListAsync();

            return registrations.Select(r => new RegistrationRowViewModel
            {
                Id = r.Id,
                ParticipantName = r.ParticipantName,
                EventTitle = r.EventTitle,
                OrganizerId = r.OrganizerId,
                OrganizerName = OrganizerDisplayName(scope, r.OrganizerId),
                RegisteredAt = r.RegisteredAt,
                Status = r.Status
            })
            .OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id)
            .ToList();
        }

        private async Task<List<TrailUsageRowViewModel>> BuildTrailUsageAsync(RecordsScope scope)
        {
            var completedQuery = _context.Events.AsNoTracking()
                .Where(e => e.Status == "Completed");

            if (scope.ScopedOrganizerId != null)
            {
                completedQuery = completedQuery.Where(e => e.OrganizerId == scope.ScopedOrganizerId);
            }

            // Deliberately the CURRENT Trail catalog name, not each Event's frozen
            // TrailNameSnapshot - this widget groups and labels by TrailId (stable
            // Trail identity, see CLAUDE.md, "Analytics and identity"), so the label
            // is "whatever this Trail is called now," not a per-Event historical
            // display value. Contrast with BuildEventHistoryAsync above, whose
            // per-row TrailName is the frozen snapshot.
            var completedEvents = await completedQuery
                .Select(e => new { e.Id, e.TrailId, TrailName = e.Trail != null ? e.Trail.Name : "Unknown Trail" })
                .ToListAsync();

            if (completedEvents.Count == 0)
            {
                return new List<TrailUsageRowViewModel>();
            }

            var completedEventIds = completedEvents.Select(e => e.Id).ToList();

            // Trail Usage's "Total Participants" is actual historical
            // participation (who actually joined a completed hike), not the
            // Capacity/Registered ActiveStatuses count above - Accepted only,
            // matching the app's established post-event participation rule
            // (CLAUDE.md: "Post-event flows count only Accepted"). A single
            // grouped query keeps this join-duplication-free.
            var acceptedCounts = await _context.EventRegistrations.AsNoTracking()
                .Where(r => completedEventIds.Contains(r.EventId) && r.Status == "Accepted")
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            return completedEvents
                .GroupBy(e => e.TrailId)
                .Select(g => new TrailUsageRowViewModel
                {
                    TrailId = g.Key,
                    TrailName = g.First().TrailName,
                    TotalEvents = g.Count(),
                    TotalParticipants = g.Sum(e => acceptedCounts.TryGetValue(e.Id, out var c) ? c : 0)
                })
                .OrderByDescending(t => t.TotalEvents)
                .ThenByDescending(t => t.TotalParticipants)
                .ThenBy(t => t.TrailName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TrailId)
                .ToList();
        }

        private async Task<List<FeedbackRowViewModel>> BuildFeedbackAsync(RecordsScope scope)
        {
            var query = _context.EventFeedbacks.AsNoTracking()
                .Where(f => f.Event != null && f.Rating >= 1 && f.Rating <= 5);

            if (scope.ScopedOrganizerId != null)
            {
                query = query.Where(f => f.Event!.OrganizerId == scope.ScopedOrganizerId);
            }

            var feedback = await query
                .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                .Select(f => new FeedbackRowViewModel
                {
                    Id = f.Id,
                    ParticipantName = f.User != null ? (f.User.FirstName + " " + f.User.LastName).Trim() : "Anonymous",
                    EventTitle = f.Event!.EventTitle,
                    Rating = f.Rating,
                    CreatedAt = f.CreatedAt,
                    DifficultyExperience = f.DifficultyExperience,
                    TrailCondition = f.TrailCondition,
                    TrailSignage = f.TrailSignage,
                    WaterSourceAvailability = f.WaterSourceAvailability,
                    HazardsEncountered = f.HazardsEncountered,
                    PreEventCommunication = f.PreEventCommunication,
                    SafetyManagement = f.SafetyManagement,
                    GroupManagement = f.GroupManagement,
                    Comment = f.Comment
                })
                .ToListAsync();

            return feedback;
        }

        private class RecordsScope
        {
            public bool IsAdmin { get; set; }

            // false => the caller supplied an organizerId that did not
            // resolve to a current Organizer-role user. The caller (Index or
            // Export) must reject the request with a generic 404 before
            // touching any Records data - never fall back to ScopedOrganizerId
            // being treated as "All Organizers".
            public bool IsValid { get; set; }

            // null => no Organizer restriction (Admin, "All Organizers").
            // Non-null => restrict to exactly this OrganizerId (an Organizer's
            // own account, or an Admin's validated filter selection).
            public string? ScopedOrganizerId { get; set; }
            public Dictionary<string, string> OrganizerNames { get; set; } = new();
            public List<OrganizerOptionViewModel> OrganizerOptions { get; set; } = new();
        }
    }

    public class RecordsViewModel
    {
        public bool IsAdmin { get; set; }
        public List<OrganizerOptionViewModel> OrganizerOptions { get; set; } = new();
        public string? SelectedOrganizerId { get; set; }

        public List<EventHistoryRowViewModel> EventHistory { get; set; } = new();
        public List<RegistrationRowViewModel> Registrations { get; set; } = new();
        public List<TrailUsageRowViewModel> TrailUsage { get; set; } = new();
        public List<FeedbackRowViewModel> Feedbacks { get; set; } = new();
        public double FeedbackAverage { get; set; }
        public int FeedbackCount { get; set; }
    }

    public class OrganizerOptionViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class EventHistoryRowViewModel
    {
        public int Id { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public string TrailName { get; set; } = string.Empty;
        public string? OrganizerId { get; set; }
        public string OrganizerName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public DateTime EventDateTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public int Registered { get; set; }
    }

    public class RegistrationRowViewModel
    {
        public int Id { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string EventTitle { get; set; } = string.Empty;
        public string? OrganizerId { get; set; }
        public string OrganizerName { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class TrailUsageRowViewModel
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public int TotalEvents { get; set; }
        public int TotalParticipants { get; set; }
    }

    public class FeedbackRowViewModel
    {
        public int Id { get; set; }
        public string ParticipantName { get; set; } = string.Empty;
        public string EventTitle { get; set; } = string.Empty;
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? DifficultyExperience { get; set; }
        public string? TrailCondition { get; set; }
        public string? TrailSignage { get; set; }
        public string? WaterSourceAvailability { get; set; }
        public string? HazardsEncountered { get; set; }
        public string? PreEventCommunication { get; set; }
        public string? SafetyManagement { get; set; }
        public string? GroupManagement { get; set; }
        public string? Comment { get; set; }
    }
}
