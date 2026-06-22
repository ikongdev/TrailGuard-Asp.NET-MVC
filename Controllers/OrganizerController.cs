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

            return RedirectToAction("Index", "Event", new { searchString, status, sortOrder });
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

        public async Task<IActionResult> RegistrationDetails(int id)
        {
            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (registration == null)
            {
                TempData["Error"] = "Registration not found";
                return RedirectToAction("Registrations");
            }

            if (registration.Event != null && registration.Assessment != null)
            {
                ViewBag.AlternativeEvents = await GetAlternativeEvents(
                    registration.Event.Id,
                    registration.Event.Difficulty ?? "",
                    registration.Assessment.Result ?? ""
                );
            }

            return View(registration);
        }

        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result)
        {
            var difficultyLevels = new List<string> { "Easy", "Moderate", "Difficult", "Technical" };
            var currentIndex = difficultyLevels.IndexOf(currentDifficulty);
            if (currentIndex < 0) currentIndex = 1;

            int targetIndex;
            if (result == "Good-Match")
                targetIndex = currentIndex;
            else if (result == "Borderline")
                targetIndex = Math.Max(0, currentIndex - 1);
            else
                targetIndex = Math.Max(0, currentIndex - 2);

            var targetDifficulty = difficultyLevels[targetIndex];

            return await _context.Events
                .Include(e => e.Trail)
                .Where(e =>
                    e.Id != eventId &&
                    e.Status == "Upcoming" &&
                    e.Difficulty == targetDifficulty &&
                    e.EventDate >= DateTime.Today)
                .Take(5)
                .ToListAsync();
        }

        public class RecommendAlternativeRequest
        {
            public int RegistrationId { get; set; }
            public int[] AlternativeEventIds { get; set; } = Array.Empty<int>();
            public string? Reason { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> RecommendAlternative([FromBody] RecommendAlternativeRequest request)
        {
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (request.AlternativeEventIds == null || request.AlternativeEventIds.Length == 0)
            {
                return Json(new { success = false, message = "Pumili muna ng kahit isang alternative event." });
            }

            registration.Status = "Alternative Recommended";
            registration.AlternativeEventId = request.AlternativeEventIds.First();
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Recommended {request.AlternativeEventIds.Length} alternative event(s)" });
        }

        public class UpdateRegistrationStatusRequest
        {
            public int Id { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? Reason { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRegistrationStatus([FromBody] UpdateRegistrationStatusRequest request)
        {
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            registration.Status = request.Status;

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Registration status updated to {request.Status}" });
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

        public async Task<IActionResult> PostEventAssessment(int eventId)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            if (eventItem.Status != "Completed")
            {
                TempData["Error"] = "This event is not yet completed.";
                return RedirectToAction("EventDetails", new { id = eventId });
            }

            var registrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Include(r => r.Assessment)
                .Where(r => r.EventId == eventId && r.Status == "Accepted")
                .ToListAsync();

            // 🔥 I-normalize ang UserId sa C# side
            foreach (var reg in registrations)
            {
                reg.UserId = reg.UserId?.Trim() ?? "";
            }

            var existingAssessments = await _context.PostEventAssessments
                .Where(a => a.EventId == eventId)
                .ToDictionaryAsync(a => a.UserId.Trim(), a => a);

            ViewBag.Registrations = registrations;
            ViewBag.ExistingAssessments = existingAssessments;

            return View(eventItem);
        }

        [HttpPost]
        public async Task<IActionResult> SubmitPostEventAssessment([FromBody] SubmitAssessmentRequest request)
        {
            Console.WriteLine($"Received: EventId={request.EventId}, RegistrationId={request.RegistrationId}, Difficulty={request.DifficultyExperience}");

            // 🔥 Hanapin ang registration
            var registration = await _context.EventRegistrations
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId);

            if (registration == null)
            {
                Console.WriteLine($"Registration not found: {request.RegistrationId}");
                return Json(new { success = false, message = "Registration not found" });
            }

            var userId = registration.UserId;
            Console.WriteLine($"Found registration for user: {userId}");

            var existingAssessment = await _context.PostEventAssessments
                .FirstOrDefaultAsync(a => a.EventId == request.EventId && a.UserId == userId);

            if (existingAssessment != null)
            {
                existingAssessment.DifficultyExperience = request.DifficultyExperience;
                existingAssessment.Notes = request.Notes;
                existingAssessment.CreatedAt = DateTime.Now;
            }
            else
            {
                var assessment = new PostEventAssessment
                {
                    EventId = request.EventId,
                    UserId = userId,
                    DifficultyExperience = request.DifficultyExperience,
                    Notes = request.Notes,
                    CreatedAt = DateTime.Now
                };
                _context.PostEventAssessments.Add(assessment);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Assessment saved successfully!" });
        }

        // 🔥 I-add itong class sa loob ng OrganizerController
        public class SubmitAssessmentRequest
        {
            public int EventId { get; set; }
            public int RegistrationId { get; set; }
            public string DifficultyExperience { get; set; } = string.Empty;
            public string? Notes { get; set; }
        }

        public async Task<IActionResult> EventComparison(int eventId)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var registrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Include(r => r.Assessment)
                .Where(r => r.EventId == eventId && r.Status == "Accepted")
                .ToListAsync();

            var participantFeedbacks = await _context.EventFeedbacks
                .Where(f => f.EventId == eventId)
                .ToDictionaryAsync(f => f.UserId, f => f);

            var organizerAssessments = await _context.PostEventAssessments
                .Where(a => a.EventId == eventId)
                .ToDictionaryAsync(a => a.UserId, a => a);

            var results = new List<ComparisonResult>();

            foreach (var reg in registrations)
            {
                var userId = reg.UserId;
                var preHike = reg.Assessment?.Result ?? "N/A";
                
                var participantFeedback = participantFeedbacks.ContainsKey(userId) 
                    ? participantFeedbacks[userId].DifficultyExperience ?? "No feedback"
                    : "No feedback";
                    
                var organizerAssessment = organizerAssessments.ContainsKey(userId) 
                    ? organizerAssessments[userId].DifficultyExperience ?? "No assessment"
                    : "No assessment";

                var finalResult = GetConservativeResult(participantFeedback, organizerAssessment);
                var comparison = ComparePreHikeToPostHike(preHike, finalResult);

                results.Add(new ComparisonResult
                {
                    ParticipantName = reg.User != null ? $"{reg.User.FirstName} {reg.User.LastName}" : reg.ParticipantName,
                    PreHikeAssessment = preHike,
                    ParticipantFeedback = participantFeedback,
                    OrganizerAssessment = organizerAssessment,
                    FinalResult = finalResult,
                    Comparison = comparison.Item1,
                    ComparisonColor = comparison.Item2,
                    ComparisonIcon = comparison.Item3
                });
            }

            ViewBag.Event = eventItem;
            return View(results);
        }

        private string GetConservativeResult(string? participantFeedback, string? organizerAssessment)
        {
            if (string.IsNullOrEmpty(participantFeedback) || string.IsNullOrEmpty(organizerAssessment))
            {
                return !string.IsNullOrEmpty(participantFeedback) ? participantFeedback : 
                    !string.IsNullOrEmpty(organizerAssessment) ? organizerAssessment : 
                    "Insufficient data";
            }

            var order = new Dictionary<string, int>
            {
                { "Could not finish - injured", 1 },
                { "Could not finish - turned back", 2 },
                { "Much harder", 3 },
                { "Harder than expected", 4 },
                { "Matched but challenging", 5 },
                { "Matched perfectly", 6 },
                { "Much easier than expected", 7 }
            };

            var participantOrder = order.ContainsKey(participantFeedback) ? order[participantFeedback] : 99;
            var organizerOrder = order.ContainsKey(organizerAssessment) ? order[organizerAssessment] : 99;

            var conservativeOrder = Math.Min(participantOrder, organizerOrder);
            return order.FirstOrDefault(x => x.Value == conservativeOrder).Key;
        }

        private Tuple<string, string, string> ComparePreHikeToPostHike(string preHike, string postHike)
        {
            var mapping = new Dictionary<string, string>
            {
                { "Much easier than expected", "Good-Match" },
                { "Matched perfectly", "Good-Match" },
                { "Matched but challenging", "Borderline" },
                { "Harder than expected", "Borderline" },
                { "Much harder", "Not Recommended" },
                { "Could not finish - turned back", "Not Recommended" },
                { "Could not finish - injured", "Not Recommended" }
            };

            var postCategory = mapping.ContainsKey(postHike) ? mapping[postHike] : "N/A";

            if (preHike == "N/A" || postCategory == "N/A")
            {
                return Tuple.Create("Insufficient Data", "text-gray-400", "fa-minus-circle");
            }

            if (preHike == postCategory)
            {
                return Tuple.Create("✅ Accurate", "text-green-400", "fa-check-circle");
            }

            var order = new Dictionary<string, int>
            {
                { "Good-Match", 1 },
                { "Borderline", 2 },
                { "Not Recommended", 3 }
            };

            var preOrder = order[preHike];
            var postOrder = order[postCategory];

            if (preOrder < postOrder)
            {
                return Tuple.Create("⚠️ Overestimated", "text-yellow-400", "fa-triangle-exclamation");
            }
            else
            {
                return Tuple.Create("⚠️ Underestimated", "text-orange-400", "fa-arrow-trend-up");
            }
        }

    }
}