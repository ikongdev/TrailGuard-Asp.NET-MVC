using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;
using Microsoft.AspNetCore.Authorization;

namespace TrailGuard.Controllers
{
    public class DeleteEventRequest
    {
        public int Id { get; set; }
    }
    [Authorize(Roles = "Admin,Organizer")]
    public class EventController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly WeatherService _weatherService;
        private readonly ILogger<EventController> _logger;
        private readonly RoleAssignmentService _roleAssignmentService;

        public const string DefaultSortOrder = "date_asc";

        // Soonest/Latest only - name-based sorting was removed (see the Event
        // Management redesign). Anything unrecognized - missing, blank, or a stale
        // bookmark from a removed option - normalizes to DefaultSortOrder rather
        // than reaching the switch below unnormalized.
        private static readonly HashSet<string> AllowedSortOrders = new(StringComparer.Ordinal)
        {
            "date_asc", "date_desc",
        };

        // Upcoming and Completed always lead, matching CLAUDE.md's Event Lifecycle
        // model, regardless of whether either currently has any events. Everything
        // else (Cancelled, or a stray value written through the free-text EditEvent
        // Status field) is data-driven - it only ever appears if some event actually
        // has it, so the listing never invents a status nobody has.
        private static readonly string[] FixedStatusPriority = { "Upcoming", "Completed" };

