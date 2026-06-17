using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Organizer")]
    public class OrganizerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public OrganizerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var model = new OrganizerDashboardViewModel();

            // Get events organized by this organizer
            var organizerEvents = await _context.Events
                .Where(e => e.OrganizedBy == user.Id || e.OrganizedBy == user.Email)
                .ToListAsync();

            var eventIds = organizerEvents.Select(e => e.Id).ToList();

            // Summary Cards
            model.TotalEvents = organizerEvents.Count;
            
            // Pending Registrations
            model.PendingRegistrations = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Pending")
                .CountAsync();

            // Upcoming Events
            model.UpcomingEvents = await _context.Events
                .Where(e => eventIds.Contains(e.Id) && e.EventDate >= DateTime.Today)
                .OrderBy(e => e.EventDate)
                .Take(5)
                .ToListAsync();

            // Event Completion Alerts (events within 7 days)
            var alertThreshold = DateTime.Today.AddDays(7);
            model.EventAlerts = await _context.Events
                .Where(e => eventIds.Contains(e.Id) && e.EventDate >= DateTime.Today && e.EventDate <= alertThreshold)
                .CountAsync();

            // Total Participants (all time)
            model.TotalParticipants = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId))
                .CountAsync();

            // Participants last 30 days
            var last30Days = DateTime.Now.AddDays(-30);
            model.ParticipantsLast30Days = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && r.RegisteredAt >= last30Days)
                .CountAsync();

            // Top Trails (most popular among organizer's events)
            var topTrailsData = await _context.Events
                .Where(e => eventIds.Contains(e.Id))
                .GroupBy(e => e.TrailId)
                .Select(g => new
                {
                    TrailId = g.Key,
                    ParticipantCount = _context.EventRegistrations.Count(r => g.Select(e => e.Id).Contains(r.EventId))
                })
                .OrderByDescending(t => t.ParticipantCount)
                .Take(5)
                .ToListAsync();

            model.TopTrails = new List<TopTrailData>();
            foreach (var item in topTrailsData)
            {
                var trail = await _context.Trails.FirstOrDefaultAsync(t => t.Id == item.TrailId);
                model.TopTrails.Add(new TopTrailData
                {
                    TrailId = item.TrailId,
                    TrailName = trail?.Name ?? "Unknown Trail",
                    ParticipantCount = item.ParticipantCount
                });
            }

            // Participant Suitability Breakdown
            var suitabilityData = await _context.Assessments
                .Where(a => eventIds.Contains(a.EventId))
                .GroupBy(a => a.SuitabilityResult)
                .Select(g => new SuitabilityData
                {
                    Result = g.Key ?? "Unknown",
                    Count = g.Count(),
                    Percentage = 0
                })
                .ToListAsync();

            model.SuitabilityBreakdown = suitabilityData;

            // Calculate percentages
            var totalAssessments = suitabilityData.Sum(s => s.Count);
            if (totalAssessments > 0)
            {
                foreach (var item in model.SuitabilityBreakdown)
                {
                    item.Percentage = Math.Round((double)item.Count / totalAssessments * 100, 1);
                }
            }

            // Top Hikers (by assessment score)
            var topHikersData = await _context.Assessments
                .Where(a => eventIds.Contains(a.EventId))
                .GroupBy(a => a.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    AverageScore = g.Average(a => a.SuitabilityScore),
                    TotalAssessments = g.Count()
                })
                .OrderByDescending(h => h.AverageScore)
                .Take(6)
                .ToListAsync();

            model.TopHikers = new List<TopHikerData>();
            foreach (var item in topHikersData)
            {
                var userInfo = await _context.Users.FirstOrDefaultAsync(u => u.Id == item.UserId);
                model.TopHikers.Add(new TopHikerData
                {
                    UserId = item.UserId,
                    UserName = userInfo != null ? $"{userInfo.FirstName} {userInfo.LastName}" : "Unknown User",
                    AverageScore = Math.Round(item.AverageScore, 1),
                    TotalAssessments = item.TotalAssessments
                });
            }

            return View(model);
        }

        // GET: Organizer/Events
        public async Task<IActionResult> Events(string searchString, string sortOrder)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            var events = _context.Events
                .Include(e => e.Trail)
                .Where(e => e.OrganizedBy == user.Id || e.OrganizedBy == user.Email);

            if (!string.IsNullOrEmpty(searchString))
            {
                events = events.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));
            }

            events = sortOrder switch
            {
                "title_desc" => events.OrderByDescending(e => e.EventTitle),
                "date_asc" => events.OrderBy(e => e.EventDate),
                "date_desc" => events.OrderByDescending(e => e.EventDate),
                _ => events.OrderBy(e => e.EventDate),
            };

            return View(await events.ToListAsync());
        }

        // GET: Organizer/Events/Add
        public async Task<IActionResult> AddEvent()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();
            ViewBag.OrganizerId = user.Id;
            ViewBag.OrganizerName = $"{user.FirstName} {user.LastName}";

            return View();
        }

        // POST: Organizer/Events/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEvent(Event model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (ModelState.IsValid)
            {
                model.OrganizedBy = user.Id;
                model.Status = "Upcoming";

                _context.Events.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Event created successfully!";
                return RedirectToAction("Events");
            }

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();
            ViewBag.OrganizerId = user.Id;
            ViewBag.OrganizerName = $"{user.FirstName} {user.LastName}";

            return View(model);
        }

        // GET: Organizer/Events/Edit/{id}
        public async Task<IActionResult> EditEvent(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found.";
                return RedirectToAction("Events");
            }

            // Verify ownership
            if (eventItem.OrganizedBy != user.Id && eventItem.OrganizedBy != user.Email)
            {
                TempData["Error"] = "You are not authorized to edit this event.";
                return RedirectToAction("Events");
            }

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();
            return View(eventItem);
        }

        // POST: Organizer/Events/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEvent(int id, Event model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var existingEvent = await _context.Events.FindAsync(id);
            if (existingEvent == null)
            {
                TempData["Error"] = "Event not found.";
                return RedirectToAction("Events");
            }

            // Verify ownership
            if (existingEvent.OrganizedBy != user.Id && existingEvent.OrganizedBy != user.Email)
            {
                TempData["Error"] = "You are not authorized to edit this event.";
                return RedirectToAction("Events");
            }

            if (ModelState.IsValid)
            {
                existingEvent.EventTitle = model.EventTitle;
                existingEvent.Description = model.Description;
                existingEvent.EventDate = model.EventDate;
                existingEvent.EventTime = model.EventTime;
                existingEvent.TrailId = model.TrailId;
                existingEvent.Capacity = model.Capacity;
                existingEvent.EstimatedDuration = model.EstimatedDuration;
                existingEvent.Status = model.Status;
                existingEvent.PaymentDetails = model.PaymentDetails;
                existingEvent.PickupPoints = model.PickupPoints;
                existingEvent.Announcements = model.Announcements;
                existingEvent.WeatherForecastAdvisory = model.WeatherForecastAdvisory;
                existingEvent.DateUpdated = DateTime.Now;

                await _context.SaveChangesAsync();
                TempData["Success"] = "Event updated successfully!";
                return RedirectToAction("Events");
            }

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();
            return View(model);
        }

        // POST: Organizer/Events/Delete/{id}
        [HttpPost]
        public async Task<JsonResult> DeleteEvent(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var eventItem = await _context.Events.FindAsync(id);
            if (eventItem == null)
            {
                return Json(new { success = false, message = "Event not found" });
            }

            // Verify ownership
            if (eventItem.OrganizedBy != user.Id && eventItem.OrganizedBy != user.Email)
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            _context.Events.Remove(eventItem);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Event deleted successfully" });
        }

        // GET: Organizer/Registrations
        public async Task<IActionResult> Registrations(string searchString, string eventFilter, string resultFilter, string sortOrder)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentEventFilter"] = eventFilter;
            ViewData["CurrentResultFilter"] = resultFilter;
            ViewData["CurrentSort"] = sortOrder;

            // Get events organized by this organizer
            var organizerEventIds = await _context.Events
                .Where(e => e.OrganizedBy == user.Id || e.OrganizedBy == user.Email)
                .Select(e => e.Id)
                .ToListAsync();

            // Get all events for dropdown filter
            ViewBag.OrganizerEvents = await _context.Events
                .Where(e => organizerEventIds.Contains(e.Id))
                .OrderBy(e => e.EventDate)
                .ToListAsync();

            var registrations = _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.User)
                .Where(r => organizerEventIds.Contains(r.EventId))
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(searchString))
            {
                registrations = registrations.Where(r => 
                    r.ParticipantName.Contains(searchString) || 
                    (r.Event != null && r.Event.EventTitle.Contains(searchString)));
            }

            if (!string.IsNullOrEmpty(eventFilter) && eventFilter != "All")
            {
                var eventId = int.Parse(eventFilter);
                registrations = registrations.Where(r => r.EventId == eventId);
            }

            // Apply sorting
            registrations = sortOrder switch
            {
                "oldest" => registrations.OrderBy(r => r.RegisteredAt),
                _ => registrations.OrderByDescending(r => r.RegisteredAt), // newest first
            };

            var registrationList = await registrations.ToListAsync();

            // Get assessment results for each registration
            var result = new List<RegistrationWithAssessmentViewModel>();
            int goodMatchCount = 0, borderlineCount = 0, notRecommendedCount = 0;

            foreach (var reg in registrationList)
            {
                var assessment = await _context.Assessments
                    .FirstOrDefaultAsync(a => a.EventId == reg.EventId && a.UserId == reg.UserId);

                var assessmentResult = assessment?.SuitabilityResult ?? "Not Assessed";
                
                // Count for summary cards
                if (assessmentResult == "Good-Match") goodMatchCount++;
                else if (assessmentResult == "Borderline") borderlineCount++;
                else if (assessmentResult == "Not Recommended") notRecommendedCount++;

                result.Add(new RegistrationWithAssessmentViewModel
                {
                    Registration = reg,
                    AssessmentResult = assessmentResult,
                    AssessmentScore = assessment?.SuitabilityScore ?? 0,
                    AssessmentId = assessment?.Id ?? 0
                });
            }

            // Apply result filter after counting (client-side filtering)
            if (!string.IsNullOrEmpty(resultFilter) && resultFilter != "All")
            {
                result = result.Where(r => r.AssessmentResult == resultFilter).ToList();
            }

            ViewBag.GoodMatchCount = goodMatchCount;
            ViewBag.BorderlineCount = borderlineCount;
            ViewBag.NotRecommendedCount = notRecommendedCount;
            ViewBag.TotalRegistrations = registrationList.Count;

            return View(result);
        }

        // POST: Organizer/Registrations/Accept
        [HttpPost]
        public async Task<JsonResult> AcceptRegistration(int registrationId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == registrationId);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            // Verify ownership
            if (registration.Event == null || (registration.Event.OrganizedBy != user.Id && registration.Event.OrganizedBy != user.Email))
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            registration.Status = "Accepted";
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Registration accepted" });
        }

        // POST: Organizer/Registrations/Reject
        [HttpPost]
        public async Task<JsonResult> RejectRegistration(int registrationId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == registrationId);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            // Verify ownership
            if (registration.Event == null || (registration.Event.OrganizedBy != user.Id && registration.Event.OrganizedBy != user.Email))
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            registration.Status = "Rejected";
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Registration rejected" });
        }

        // POST: Organizer/Registrations/RecommendAlternative
        [HttpPost]
        public async Task<JsonResult> RecommendAlternative(int registrationId, int alternativeEventId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == registrationId);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            // Verify ownership
            if (registration.Event == null || (registration.Event.OrganizedBy != user.Id && registration.Event.OrganizedBy != user.Email))
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            registration.Status = "Recommended";
            registration.AlternativeEventId = alternativeEventId;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Alternative event recommended" });
        }
    }
}