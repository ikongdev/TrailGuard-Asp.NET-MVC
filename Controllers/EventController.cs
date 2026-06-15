using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    public class DeleteEventRequest
    {
        public int Id { get; set; }
    }
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

            var groupedEvents = eventsList
                .Where(e => e.Trail != null)
                .GroupBy(e => e.TrailId)
                .Select(g => new EventGroupViewModel
                {
                    TrailId = g.Key,
                    TrailName = g.First().Trail?.Name ?? "Unknown Trail",
                    TrailLocation = g.First().Location,
                    Events = g.OrderBy(e => e.EventDate).ToList()
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
        public async Task<JsonResult> GetWeatherForecast(int trailId, DateTime eventDate)
        {
            try
            {
                var trail = await _context.Trails.FindAsync(trailId);
                if (trail == null)
                {
                    return Json(new { success = false, message = "Trail not found" });
                }

                var forecast = await _weatherService.GetWeatherForecastAsync(trail.Location, eventDate);
                return Json(new { success = true, forecast = forecast });
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
                    Announcements = model.Announcements,
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
                announcements = eventItem.Announcements,
                paymentDetails = eventItem.PaymentDetails,
                pickupPoints = eventItem.PickupPoints,
                status = eventItem.Status
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);
            
            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Index");
            }

            ViewBag.Trail = eventItem.Trail;
            return View(eventItem);
        }

        [HttpPost]
        public async Task<JsonResult> UpdateStatus([FromBody] UpdateStatusRequest request)
        {
            try
            {
                var eventItem = await _context.Events.FindAsync(request.Id);
                if (eventItem == null)
                {
                    return Json(new { success = false, message = "Event not found" });
                }

                eventItem.Status = request.Status;
                eventItem.DateUpdated = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class UpdateStatusRequest
        {
            public int Id { get; set; }
            public string Status { get; set; } = string.Empty;
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
                existingEvent.Announcements = model.Announcements;
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