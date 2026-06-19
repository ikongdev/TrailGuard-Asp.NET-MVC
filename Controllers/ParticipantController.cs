using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class ParticipantController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ParticipantController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Where(r => r.UserId == userId && r.Status != "Cancelled")
                .ToListAsync();

            var upcomingEvents = registrations
                .Where(r => r.Status == "Pending" || r.Status == "Accepted")
                .Select(r => r.Event)
                .Where(e => e != null && e.EventDate >= DateTime.Today)
                .ToList();

            var completedHikes = registrations
                .Where(r => r.Status == "Accepted" && r.Event != null && r.Event.Status == "Completed")
                .Count();

            var pendingRegistrations = registrations
                .Where(r => r.Status == "Pending")
                .Count();

            var totalRegistrations = registrations.Count;

            var latestAssessment = registrations
                .Where(r => r.Assessment != null)
                .OrderByDescending(r => r.RegisteredAt)
                .Select(r => r.Assessment)
                .FirstOrDefault();

            LatestAssessmentResult? latestResult = null;
            if (latestAssessment != null)
            {
                latestResult = new LatestAssessmentResult
                {
                    Result = latestAssessment.Result ?? "Not Recommended",
                    TotalScore = latestAssessment.TotalScore ?? 0,
                    Description = GetAssessmentDescription(latestAssessment.Result ?? ""),
                    SubmittedAt = latestAssessment.SubmittedAt
                };
            }

            var recommendedEvents = new List<Event>();
            if (latestAssessment != null && !string.IsNullOrEmpty(userId))
            {
                recommendedEvents = await GetRecommendedEvents(latestAssessment.Result ?? "", userId);
            }

            var weatherForecast = new WeatherForecast
            {
                Date = DateTime.Now.AddDays(3),
                Condition = "Cloudy with possible light rain",
                Temperature = "25°C–30°C",
                RainChance = 55,
                WindSpeed = "12 km/h",
                RiskLevel = "Moderate"
            };

            var badgesEarned = completedHikes >= 10 ? 5 :
                            completedHikes >= 7 ? 4 :
                            completedHikes >= 5 ? 3 :
                            completedHikes >= 3 ? 2 :
                            completedHikes >= 1 ? 1 : 0;

            var hikerRank = completedHikes >= 10 ? "Top 15%" :
                            completedHikes >= 7 ? "Top 25%" :
                            completedHikes >= 5 ? "Top 40%" :
                            completedHikes >= 3 ? "Top 60%" :
                            completedHikes >= 1 ? "Top 80%" : "Not Ranked";

            var viewModel = new ParticipantDashboardViewModel
            {
                UpcomingEventsCount = upcomingEvents.Count,
                CompletedHikes = completedHikes,
                PendingRegistrations = pendingRegistrations,
                TotalRegistrations = totalRegistrations,
                UpcomingEvents = upcomingEvents!,
                LatestAssessment = latestResult,
                RecommendedEvents = recommendedEvents,
                WeatherForecast = weatherForecast,
                HikerRank = hikerRank,
                BadgesEarned = badgesEarned,
                NextMilestone = completedHikes >= 10 ? "You're a hiking legend! 🏆" :
                                completedHikes >= 5 ? "Keep going! You're almost there!" :
                                completedHikes >= 1 ? "Great start! Keep hiking!" :
                                "Keep hiking to climb higher!"
            };

            return View(viewModel);
        }

        private string GetAssessmentDescription(string result)
        {
            return result switch
            {
                "Good-Match" => "You're well-prepared for moderate to challenging mountain trails",
                "Borderline" => "You're almost there! A bit more preparation will help",
                "Not Recommended" => "Consider starting with easier trails to build experience",
                _ => "Take the assessment to get personalized recommendations"
            };
        }

        private async Task<List<Event>> GetRecommendedEvents(string assessmentResult, string userId)
        {
            var difficultyLevels = new List<string> { "Easy", "Moderate", "Difficult", "Technical" };

            int targetIndex = assessmentResult switch
            {
                "Good-Match" => 2,
                "Borderline" => 1,
                "Not Recommended" => 0,
                _ => 1
            };

            var targetDifficulty = difficultyLevels[targetIndex];

            var recommendedEvents = await _context.Events
                .Include(e => e.Trail)
                .Where(e => e.Status == "Upcoming" &&
                           e.EventDate >= DateTime.Today &&
                           e.Difficulty == targetDifficulty)
                .OrderBy(e => e.EventDate)
                .Take(4)
                .ToListAsync();

            return recommendedEvents;
        }
        public async Task<IActionResult> Trails(string searchString, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            var trails = _context.Trails.AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                trails = trails.Where(t => 
                    t.Name.Contains(searchString) || 
                    t.Location.Contains(searchString));
            }

            trails = sortOrder switch
            {
                "oldest" => trails.OrderBy(t => t.DateAdded),
                "name_asc" => trails.OrderBy(t => t.Name),
                "name_desc" => trails.OrderByDescending(t => t.Name),
                "distance_asc" => trails.OrderBy(t => t.DistanceKm),
                "distance_desc" => trails.OrderByDescending(t => t.DistanceKm),
                "elevation_asc" => trails.OrderBy(t => t.ElevationGainMeters),
                "elevation_desc" => trails.OrderByDescending(t => t.ElevationGainMeters),
                _ => trails.OrderByDescending(t => t.DateAdded),
            };

            var trailsList = await trails.ToListAsync();
            return View(trailsList);
        }

        public async Task<IActionResult> Events(string searchString, string difficulty, string trailFilter, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentDifficulty"] = difficulty;
            ViewData["CurrentTrailFilter"] = trailFilter;
            ViewData["CurrentSort"] = sortOrder;

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();

            var events = _context.Events
                .Include(e => e.Trail)
                .Where(e => e.Status == "Upcoming" || e.Status == "Completed")
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                events = events.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(difficulty) && difficulty != "All")
            {
                events = events.Where(e => e.Difficulty == difficulty);
            }

            if (!string.IsNullOrEmpty(trailFilter) && trailFilter != "All")
            {
                var trailId = int.Parse(trailFilter);
                events = events.Where(e => e.TrailId == trailId);
            }

            events = sortOrder switch
            {
                "date_desc" => events.OrderByDescending(e => e.EventDate),
                "title_asc" => events.OrderBy(e => e.EventTitle),
                "title_desc" => events.OrderByDescending(e => e.EventTitle),
                "difficulty_asc" => events.OrderBy(e => e.Difficulty),
                "difficulty_desc" => events.OrderByDescending(e => e.Difficulty),
                _ => events.OrderBy(e => e.EventDate),
            };

            var eventsList = await events.ToListAsync();

            var eventIds = eventsList.Select(e => e.Id).ToList();
            var acceptedCounts = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Accepted")
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
                        e.RegisteredCount = acceptedCounts.ContainsKey(e.Id) ? acceptedCounts[e.Id] : 0;
                        return e;
                    }).ToList()
                })
                .ToList();

            return View(groupedEvents);
        }

        // GET: Participant/GetTrailEvents (for modal)
        [HttpGet]
        public async Task<JsonResult> GetTrailEvents(int trailId)
        {
            var events = await _context.Events
                .Where(e => e.TrailId == trailId && e.Status == "Upcoming" && e.EventDate >= DateTime.Today)
                .OrderBy(e => e.EventDate)
                .Select(e => new
                {
                    id = e.Id,
                    eventTitle = e.EventTitle,
                    eventDate = e.EventDate.ToString("MMM dd, yyyy"),
                    eventTime = e.FormattedEventTime,
                    difficulty = e.Difficulty
                })
                .ToListAsync();

            return Json(events);
        }

        public async Task<IActionResult> Details(int id)
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
                .Where(r => r.EventId == id && r.Status == "Accepted")
                .ToListAsync();

            var allRegistrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Where(r => r.EventId == id && r.Status != "Rejected" && r.Status != "Cancelled")
                .ToListAsync();
            
            ViewBag.Registrations = allRegistrations;
            ViewBag.RegisteredCount = acceptedRegistrations.Count;
            ViewBag.AvailableSlots = eventItem.Capacity - acceptedRegistrations.Count;

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

            // ✅ I-check kung nagbigay na ng feedback ang participant
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var hasGivenFeedback = false;
            if (userId != null && eventItem.Status == "Completed")
            {
                hasGivenFeedback = await _context.EventFeedbacks
                    .AnyAsync(f => f.EventId == id && f.UserId == userId);
            }
            ViewBag.HasGivenFeedback = hasGivenFeedback;
            
            ViewBag.Trail = eventItem.Trail;
            return View(eventItem);
        }

        [HttpGet]
        public async Task<IActionResult> Feedback(int eventId)
        {
            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var hasGivenFeedback = await _context.EventFeedbacks
                .AnyAsync(f => f.EventId == eventId && f.UserId == userId);

            if (hasGivenFeedback)
            {
                TempData["Error"] = "You have already given feedback for this event.";
                return RedirectToAction("Details", new { id = eventId });
            }

            ViewBag.Event = eventItem;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SubmitFeedback(int eventId, int Rating, string DifficultyExperience, string Comment)
        {
            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var hasGivenFeedback = await _context.EventFeedbacks
                .AnyAsync(f => f.EventId == eventId && f.UserId == userId);

            if (hasGivenFeedback)
            {
                TempData["Error"] = "You have already given feedback for this event.";
                return RedirectToAction("Details", new { id = eventId });
            }

            var feedback = new EventFeedback
            {
                EventId = eventId,
                UserId = userId ?? "",
                Rating = Rating,
                DifficultyExperience = DifficultyExperience,
                Comment = Comment,
                CreatedAt = DateTime.Now
            };

            _context.EventFeedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Thank you for your feedback!";
            return RedirectToAction("Details", new { id = eventId });
        }
    }
}