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
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            var organizerName = user != null ? $"{user.FirstName} {user.LastName}" : "";

            var totalEvents = await _context.Events
                .Where(e => e.OrganizedBy == userId || e.OrganizedBy == organizerName)
                .CountAsync();

            var eventIds = await _context.Events
                .Where(e => e.OrganizedBy == userId || e.OrganizedBy == organizerName)
                .Select(e => e.Id)
                .ToListAsync();

            var pendingRegistrations = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Pending")
                .CountAsync();

            var eventAlerts = await _context.Events
                .Where(e => (e.OrganizedBy == userId || e.OrganizedBy == organizerName) &&
                            e.EventDate < DateTime.Now && e.Status != "Completed")
                .CountAsync();

            var participantsInRole = await _userManager.GetUsersInRoleAsync("Participant");
            var totalParticipants = participantsInRole.Count;

            var trendData = new List<MonthlyTrendData>();
            for (int i = 11; i >= 0; i--)
            {
                var monthStart = DateTime.Now.AddMonths(-i).AddDays(1 - DateTime.Now.Day);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var monthName = monthStart.ToString("MMM");

                var count = await _context.EventRegistrations
                    .Where(r => eventIds.Contains(r.EventId) && r.Status == "Accepted" &&
                                r.RegisteredAt >= monthStart && r.RegisteredAt <= monthEnd)
                    .CountAsync();

                trendData.Add(new MonthlyTrendData { Month = monthName, Count = count });
            }

            var topTrails = await _context.Events
                .Where(e => e.Trail != null && (e.OrganizedBy == userId || e.OrganizedBy == organizerName))
                .GroupBy(e => e.TrailId)
                .Select(g => new TopTrailData
                {
                    TrailId = g.Key,
                    TrailName = g.First().Trail!.Name,
                    EventCount = g.Count()
                })
                .OrderByDescending(t => t.EventCount)
                .Take(5)
                .ToListAsync();

            Console.WriteLine($"Top Trails Count: {topTrails.Count}");
            foreach (var trail in topTrails)
            {
                Console.WriteLine($"Trail: {trail.TrailName}, Events: {trail.EventCount}");
            }

            var suitabilityData = await _context.Assessments
                .Where(a => eventIds.Contains(a.EventId) && a.IsActive == true)
                .GroupBy(a => a.Result)
                .Select(g => new SuitabilityData
                {
                    Result = g.Key ?? "Not Recommended",
                    Count = g.Count()
                })
                .ToListAsync();

            var totalAssessments = suitabilityData.Sum(s => s.Count);
            foreach (var item in suitabilityData)
            {
                item.Percentage = totalAssessments > 0 ? (int)Math.Round((double)item.Count / totalAssessments * 100) : 0;
            }

            var completedEventIds = await _context.Events
                .Where(e => eventIds.Contains(e.Id) && e.Status == "Completed")
                .Select(e => e.Id)
                .ToListAsync();

            var topHikers = await _context.EventRegistrations
                .Where(r => completedEventIds.Contains(r.EventId) && r.Status == "Accepted")
                .GroupBy(r => r.UserId)
                .Select(g => new TopHikerData
                {
                    UserId = g.Key,
                    UserName = g.First().User!.FirstName + " " + g.First().User!.LastName,
                    CompletedEvents = g.Count()
                })
                .OrderByDescending(h => h.CompletedEvents)
                .Take(5)
                .ToListAsync();

            var viewModel = new OrganizerDashboardViewModel
            {
                TotalEvents = totalEvents,
                PendingRegistrations = pendingRegistrations,
                EventAlerts = eventAlerts,
                TotalParticipants = totalParticipants,
                TrendData = trendData,
                TopTrails = topTrails,
                SuitabilityBreakdown = suitabilityData,
                TopHikers = topHikers,
                TotalAssessments = totalAssessments
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Events(string searchString, string status, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = status;
            ViewData["CurrentSort"] = sortOrder;

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            var organizerName = user != null ? $"{user.FirstName} {user.LastName}" : "";

            var events = _context.Events
                .Include(e => e.Trail)
                .Where(e => e.OrganizedBy == userId || e.OrganizedBy == organizerName)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                events = events.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                events = events.Where(e => e.Status == status);
            }

            events = sortOrder switch
            {
                "date_desc" => events.OrderByDescending(e => e.EventDate),
                "title_asc" => events.OrderBy(e => e.EventTitle),
                "title_desc" => events.OrderByDescending(e => e.EventTitle),
                "status_asc" => events.OrderBy(e => e.Status),
                _ => events.OrderBy(e => e.EventDate),
            };

            var eventsList = await events.ToListAsync();

            return View(eventsList);
        }

        public async Task<IActionResult> Registrations(string searchString, string statusFilter, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = statusFilter;
            ViewData["CurrentSort"] = sortOrder;

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = await _userManager.FindByIdAsync(userId ?? "");
            var organizerName = user != null ? $"{user.FirstName} {user.LastName}" : "";

            var eventIds = await _context.Events
                .Where(e => e.OrganizedBy == userId || e.OrganizedBy == organizerName)
                .Select(e => e.Id)
                .ToListAsync();

            var registrations = _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.Assessment)
                .Include(r => r.User)
                .Where(r => eventIds.Contains(r.EventId))
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                registrations = registrations.Where(r =>
                    r.ParticipantName.Contains(searchString) ||
                    (r.Event != null && r.Event.EventTitle.Contains(searchString)));
            }

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
            {
                registrations = registrations.Where(r => r.Status == statusFilter);
            }

            registrations = sortOrder switch
            {
                "date_desc" => registrations.OrderByDescending(r => r.RegisteredAt),
                "participant_asc" => registrations.OrderBy(r => r.ParticipantName),
                "participant_desc" => registrations.OrderByDescending(r => r.ParticipantName),
                _ => registrations.OrderBy(r => r.RegisteredAt),
            };

            var registrationsList = await registrations.ToListAsync();

            var viewModel = registrationsList.Select(r => new RegistrationWithAssessmentViewModel
            {
                RegistrationId = r.Id,
                EventId = r.EventId,
                EventTitle = r.Event?.EventTitle ?? "Unknown Event",
                EventDate = r.Event?.EventDate.ToString("MMM dd, yyyy") ?? "",
                EventTime = r.Event?.FormattedEventTime ?? "",
                EventDifficulty = r.Event?.Difficulty ?? "",
                ParticipantName = r.ParticipantName,
                UserId = r.UserId,
                Email = r.User != null ? r.User.Email ?? "" : "",
                PickupPoint = r.PickupPoint ?? "",
                Status = r.Status,
                RegisteredAt = r.RegisteredAt,
                IsPaid = r.IsPaid,
                PaymentReceiptUrl = r.PaymentReceiptUrl,
                EmergencyContactName = r.EmergencyContactName,
                EmergencyContactNumber = r.EmergencyContactNumber,
                AssessmentId = r.AssessmentId,
                AssessmentResult = r.Assessment?.Result,
                AssessmentTotalScore = r.Assessment?.TotalScore,
                MedicalConditions = r.Assessment?.MedicalConditions,
                FitnessLevel = r.Assessment?.ExerciseFrequency,
                HikingExperience = r.Assessment?.MountainsClimbed,
                GearItems = r.Assessment?.GearItems
            }).ToList();

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRegistrationStatus(int id, string status)
        {
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            registration.Status = status;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Registration status updated to {status}" });
        }

        public IActionResult AddEvent()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddEvent(Event model)
        {
            if (ModelState.IsValid)
            {
                _context.Events.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Event created successfully!";
                return RedirectToAction("Events");
            }
            return View(model);
        }

        public async Task<IActionResult> EditEvent(int id)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            return View(eventItem);
        }

        [HttpPost]
        public async Task<IActionResult> EditEvent(int id, Event model)
        {
            if (id != model.Id)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Event updated successfully!";
                    return RedirectToAction("Events");
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Events.Any(e => e.Id == id))
                    {
                        TempData["Error"] = "Event not found";
                        return RedirectToAction("Events");
                    }
                    throw;
                }
            }
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            var eventItem = await _context.Events.FindAsync(id);
            if (eventItem == null)
            {
                return Json(new { success = false, message = "Event not found" });
            }

            _context.Events.Remove(eventItem);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Event deleted successfully" });
        }

        public async Task<IActionResult> EventDetails(int id)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var acceptedRegistrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Include(r => r.Assessment)
                .Where(r => r.EventId == id && r.Status == "Accepted")
                .ToListAsync();

            var allRegistrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Include(r => r.Assessment)
                .Where(r => r.EventId == id && r.Status != "Rejected" && r.Status != "Cancelled")
                .ToListAsync();

            ViewBag.Registrations = allRegistrations;
            ViewBag.RegisteredCount = acceptedRegistrations.Count;
            ViewBag.AvailableSlots = eventItem.Capacity - acceptedRegistrations.Count;
            ViewBag.Trail = eventItem.Trail;

            return View(eventItem);
        }
    }
}