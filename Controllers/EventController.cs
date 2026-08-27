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

        public EventController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<ApplicationUser> userManager, WeatherService weatherService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _weatherService = weatherService;
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
                    suggestedReminder = reminder
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
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

                var organizer = await _userManager.FindByIdAsync(model.OrganizedBy ?? "");
                var organizerName = organizer != null ? $"{organizer.FirstName} {organizer.LastName}" : model.OrganizedBy;

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
                    Status = "Upcoming",
                    WeatherForecastAdvisory = model.WeatherForecastAdvisory,
                    WeatherRiskLevel = model.WeatherRiskLevel,
                    WeatherReminder = model.WeatherReminder,
                    NotesAndReminders = model.NotesAndReminders,
                    PaymentDetails = model.PaymentDetails,
                    PickupPoints = model.PickupPoints
                };

                _context.Events.Add(newEvent);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetEvent(int id)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);
            
            if (eventItem == null)
            {
                return Json(new { success = false, message = "Event not found" });
            }

            return Json(new
            {
                success = true,
                id = eventItem.Id,
                eventTitle = eventItem.EventTitle,
                description = eventItem.Description,
                eventDate = eventItem.EventDate.ToString("yyyy-MM-dd"),
                eventTime = eventItem.EventTime.ToString(),
                trailId = eventItem.TrailId,
                trailName = eventItem.Trail?.Name,
                estimatedDuration = eventItem.EstimatedDuration,
                capacity = eventItem.Capacity,
                organizedBy = eventItem.OrganizedBy,
                weatherForecastAdvisory = eventItem.WeatherForecastAdvisory,
                weatherRiskLevel = eventItem.WeatherRiskLevel,
                weatherReminder = eventItem.WeatherReminder,
                announcements = eventItem.NotesAndReminders,
                paymentDetails = eventItem.PaymentDetails,
                pickupPoints = eventItem.PickupPoints,
                status = eventItem.Status
            });
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
                return Json(new { success = false, message = ex.Message });
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
                return Json(new { success = false, message = ex.Message });
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
                return Json(new { success = false, message = ex.Message });
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

                existingEvent.EventTitle = model.EventTitle;
                existingEvent.Description = model.Description;
                existingEvent.EventDate = model.EventDate;
                existingEvent.EventTime = model.EventTime;
                existingEvent.TrailId = model.TrailId;
                existingEvent.Location = trail.Location;
                existingEvent.Difficulty = DifficultyCalculator.ComputeDifficulty(trail);
                existingEvent.EstimatedDuration = model.EstimatedDuration;
                existingEvent.Capacity = model.Capacity;
                existingEvent.OrganizedBy = model.OrganizedBy;
                existingEvent.Status = model.Status ?? existingEvent.Status;
                existingEvent.WeatherForecastAdvisory = model.WeatherForecastAdvisory;
                existingEvent.WeatherRiskLevel = model.WeatherRiskLevel ?? existingEvent.WeatherRiskLevel;
                existingEvent.WeatherReminder = model.WeatherReminder ?? existingEvent.WeatherReminder;
                existingEvent.NotesAndReminders = model.NotesAndReminders;
                existingEvent.PaymentDetails = model.PaymentDetails;
                existingEvent.PickupPoints = model.PickupPoints;
                existingEvent.DateUpdated = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
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

                _context.Events.Remove(eventItem);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Event deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}