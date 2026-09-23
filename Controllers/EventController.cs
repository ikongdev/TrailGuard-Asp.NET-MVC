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





        private static readonly HashSet<string> AllowedSortOrders = new(StringComparer.Ordinal)
        {
            "date_asc", "date_desc",
        };






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
            ViewBag.ActiveTrails = await _context.Trails.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();

            var organizers = await _userManager.GetUsersInRoleAsync("Organizer");
            ViewBag.Organizers = organizers.ToList();

            var actualStatuses = await _context.Events.Select(e => e.Status).Distinct().ToListAsync();
            var orderedStatuses = FixedStatusPriority
                .Concat(actualStatuses.Except(FixedStatusPriority, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
                .ToList();





            var upcomingEventsCount = await _context.Events.CountAsync(e => e.Status == "Upcoming");

            IQueryable<Event> query = _context.Events;

            if (!string.IsNullOrEmpty(normalizedSearch))
            {



                query = query.Where(e =>
                    e.EventTitle.Contains(normalizedSearch) ||
                    e.Location.Contains(normalizedSearch) ||
                    e.TrailNameSnapshot.Contains(normalizedSearch));
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
                typicalDurationHours = trail.TypicalDurationHours,
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
                difficulty = DifficultyCalculator.ComputeDifficulty(trail)
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

                        reminder = existingEvent.WeatherReminder;
                    }
                }

                return Json(new
                {
                    success = true,
                    forecastDetails = forecast.ForecastDetails,
                    riskLevel = forecast.RiskLevel,
                    suggestedReminder = reminder,




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







                if (!trail.IsActive)
                {
                    return Json(new { success = false, message = "The selected trail is no longer active. Please choose another trail." });
                }








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



                    organizerAccount = currentUser;
                }

                var organizerName = $"{organizerAccount.FirstName} {organizerAccount.LastName}";




                var scheduleResult = PickupScheduleHelper.ValidateAndFormat(model.PickupSchedules);
                if (!scheduleResult.Success)
                {
                    return Json(new { success = false, message = scheduleResult.Error });
                }










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





                EventTrailSnapshotHelper.CaptureSnapshot(newEvent, trail);

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






                if (eventItem.Status == "Completed")
                {
                    return Json(new { success = false, message = "Completed events are read-only and cannot be edited." });
                }








                var weatherSnapshot = WeatherSnapshotHelper.TryDeserialize(eventItem.WeatherSnapshotJson, _logger);











                var trailIsActive = await _context.Trails
                    .Where(t => t.Id == eventItem.TrailId)
                    .Select(t => (bool?)t.IsActive)
                    .FirstOrDefaultAsync() ?? true;

                return Json(new
                {
                    success = true,
                    id = eventItem.Id,
                    eventTitle = eventItem.EventTitle,
                    description = eventItem.Description,
                    eventDate = eventItem.EventDate.ToString("yyyy-MM-dd"),
                    eventTime = eventItem.EventTime.ToString(),
                    trailId = eventItem.TrailId,
                    trailName = eventItem.TrailNameSnapshot,
                    trailIsActive = trailIsActive,











                    trailLocation = eventItem.Location,
                    trailDistanceKm = eventItem.TrailDistanceKmSnapshot,
                    trailElevationGainMeters = eventItem.TrailElevationGainMetersSnapshot,
                    trailTerrain = eventItem.TrailTerrainSnapshot,
                    trailClass = eventItem.TrailClassSnapshot,
                    trailClassLabel = DifficultyCalculator.TrailClassLabel(eventItem.TrailClassSnapshot),
                    trailDifficulty = eventItem.Difficulty,
                    estimatedDuration = eventItem.EstimatedDuration,
                    capacity = eventItem.Capacity,
                    organizedBy = eventItem.OrganizedBy,
                    weatherForecastAdvisory = eventItem.WeatherForecastAdvisory,
                    weatherRiskLevel = eventItem.WeatherRiskLevel,
                    weatherReminder = eventItem.WeatherReminder,
                    announcements = eventItem.NotesAndReminders,
                    paymentDetails = eventItem.PaymentDetails,
                    pickupPoints = eventItem.PickupPoints,




                    pickupSchedules = PickupScheduleHelper.ParseForEditing(eventItem.PickupPoints).Select(s => new
                    {
                        location = s.Location,
                        time = s.Time,
                        requiresTime = s.RequiresTime
                    }),








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








            ViewBag.CanAssessParticipants = eventItem.Status == "Completed"
                && await _userManager.IsInRoleAsync(currentUser, "Organizer")
                && !await _userManager.IsInRoleAsync(currentUser, "Admin")
                && eventItem.OrganizerId != null
                && eventItem.OrganizerId == currentUser.Id;







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







            var joinedRegistrations = allRegistrations
                .Where(r => r.Status == "Accepted" || r.Status == "Pending")
                .OrderBy(r => r.RegisteredAt)
                .ToList();







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








            ApplicationUser? organizer = null;
            if (!string.IsNullOrEmpty(eventItem.OrganizerId))
            {
                organizer = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == eventItem.OrganizerId);
            }
            else if (!string.IsNullOrEmpty(eventItem.OrganizedBy))
            {
                organizer = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u =>
                        (u.FirstName + " " + u.LastName) == eventItem.OrganizedBy ||
                        (u.FirstName + " " + u.MiddleName + " " + u.LastName) == eventItem.OrganizedBy ||
                        u.Email == eventItem.OrganizedBy ||
                        u.Id == eventItem.OrganizedBy
                    );
            }
            ViewBag.Organizer = organizer;

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








                if (existingEvent.TrailId != model.TrailId && !trail.IsActive)
                {
                    return Json(new { success = false, message = "The selected trail is no longer active. Please choose another trail." });
                }









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









                    if (existingEvent.OrganizerId == null || existingEvent.OrganizerId != currentUser.Id)
                    {
                        return Json(new { success = false, message = "Event not found" });
                    }

                    organizerName = existingEvent.OrganizedBy ?? string.Empty;
                    resolvedOrganizerId = existingEvent.OrganizerId;
                }














                if (existingEvent.Status == "Completed")
                {
                    return Json(new { success = false, message = "Completed events are read-only and cannot be edited." });
                }

                var scheduleResult = PickupScheduleHelper.ValidateAndFormat(model.PickupSchedules);
                if (!scheduleResult.Success)
                {
                    return Json(new { success = false, message = scheduleResult.Error });
                }
















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






                if (existingEvent.TrailId != model.TrailId)
                {
                    EventTrailSnapshotHelper.CaptureSnapshot(existingEvent, trail);
                }

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







                if (eventItem.Status == "Completed")
                {
                    return Json(new { success = false, message = "Completed events are part of the event history and cannot be deleted." });
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
