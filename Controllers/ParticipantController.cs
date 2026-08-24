using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class ParticipantController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly WeatherService _weatherService;


        public ParticipantController(ApplicationDbContext context, WeatherService weatherService)
        {
            _context = context;
            _weatherService = weatherService;
        }

        public async Task<IActionResult> Index()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Where(r => r.UserId == userId && r.Status != "Cancelled")
                .ToListAsync();

            var upcomingEvents = registrations
                .Where(r => r.Status == "Accepted")
                .Select(r => r.Event)
                .Where(e => e != null && e.EventDate >= DateTime.Today && e.Status == "Upcoming")
                .ToList();

            var completedRegistrations = registrations
                .Where(r => r.Status == "Accepted" && r.Event != null && r.Event.Status == "Completed")
                .ToList();
            var completedHikes = completedRegistrations.Count;

            var needsAction = registrations
                .Count(r => r.Status == "Pending" || r.Status == "Awaiting Payment");

            var activeRegistrations = registrations
                .Count(r => RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));

            var latestAssessment = registrations
                .Where(r => r.Assessment != null && r.Assessment.IsActive == true)
                .Select(r => r.Assessment)
                .OrderByDescending(a => a!.SubmittedAt)
                .FirstOrDefault();

            LatestAssessmentResult? latestResult = null;
            if (latestAssessment != null)
            {
                var latestRegistration = registrations.First(r => r.Assessment == latestAssessment);
                var latestEvent = latestRegistration.Event;

                var suitabilityResult = await _context.SuitabilityResults
                    .FirstOrDefaultAsync(sr => sr.AssessmentId == latestAssessment.Id);

                latestResult = new LatestAssessmentResult
                {
                    Result = latestAssessment.Result ?? "Not Recommended",
                    Description = GetAssessmentDescription(latestAssessment.Result ?? ""),
                    SubmittedAt = latestAssessment.SubmittedAt,
                    ConfidenceScore = suitabilityResult?.ConfidenceScore ?? 0,
                    HasMlPrediction = suitabilityResult != null,
                    AssessmentId = latestAssessment.Id,
                    EventId = latestEvent?.Id ?? 0,
                    EventTitle = latestEvent?.EventTitle ?? "",
                    TrailName = latestEvent?.Trail?.Name ?? "",
                    EventDifficulty = latestEvent?.Difficulty ?? ""
                };
            }

            var recommendedEvents = new List<Event>();
            if (latestResult != null && !string.IsNullOrEmpty(userId))
            {
                recommendedEvents = await GetRecommendedEvents(latestResult.Result, latestResult.EventDifficulty, userId);
            }

            var difficultyLevels = DifficultyCalculator.Bands;

            string? personalBestDifficulty = null;
            double? personalBestDistanceKm = null;
            int? personalBestElevationMeters = null;

            var hikesWithTrail = completedRegistrations.Where(r => r.Event!.Trail != null).ToList();
            if (hikesWithTrail.Any())
            {
                personalBestDifficulty = hikesWithTrail
                    .Select(r => r.Event!.Difficulty)
                    .OrderByDescending(d => Array.IndexOf(difficultyLevels, d))
                    .FirstOrDefault();

                personalBestDistanceKm = hikesWithTrail.Max(r => r.Event!.Trail!.DistanceKm);
                personalBestElevationMeters = hikesWithTrail.Max(r => r.Event!.Trail!.ElevationGainMeters);
            }

            var hikeCounts = await _context.EventRegistrations
                .Where(r => r.Status == "Accepted" && r.Event!.Status == "Completed")
                .GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();

            var rank = hikeCounts.Count(x => x.Count > completedHikes) + 1;
            var totalHikers = hikeCounts.Count;
            var isRanked = completedHikes > 0 && rank <= 10;

            var viewModel = new ParticipantDashboardViewModel
            {
                UpcomingEventsCount = upcomingEvents.Count,
                CompletedHikes = completedHikes,
                PendingRegistrations = needsAction,
                TotalRegistrations = activeRegistrations,
                UpcomingEvents = upcomingEvents!,
                LatestAssessment = latestResult,
                RecommendedEvents = recommendedEvents,
                PersonalBestDifficulty = personalBestDifficulty,
                PersonalBestDistanceKm = personalBestDistanceKm,
                PersonalBestElevationMeters = personalBestElevationMeters,
                Rank = rank,
                TotalHikers = totalHikers,
                IsRanked = isRanked
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetEventWeather(int eventId)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null || eventItem.Trail == null)
            {
                return Json(new { success = false, unavailableReason = "NoLocation" });
            }

            var forecast = await _weatherService.GetWeatherForecastAsync(eventItem.Trail.Location, eventItem.EventDate);

            if (!string.IsNullOrEmpty(forecast.UnavailableReason))
            {
                return Json(new
                {
                    success = false,
                    unavailableReason = forecast.UnavailableReason
                });
            }

            var previousRiskLevel = eventItem.WeatherRiskLevel;

            eventItem.WeatherForecastAdvisory = forecast.ForecastDetails;
            eventItem.WeatherRiskLevel = forecast.RiskLevel;

            if (string.IsNullOrEmpty(eventItem.WeatherReminder) || previousRiskLevel != forecast.RiskLevel)
            {
                // Reminder is either unset or no longer matches the current conditions — refresh it.
                // Otherwise the organizer already edited it and a background refresh shouldn't discard that.
                eventItem.WeatherReminder = forecast.SuggestedReminder;
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                riskLevel = eventItem.WeatherRiskLevel,
                details = eventItem.WeatherForecastAdvisory,
                reminder = eventItem.WeatherReminder
            });
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

        private async Task<List<Event>> GetRecommendedEvents(
            string assessmentResult, string assessedDifficulty, string userId)
        {
            var levels = DifficultyCalculator.Bands;

            var currentIndex = Array.IndexOf(levels, assessedDifficulty);
            if (currentIndex < 0) currentIndex = 1;

            var targetIndex = assessmentResult switch
            {
                "Good-Match" => currentIndex,
                "Borderline" => Math.Max(0, currentIndex - 1),
                _ => 0
            };

            var registeredEventIds = await _context.EventRegistrations
                .Where(r => r.UserId == userId && r.Status != "Cancelled" && r.Status != "Rejected")
                .Select(r => r.EventId)
                .ToListAsync();

            for (var i = targetIndex; i >= 0; i--)
            {
                var events = await _context.Events
                    .Include(e => e.Trail)
                    .Where(e => e.Status == "Upcoming"
                             && e.EventDate >= DateTime.Today
                             && e.Difficulty == levels[i]
                             && !registeredEventIds.Contains(e.Id))
                    .OrderBy(e => e.EventDate)
                    .Take(4)
                    .ToListAsync();

                if (events.Any()) return events;
            }

            return new List<Event>();
        }
        public async Task<IActionResult> Trails()
        {
            var trails = await _context.Trails
                .OrderByDescending(t => t.DateAdded)
                .ToListAsync();

            return View(trails);
        }

        public async Task<IActionResult> Events(string searchString, string difficulty, string trailFilter, string sortOrder)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentDifficulty"] = difficulty;
            ViewData["CurrentTrailFilter"] = trailFilter;
            ViewData["CurrentSort"] = sortOrder;

            ViewBag.Trails = await _context.Trails.OrderBy(t => t.Name).ToListAsync();

            var events = _context.Events
                .Include(e => e.Trail)
                .Where(e => e.Status == "Upcoming" && e.EventDate >= DateTime.Today)
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

            List<Event> eventsList;
            if (sortOrder == "difficulty_asc" || sortOrder == "difficulty_desc")
            {
                // Event.Difficulty is a band name ("Easy", "Minor Climb", ...),
                // not a rank - an alphabetical OrderBy on the string only happened to match
                // severity order for today's exact band names and would silently break the
                // moment a label changed. Sorting on the underlying NPS rating can't drift
                // that way, and it's the only way to order two trails that share a band.
                eventsList = await events.ToListAsync();
                eventsList = sortOrder == "difficulty_asc"
                    ? eventsList.OrderBy(e => e.Trail != null ? DifficultyCalculator.ComputeRating(e.Trail) : 0).ToList()
                    : eventsList.OrderByDescending(e => e.Trail != null ? DifficultyCalculator.ComputeRating(e.Trail) : 0).ToList();
            }
            else
            {
                events = sortOrder switch
                {
                    "date_desc" => events.OrderByDescending(e => e.EventDate),
                    "title_asc" => events.OrderBy(e => e.EventTitle),
                    "title_desc" => events.OrderByDescending(e => e.EventTitle),
                    _ => events.OrderBy(e => e.EventDate),
                };
                eventsList = await events.ToListAsync();
            }

            var eventIds = eventsList.Select(e => e.Id).ToList();
            var capacityCounts = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .GroupBy(r => r.EventId)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventId, x => x.Count);

            foreach (var e in eventsList)
            {
                e.RegisteredCount = capacityCounts.TryGetValue(e.Id, out var count) ? count : 0;
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRegistrations = await _context.EventRegistrations
                .Where(r => eventIds.Contains(r.EventId) && r.UserId == userId)
                .ToListAsync();

            // A participant can hold several rows for one event (cancel, then register
            // again), so this can't be a plain lookup - the active one wins if there is
            // one, otherwise fall back to whichever attempt is most recent.
            var statusByEventId = userRegistrations
                .GroupBy(r => r.EventId)
                .ToDictionary(
                    g => g.Key,
                    g => (g.FirstOrDefault(r => RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                          ?? g.OrderByDescending(r => r.RegisteredAt).First()).Status);

            var cardViewModels = eventsList
                .Where(e => e.Trail != null)
                .Select(e => new EventBrowseCardViewModel
                {
                    Event = e,
                    RegistrationStatus = statusByEventId.TryGetValue(e.Id, out var status) ? status : null
                })
                .ToList();

            return View(cardViewModels);
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
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
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
        public async Task<IActionResult> SubmitFeedback(
            int eventId,
            int Rating,
            string DifficultyExperience,
            string TrailCondition,
            string TrailSignage,
            string WaterSourceAvailability,
            string? HazardsEncountered,
            string PreEventCommunication,
            string SafetyManagement,
            string GroupManagement,
            string? Comment)
        {
            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            if (Rating < 1 || Rating > 5 ||
                string.IsNullOrWhiteSpace(DifficultyExperience) ||
                string.IsNullOrWhiteSpace(TrailCondition) ||
                string.IsNullOrWhiteSpace(TrailSignage) ||
                string.IsNullOrWhiteSpace(WaterSourceAvailability) ||
                string.IsNullOrWhiteSpace(PreEventCommunication) ||
                string.IsNullOrWhiteSpace(SafetyManagement) ||
                string.IsNullOrWhiteSpace(GroupManagement))
            {
                TempData["Error"] = "Please complete all required fields before submitting.";
                return RedirectToAction("Details", new { id = eventId });
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
                TrailCondition = TrailCondition,
                TrailSignage = TrailSignage,
                WaterSourceAvailability = WaterSourceAvailability,
                HazardsEncountered = HazardsEncountered,
                PreEventCommunication = PreEventCommunication,
                SafetyManagement = SafetyManagement,
                GroupManagement = GroupManagement,
                Comment = Comment,
                CreatedAt = DateTime.Now
            };

            _context.EventFeedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);
            if (registration != null)
            {
                await FinalLabelService.UpsertFinalLabel(_context, registration.Id);
            }

            TempData["Success"] = "Thank you for your feedback!";
            return RedirectToAction("Details", new { id = eventId });
        }
    }
}