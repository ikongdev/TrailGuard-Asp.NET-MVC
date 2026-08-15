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

        public EventController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, UserManager<ApplicationUser> userManager, WeatherService weatherService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _userManager = userManager;
            _weatherService = weatherService;
        }

        public async Task<IActionResult> Index(string searchString, string status, string sortOrder)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = status;
            ViewData["CurrentSort"] = sortOrder;

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();
            
            var organizers = await _userManager.GetUsersInRoleAsync("Organizer");
            ViewBag.Organizers = organizers.ToList();

            var events = _context.Events
                .Include(e => e.Trail)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                events = events.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                events = events.Where(e => e.Status == status);
            }

            var eventsList = await events.ToListAsync();

            var eventIds = eventsList.Select(e => e.Id).ToList();
            var capacityCounts = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            var groupedEvents = eventsList
                .Where(e => e.Trail != null)
                .GroupBy(e => e.TrailId)
                .Select(g => new EventGroupViewModel
                {
                    TrailId = g.Key,
                    TrailName = g.First().Trail?.Name ?? "Unknown Trail",
                    TrailLocation = g.First().Location,
                    Events = g.OrderBy(e => e.EventDate).Select(e => {
                        e.RegisteredCount = capacityCounts.ContainsKey(e.Id) ? capacityCounts[e.Id] : 0;
                        return e;
                    }).ToList()
                })
                .ToList();

            return View(groupedEvents);
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
                masl = trail.ElevationGainMeters,
                distance = trail.DistanceKm,
                terrain = trail.Terrain
            });
        }

        [HttpGet]
        public async Task<JsonResult> GetCalculatedDifficulty(int trailId, double duration)
        {
            var trail = await _context.Trails.FindAsync(trailId);
            if (trail == null)
            {
                return Json(new { success = false });
            }
            
            var difficulty = DifficultyCalculator.ComputeDifficulty(trail, duration);
            return Json(new { success = true, difficulty = difficulty });
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
                    Difficulty = DifficultyCalculator.ComputeDifficulty(trail, model.EstimatedDuration),
                    EstimatedDuration = model.EstimatedDuration,
                    Capacity = model.Capacity,
                    OrganizedBy = organizerName,
                    Status = "Upcoming",
                    MASL = trail.ElevationGainMeters,
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
                existingEvent.Difficulty = DifficultyCalculator.ComputeDifficulty(trail, model.EstimatedDuration);
                existingEvent.EstimatedDuration = model.EstimatedDuration;
                existingEvent.Capacity = model.Capacity;
                existingEvent.OrganizedBy = model.OrganizedBy;
                existingEvent.Status = model.Status ?? existingEvent.Status;
                existingEvent.MASL = trail.ElevationGainMeters;
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