        public EventController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<ApplicationUser> userManager, WeatherService weatherService, ILogger<EventController> logger, RoleAssignmentService roleAssignmentService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _weatherService = weatherService;
            _logger = logger;
            _roleAssignmentService = roleAssignmentService;
        }

        // Single ownership rule for every Organizer-facing Event action in
        // this controller: an Admin (and therefore a dual-role
        // Admin+Organizer account, which always also holds the Admin role)
        // retains full access; a pure Organizer may only act on an Event
        // whose stable OrganizerId matches their own account. A null
        // OrganizerId (an unresolved legacy Event - see CLAUDE.md) or a
        // different Organizer's ID both deny access outright - ownership is
        // never inferred from OrganizedBy, email, or display name.
        private async Task<bool> CanManageEventAsync(Event eventItem, ApplicationUser currentUser)
        {
            if (await _userManager.IsInRoleAsync(currentUser, "Admin")) return true;
            return eventItem.OrganizerId != null && eventItem.OrganizerId == currentUser.Id;
        }

        public async Task<IActionResult> Index(string searchString, string status, string trailId, string difficulty, string sortOrder)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var normalizedSearch = (searchString ?? string.Empty).Trim();
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "All" : status;
            var normalizedTrailId = string.IsNullOrWhiteSpace(trailId) ? "All" : trailId;
            var normalizedDifficulty = string.IsNullOrWhiteSpace(difficulty) ? "All" : difficulty;
            var normalizedSort = AllowedSortOrders.Contains(sortOrder ?? string.Empty) ? sortOrder! : DefaultSortOrder;

            ViewData["CurrentFilter"] = normalizedSearch;
            ViewData["CurrentStatus"] = normalizedStatus;
            ViewData["CurrentTrailId"] = normalizedTrailId;
            ViewData["CurrentDifficulty"] = normalizedDifficulty;
            ViewData["CurrentSort"] = normalizedSort;

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();

            var organizers = await _userManager.GetUsersInRoleAsync("Organizer");
            ViewBag.Organizers = organizers.ToList();

            var actualStatuses = await _context.Events.Select(e => e.Status).Distinct().ToListAsync();
            var orderedStatuses = FixedStatusPriority
                .Concat(actualStatuses.Except(FixedStatusPriority, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
                .ToList();

            // Independent of every filter below, by design - this is the same
            // Status == "Upcoming" classifier the Upcoming section itself uses, just
            // counted against the whole catalog instead of the filtered subset, so
            // the header summary and an unfiltered Upcoming section always agree.
            var upcomingEventsCount = await _context.Events.CountAsync(e => e.Status == "Upcoming");

            IQueryable<Event> query = _context.Events.Include(e => e.Trail);

            if (!string.IsNullOrEmpty(normalizedSearch))
            {
                query = query.Where(e =>
                    e.EventTitle.Contains(normalizedSearch) ||
                    e.Location.Contains(normalizedSearch) ||
                    (e.Trail != null && e.Trail.Name.Contains(normalizedSearch)));
            }

            if (normalizedStatus != "All")
            {
                query = query.Where(e => e.Status == normalizedStatus);
            }

            if (normalizedTrailId != "All" && int.TryParse(normalizedTrailId, out var trailIdValue))
            {
                query = query.Where(e => e.TrailId == trailIdValue);
            }

            if (normalizedDifficulty != "All")
            {
                query = query.Where(e => e.Difficulty == normalizedDifficulty);
            }

            // Soonest/Latest sort by the event's actual schedule - date, then start
            // time - never by title or DateCreated, with Id as the final, deterministic
            // tiebreaker so two events sharing a date and time still sort consistently.
            query = normalizedSort switch
            {
                "date_desc" => query.OrderByDescending(e => e.EventDate).ThenByDescending(e => e.EventTime).ThenByDescending(e => e.Id),
                _ => query.OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id),
            };

            var filteredEvents = await query.ToListAsync();

            var eventIds = filteredEvents.Select(e => e.Id).ToList();
            var capacityCounts = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            foreach (var eventItem in filteredEvents)
            {
                eventItem.RegisteredCount = capacityCounts.TryGetValue(eventItem.Id, out var count) ? count : 0;
            }

            // ToLookup over the already status-priority-known list preserves each
            // status's internal Soonest/Latest order (LINQ-to-Objects grouping is
            // stable) - this just arranges the groups in the fixed section order and
            // drops any status with zero matches, per "do not render empty sections."
            var eventsByStatus = filteredEvents.ToLookup(e => e.Status);
            var statusGroups = orderedStatuses
                .Where(s => normalizedStatus == "All" || s == normalizedStatus)
                .Select(s => new EventStatusGroupViewModel { Status = s, Events = eventsByStatus[s].ToList() })
                .Where(g => g.Events.Count > 0)
                .ToList();

            var viewModel = new EventManagementViewModel
            {
                StatusGroups = statusGroups,
                AvailableStatuses = orderedStatuses,
                UpcomingEventsCount = upcomingEventsCount,
                HasAnyResults = filteredEvents.Count > 0
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<JsonResult> GetTrailDetails(int trailId)
        {
            var trail = await _context.Trails.FindAsync(trailId);
            if (trail == null)
            {
                return Json(new { success = false, message = "Trail not found" });
            }

            return Json(new
            {
                success = true,
                location = trail.Location,
                elevationGainMeters = trail.ElevationGainMeters,
                distance = trail.DistanceKm,
                terrain = trail.Terrain,
                trailClass = trail.TrailClass,
                trailClassLabel = DifficultyCalculator.TrailClassLabel(trail.TrailClass)
            });
        }

        [HttpGet]
        public async Task<JsonResult> GetCalculatedDifficulty(int trailId)
        {
            var trail = await _context.Trails.FindAsync(trailId);
            if (trail == null)
            {
                return Json(new { success = false });
            }

            return Json(new
            {
                success = true,
                difficulty = DifficultyCalculator.ComputeDifficulty(trail),
                suggestedDurationHours = Math.Round(DifficultyCalculator.SuggestedDurationHours(trail), 1)
            });
        }

        [HttpGet]
        public async Task<JsonResult> GetWeatherForecast(int trailId, DateTime eventDate, int? eventId = null)
        {
            try
            {
                var trail = await _context.Trails.FindAsync(trailId);
                if (trail == null)
                {
                    return Json(new { success = false, message = "Trail not found" });
                }

                var forecast = await _weatherService.GetWeatherForecastAsync(trail.Location, eventDate);
                var reminder = forecast.SuggestedReminder;

                if (eventId.HasValue)
                {
                    var existingEvent = await _context.Events.FindAsync(eventId.Value);
                    if (existingEvent != null &&
                        !string.IsNullOrEmpty(existingEvent.WeatherReminder) &&
                        existingEvent.WeatherRiskLevel == forecast.RiskLevel)
                    {
                        // Organizer already edited the reminder and the risk level hasn't changed since — keep their wording.
                        reminder = existingEvent.WeatherReminder;
                    }
                }

                return Json(new
                {
                    success = true,
                    forecastDetails = forecast.ForecastDetails,
                    riskLevel = forecast.RiskLevel,
                    suggestedReminder = reminder,
                    // Structured fields for Add Event's forecast result card.
                    // Edit Event's own weather JS predates these and simply
                    // ignores them - forecastDetails/riskLevel/suggestedReminder
                    // above are unchanged, so it keeps working as before.
                    condition = forecast.Condition,
                    weatherCode = forecast.WeatherCode,
                    temperatureMinC = forecast.TemperatureMinC,
                    temperatureMaxC = forecast.TemperatureMaxC,
                    expectedRainfallMm = forecast.ExpectedRainfallMm,
                    windSpeedKmh = forecast.WindSpeedKmh,
                    windDescription = forecast.WindDescription,
                    updatedAt = forecast.UpdatedAt,
                    unavailableReason = forecast.UnavailableReason
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch weather forecast for trail {TrailId} on {EventDate}.", trailId, eventDate);
                return Json(new { success = false, message = "Weather forecast is temporarily unavailable. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> AddEvent([FromBody] EventCreateModel model)
        {
            try
            {
                var trail = await _context.Trails.FindAsync(model.TrailId);
                if (trail == null)
                {
                    return Json(new { success = false, message = "Trail not found" });
                }

                // Organizer assignment is resolved entirely server-side from the
                // authenticated user's role - a client-submitted OrganizerId is
                // only ever consulted for an Admin caller, and even then it must
                // resolve to an account actually holding the Organizer role. See
                // CLAUDE.md's Add Event modal task: the old endpoint trusted
                // whatever OrganizedBy value the browser sent, which allowed
                // organizer spoofing.
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Unable to verify your account. Please sign in again." });
                }

                ApplicationUser organizerAccount;
                if (await _userManager.IsInRoleAsync(currentUser, "Admin"))
                {
                    if (string.IsNullOrWhiteSpace(model.OrganizerId))
                    {
                        return Json(new { success = false, message = "Please select an organizer." });
                    }

                    var selectedAccount = await _userManager.FindByIdAsync(model.OrganizerId);
                    if (selectedAccount == null || !await _userManager.IsInRoleAsync(selectedAccount, "Organizer"))
                    {
                        return Json(new { success = false, message = "The selected organizer is not valid." });
                    }

                    organizerAccount = selectedAccount;
                }
                else
                {
                    // Not an Admin, so [Authorize(Roles = "Admin,Organizer")] on
                    // this controller guarantees the caller holds the Organizer
                    // role. Any OrganizerId the client sent is ignored.
                    organizerAccount = currentUser;
                }

                var organizerName = $"{organizerAccount.FirstName} {organizerAccount.LastName}";

                // Add Event submits structured schedules, never a raw
                // PickupPoints string - the server (not the browser) is what
                // turns validated schedules into the canonical stored lines.
                var scheduleResult = PickupScheduleHelper.ValidateAndFormat(model.PickupSchedules);
                if (!scheduleResult.Success)
                {
                    return Json(new { success = false, message = scheduleResult.Error });
                }

                // A structured weather snapshot is optional - Add Event only
                // sends one when a successful forecast is currently in its
                // state. It's never trusted as submitted: TryValidateForSubmission
                // re-checks every field's shape/range and confirms the
                // snapshot's own TrailId/ForecastDate actually match this
                // same request's TrailId/EventDate, so a stale snapshot left
                // over from a since-changed trail or date can't be saved as
                // if it were current. No match or no submission at all both
                // mean a null snapshot - never fabricated.
                string? weatherSnapshotJson = null;
                if (WeatherSnapshotHelper.TryValidateForSubmission(model.WeatherSnapshot, model.TrailId, model.EventDate, out var snapshotRejectReason))
                {
                    weatherSnapshotJson = WeatherSnapshotHelper.Serialize(model.WeatherSnapshot!);
                }
                else if (model.WeatherSnapshot != null)
                {
                    _logger.LogWarning("Discarded a submitted Add Event weather snapshot: {Reason}", snapshotRejectReason);
                }

                var newEvent = new Event
                {
                    EventTitle = model.EventTitle,
                    Description = model.Description,
                    EventDate = model.EventDate,
                    EventTime = model.EventTime,
                    TrailId = model.TrailId,
                    Location = trail.Location,
                    Difficulty = DifficultyCalculator.ComputeDifficulty(trail),
                    EstimatedDuration = model.EstimatedDuration,
                    Capacity = model.Capacity,
                    OrganizedBy = organizerName,
                    OrganizerId = organizerAccount.Id,
                    Status = "Upcoming",
                    WeatherForecastAdvisory = model.WeatherForecastAdvisory,
                    WeatherRiskLevel = model.WeatherRiskLevel,
                    WeatherReminder = model.WeatherReminder,
                    WeatherSnapshotJson = weatherSnapshotJson,
                    NotesAndReminders = model.NotesAndReminders,
                    PaymentDetails = model.PaymentDetails,
                    PickupPoints = string.Join("\n", scheduleResult.CanonicalLines)
                };

                _context.Events.Add(newEvent);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event added successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create event for trail {TrailId}.", model.TrailId);
                return Json(new { success = false, message = "Something went wrong while creating the event. Please try again." });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetEvent(int id)
        {
            try
            {
                var eventItem = await _context.Events
                    .Include(e => e.Trail)
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var trail = eventItem.Trail;

                // Defensive read - malformed/unsupported stored JSON
                // degrades to null (treated the same as a legacy event that
                // never had one) rather than breaking this endpoint. Never
                // returned as a raw string; only as this validated,
                // explicitly-shaped object, with field names matching
                // GetWeatherForecast's own response so the same client-side
                // renderer can consume either one.
                var weatherSnapshot = WeatherSnapshotHelper.TryDeserialize(eventItem.WeatherSnapshotJson, _logger);

                return Json(new
                {
                    success = true,
                    id = eventItem.Id,
                    eventTitle = eventItem.EventTitle,
                    description = eventItem.Description,
                    eventDate = eventItem.EventDate.ToString("yyyy-MM-dd"),
                    eventTime = eventItem.EventTime.ToString(),
                    trailId = eventItem.TrailId,
                    trailName = trail?.Name,
                    // Trail preview fields the Edit Event modal's Step 1 cards
                    // show on open - returned here so hydration never needs a
                    // GetTrailDetails/GetCalculatedDifficulty round trip (which
                    // would mean dispatching a synthetic 'change' event on the
                    // trail select to trigger them, and that would also fire
                    // the weather refetch that hydration must NOT trigger).
                    trailLocation = trail?.Location,
                    trailDistanceKm = trail?.DistanceKm,
                    trailElevationGainMeters = trail?.ElevationGainMeters,
                    trailTerrain = trail?.Terrain,
                    trailClass = trail?.TrailClass,
                    trailClassLabel = trail != null ? DifficultyCalculator.TrailClassLabel(trail.TrailClass) : null,
                    trailDifficulty = trail != null ? DifficultyCalculator.ComputeDifficulty(trail) : eventItem.Difficulty,
                    estimatedDuration = eventItem.EstimatedDuration,
                    capacity = eventItem.Capacity,
                    organizedBy = eventItem.OrganizedBy,
                    weatherForecastAdvisory = eventItem.WeatherForecastAdvisory,
                    weatherRiskLevel = eventItem.WeatherRiskLevel,
                    weatherReminder = eventItem.WeatherReminder,
                    announcements = eventItem.NotesAndReminders,
                    paymentDetails = eventItem.PaymentDetails,
                    pickupPoints = eventItem.PickupPoints,
                    // Structured hydration for the Pickup Schedules builder -
                    // legacy lines without a valid canonical time suffix come
                    // back with time: null, requiresTime: true rather than
                    // being dropped or given an invented time.
                    pickupSchedules = PickupScheduleHelper.ParseForEditing(eventItem.PickupPoints).Select(s => new
                    {
                        location = s.Location,
                        time = s.Time,
                        requiresTime = s.RequiresTime
                    }),
                    // Structured hydration for the modern weather card - null
                    // when the event has never had a successfully-validated
                    // snapshot saved (legacy events, or one that failed
                    // validation). trailId/forecastDate let the client decide
                    // whether this snapshot still matches the event's current
                    // trail/date (rendered as the live card) or belongs to a
                    // previous context (rendered as a stale/previous notice) -
                    // see the Edit Event weather hydration logic.
                    weatherSnapshot = weatherSnapshot == null ? null : new
                    {
                        trailId = weatherSnapshot.TrailId,
                        forecastDate = weatherSnapshot.ForecastDate.ToString("yyyy-MM-dd"),
                        condition = weatherSnapshot.Condition,
                        weatherCode = weatherSnapshot.WeatherCode,
                        temperatureMinC = weatherSnapshot.TemperatureMinC,
                        temperatureMaxC = weatherSnapshot.TemperatureMaxC,
                        expectedRainfallMm = weatherSnapshot.ExpectedRainfallMm,
                        windSpeedKmh = weatherSnapshot.WindSpeedKmh,
                        windDescription = weatherSnapshot.WindDescription,
                        riskLevel = weatherSnapshot.RiskLevel,
                        updatedAt = weatherSnapshot.UpdatedAt
                    },
                    status = eventItem.Status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load event {EventId} for editing.", id);
                return Json(new { success = false, message = "Unable to load this event. Please try again." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Index");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Index");
            }

            // Assess Participants is an Organizer-only feature - a dual-role
            // Admin+Organizer account holds the Admin role too and follows
            // the Admin branch everywhere else in this app, so it's excluded
            // here as well, not just given access via CanManageEventAsync
            // above. Ownership is re-derived explicitly (rather than assumed
            // from the page-access check just passed) since this flag
            // controls whether a sensitive action is even shown.
            ViewBag.CanAssessParticipants = eventItem.Status == "Completed"
                && await _userManager.IsInRoleAsync(currentUser, "Organizer")
                && !await _userManager.IsInRoleAsync(currentUser, "Admin")
                && eventItem.OrganizerId != null
                && eventItem.OrganizerId == currentUser.Id;

            // View Comparison's destination (OrganizerController.EventComparison)
            // is Organizer-only and, unlike assessment, does NOT exclude a
            // dual-role Admin+Organizer account - it only requires ownership.
            // This flag mirrors that exact policy so the link is never shown
            // to a viewer its destination would deny (including an Admin-only
            // account, which lacks the Organizer role entirely).
            ViewBag.CanViewComparison = eventItem.Status == "Completed"
                && await _userManager.IsInRoleAsync(currentUser, "Organizer")
                && eventItem.OrganizerId != null
                && eventItem.OrganizerId == currentUser.Id;

            var capacityRegistrations = await _context.EventRegistrations
                .Where(r => r.EventId == id && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .ToListAsync();

            var allRegistrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Where(r => r.EventId == id && r.Status != "Rejected" && r.Status != "Cancelled")
                .ToListAsync();

            ViewBag.Registrations = allRegistrations;
            ViewBag.RegisteredCount = capacityRegistrations.Count;
            ViewBag.AvailableSlots = eventItem.Capacity - capacityRegistrations.Count;

            // Registered Participants card (Views/Event/Details.cshtml): same
            // Accepted/Pending-only, RegisteredAt-ascending set this page has always
            // shown - built here, not in the view, so the same pass that decides what
            // renders also decides each row's Profile-link eligibility from one
            // bounded role lookup, never a query (or a ProfileAccessService.ResolveAsync
            // call) per row.
            var joinedRegistrations = allRegistrations
                .Where(r => r.Status == "Accepted" || r.Status == "Pending")
                .OrderBy(r => r.RegisteredAt)
                .ToList();

            // One check for the viewer (not per row). CanManageEventAsync above
            // already confirms this viewer is Admin, or the Organizer who owns this
            // Event - a conflicted Admin+Organizer account still evaluates to a
            // single clean status here since OperationalRolePolicy.Evaluate treats
            // "holds more than one operational role" as Conflict, which grants no
            // Profile-link privilege below (matching ProfileAccessService).
            var viewerIntegrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(currentUser));

            var targetUserIds = joinedRegistrations
                .Where(r => r.User != null)
                .Select(r => r.UserId)
                .Distinct()
                .ToList();

            var targetIntegrities = await _roleAssignmentService.GetRoleIntegrityStatusesAsync(targetUserIds);

            var participantRows = joinedRegistrations.Select(r =>
            {
                var canView = false;
                if (r.User != null &&
                    targetIntegrities.TryGetValue(r.UserId, out var targetStatus) &&
                    targetStatus == RoleIntegrityStatus.Participant)
                {
                    canView = viewerIntegrity.Status switch
                    {
                        RoleIntegrityStatus.Admin => true,
                        RoleIntegrityStatus.Organizer => r.User.IsActive && ProfileAccessPolicy.AllowsOrganizerRelationship(r.Status),
                        _ => false
                    };
                }

                return new EventParticipantRowViewModel
                {
                    ParticipantName = r.ParticipantName,
                    ProfilePictureUrl = r.User?.ProfilePictureUrl,
                    Status = r.Status,
                    PublicProfileId = r.User?.PublicProfileId ?? Guid.Empty,
                    CanViewProfile = canView
                };
            }).ToList();

            ViewBag.ParticipantRows = participantRows;

            if (!string.IsNullOrEmpty(eventItem.OrganizedBy))
            {
                var organizer = await _context.Users
                    .FirstOrDefaultAsync(u => 
                        (u.FirstName + " " + u.LastName) == eventItem.OrganizedBy ||
                        (u.FirstName + " " + u.MiddleName + " " + u.LastName) == eventItem.OrganizedBy ||
                        u.Email == eventItem.OrganizedBy ||
                        u.Id == eventItem.OrganizedBy
                    );
                ViewBag.Organizer = organizer;
            }
            
            ViewBag.Trail = eventItem.Trail;
            return View(eventItem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> CompleteEvent([FromBody] CompleteEventRequest request)
        {
            try
            {
                var eventItem = await _context.Events.FindAsync(request.Id);
                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                if (eventItem.Status != "Upcoming")
                {
                    return Json(new { success = false, message = "Only upcoming events can be marked as completed" });
                }

                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var organizer = await _userManager.FindByIdAsync(userId ?? "");
                var organizerName = organizer != null ? $"{organizer.FirstName} {organizer.LastName}" : "Organizer";

                eventItem.Status = "Completed";
                eventItem.CompletedAt = DateTime.Now;
                eventItem.CompletedBy = organizerName;
                eventItem.DateUpdated = DateTime.Now;

                var registrationsToVoid = await _context.EventRegistrations
                    .Where(r => r.EventId == request.Id &&
                        (r.Status == "Pending" || r.Status == "Awaiting Payment" || r.Status == "For Payment Verification"))
                    .ToListAsync();

                foreach (var registration in registrationsToVoid)
                {
                    registration.Status = "Voided";
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event marked as completed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete event {EventId}.", request.Id);
                return Json(new { success = false, message = "Unable to complete the event right now. Please try again." });
            }
        }

        public class CompleteEventRequest
        {
            public int Id { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> CancelEvent([FromBody] CancelEventRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Json(new { success = false, message = "A cancellation reason is required" });
                }

                var eventItem = await _context.Events.FindAsync(request.Id);
                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                if (eventItem.Status != "Upcoming")
                {
                    return Json(new { success = false, message = "Only upcoming events can be cancelled" });
                }

                eventItem.Status = "Cancelled";
                eventItem.CancelledAt = DateTime.Now;
                eventItem.CancellationReason = request.Reason;
                eventItem.DateUpdated = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event cancelled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel event {EventId}.", request.Id);
                return Json(new { success = false, message = "Unable to cancel the event right now. Please try again." });
            }
        }

        public class CancelEventRequest
        {
            public int Id { get; set; }
            public string Reason { get; set; } = string.Empty;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> RescheduleEvent([FromBody] RescheduleEventRequest request)
        {
            try
            {
                var eventItem = await _context.Events.FindAsync(request.Id);
                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                if (eventItem.Status != "Upcoming")
                {
                    return Json(new { success = false, message = "Only upcoming events can be rescheduled" });
                }

                eventItem.EventDate = request.NewDate;
                eventItem.EventTime = request.NewTime;
                eventItem.DateUpdated = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event rescheduled" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reschedule event {EventId}.", request.Id);
                return Json(new { success = false, message = "Unable to reschedule the event right now. Please try again." });
            }
        }

        public class RescheduleEventRequest
        {
            public int Id { get; set; }
            public DateTime NewDate { get; set; }
            public TimeSpan NewTime { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> EditEvent([FromBody] EventEditModel model)
        {
            try
            {
                // Everything below is validation - existingEvent's properties
                // are only ever touched once every check has passed, so a
                // rejected request never leaves a partial mutation to save.
                var existingEvent = await _context.Events.FindAsync(model.Id);
                if (existingEvent == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var trail = await _context.Trails.FindAsync(model.TrailId);
                if (trail == null)
                {
                    return Json(new { success = false, message = "Trail not found" });
                }

                // Organizer assignment is resolved the same way Add Event
                // resolves it - see AddEvent's own comment for the full
                // rationale. The one Edit-specific difference: a non-Admin
                // (Organizer) caller does NOT get assigned as the organizer the
                // way a new event's creator does - editing an existing event
                // must never reassign it to whoever happens to be editing it,
                // so the event's current OrganizedBy is preserved untouched
                // instead.
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Unable to verify your account. Please sign in again." });
                }

                string organizerName;
                string? resolvedOrganizerId;
                if (await _userManager.IsInRoleAsync(currentUser, "Admin"))
                {
                    if (string.IsNullOrWhiteSpace(model.OrganizerId))
                    {
                        return Json(new { success = false, message = "Please select an organizer." });
                    }

                    var selectedAccount = await _userManager.FindByIdAsync(model.OrganizerId);
                    if (selectedAccount == null || !await _userManager.IsInRoleAsync(selectedAccount, "Organizer"))
                    {
                        return Json(new { success = false, message = "The selected organizer is not valid." });
                    }

                    organizerName = $"{selectedAccount.FirstName} {selectedAccount.LastName}";
                    resolvedOrganizerId = selectedAccount.Id;
                }
                else
                {
                    // Not an Admin, so [Authorize(Roles = "Admin,Organizer")] on
                    // this controller guarantees the caller holds the Organizer
                    // role. Ownership is checked against the stable
                    // OrganizerId only - never OrganizedBy, a display-name
                    // snapshot that is not a safe identity key - and a null
                    // OrganizerId (an unresolved legacy Event) is never
                    // treated as owned by whichever Organizer happens to ask.
                    // Any OrganizerId the client sent is ignored either way;
                    // the event keeps its existing organizer assignment.
                    if (existingEvent.OrganizerId == null || existingEvent.OrganizerId != currentUser.Id)
                    {
                        return Json(new { success = false, message = "Event not found" });
                    }

                    organizerName = existingEvent.OrganizedBy ?? string.Empty;
                    resolvedOrganizerId = existingEvent.OrganizerId;
                }

                var scheduleResult = PickupScheduleHelper.ValidateAndFormat(model.PickupSchedules);
                if (!scheduleResult.Success)
                {
                    return Json(new { success = false, message = scheduleResult.Error });
                }

                // Weather snapshot: replace only when a valid new one is
                // submitted for THIS event's TrailId/EventDate; otherwise
                // preserve whatever the event already had. This covers every
                // case the same way:
                //   - no new snapshot submitted, trail/date unchanged -> the
                //     organizer didn't touch weather this edit; keep the
                //     existing snapshot as-is.
                //   - trail/date changed but no successful matching refresh
                //     was submitted -> the old snapshot (with its OWN,
                //     original trail/date) is preserved rather than rewritten
                //     to pretend it matches the new context.
                //   - a valid snapshot matching the submitted trail/date IS
                //     supplied -> it replaces the stored one.
                // A failed/rejected submission never overwrites a valid
                // stored snapshot with null.
                var weatherSnapshotJson = existingEvent.WeatherSnapshotJson;
                if (WeatherSnapshotHelper.TryValidateForSubmission(model.WeatherSnapshot, model.TrailId, model.EventDate, out var snapshotRejectReason))
                {
                    weatherSnapshotJson = WeatherSnapshotHelper.Serialize(model.WeatherSnapshot!);
                }
                else if (model.WeatherSnapshot != null)
                {
                    _logger.LogWarning("Discarded a submitted Edit Event weather snapshot for event {EventId}: {Reason}", model.Id, snapshotRejectReason);
                }

                existingEvent.EventTitle = model.EventTitle;
                existingEvent.Description = model.Description;
                existingEvent.EventDate = model.EventDate;
                existingEvent.EventTime = model.EventTime;
                existingEvent.TrailId = model.TrailId;
                existingEvent.Location = trail.Location;
                existingEvent.Difficulty = DifficultyCalculator.ComputeDifficulty(trail);
                existingEvent.EstimatedDuration = model.EstimatedDuration;
                existingEvent.Capacity = model.Capacity;
                existingEvent.OrganizedBy = organizerName;
                existingEvent.OrganizerId = resolvedOrganizerId;
                existingEvent.Status = model.Status ?? existingEvent.Status;
                existingEvent.WeatherForecastAdvisory = model.WeatherForecastAdvisory;
                existingEvent.WeatherRiskLevel = model.WeatherRiskLevel ?? existingEvent.WeatherRiskLevel;
                existingEvent.WeatherReminder = model.WeatherReminder ?? existingEvent.WeatherReminder;
                existingEvent.WeatherSnapshotJson = weatherSnapshotJson;
                existingEvent.NotesAndReminders = model.NotesAndReminders;
                existingEvent.PaymentDetails = model.PaymentDetails;
                existingEvent.PickupPoints = string.Join("\n", scheduleResult.CanonicalLines);
                existingEvent.DateUpdated = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event updated successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update event {EventId}.", model.Id);
                return Json(new { success = false, message = "Something went wrong while updating the event. Please try again." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> DeleteEvent([FromBody] DeleteEventRequest request)
        {
            try
            {
                var eventItem = await _context.Events.FindAsync(request.Id);

                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null || !await CanManageEventAsync(eventItem, currentUser))
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                _context.Events.Remove(eventItem);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete event {EventId}.", request.Id);
                return Json(new { success = false, message = "Unable to delete the event right now. Please try again." });
            }
        }
    }
}