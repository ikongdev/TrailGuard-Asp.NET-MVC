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
        private readonly ParticipantProgressService _participantProgressService;


        public ParticipantController(ApplicationDbContext context, WeatherService weatherService, ParticipantProgressService participantProgressService)
        {
            _context = context;
            _weatherService = weatherService;
            _participantProgressService = participantProgressService;
        }

        public async Task<IActionResult> Index()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
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
                    CompletionProbability = suitabilityResult?.CompletionProbability ?? 0,
                    HasMlPrediction = suitabilityResult != null,
                    AssessmentId = latestAssessment.Id,
                    EventId = latestEvent?.Id ?? 0,
                    EventTitle = latestEvent?.EventTitle ?? "",
                    TrailName = latestEvent?.TrailNameSnapshot ?? "",
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






            if (completedRegistrations.Any())
            {
                personalBestDifficulty = completedRegistrations
                    .Select(r => r.Event!.Difficulty)
                    .OrderByDescending(d => Array.IndexOf(difficultyLevels, d))
                    .FirstOrDefault();

                personalBestDistanceKm = completedRegistrations.Max(r => r.Event!.TrailDistanceKmSnapshot);
                personalBestElevationMeters = completedRegistrations.Max(r => r.Event!.TrailElevationGainMetersSnapshot);
            }





            var progress = string.IsNullOrEmpty(userId)
                ? new ParticipantProgressResult()
                : await _participantProgressService.GetProgressAsync(userId);

            var viewModel = new ParticipantDashboardViewModel
            {
                UpcomingEventsCount = upcomingEvents.Count,
                CompletedHikes = progress.DistinctCompletedEventCount,
                PendingRegistrations = needsAction,
                TotalRegistrations = activeRegistrations,
                UpcomingEvents = upcomingEvents!,
                LatestAssessment = latestResult,
                RecommendedEvents = recommendedEvents,
                PersonalBestDifficulty = personalBestDifficulty,
                PersonalBestDistanceKm = personalBestDistanceKm,
                PersonalBestElevationMeters = personalBestElevationMeters,
                TrailPoints = progress.TrailPoints,
                Rank = progress.Rank ?? 0,
                TotalHikers = progress.RankedParticipantCount,
                IsRanked = progress.IsRanked
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetEventWeather(int eventId)
        {
            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null || string.IsNullOrEmpty(eventItem.Location))
            {
                return Json(new { success = false, unavailableReason = "NoLocation" });
            }




            var forecast = await _weatherService.GetWeatherForecastAsync(eventItem.Location, eventItem.EventDate);

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
                .Where(t => t.IsActive)
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





            if (!string.IsNullOrEmpty(trailFilter) && trailFilter != "All" && int.TryParse(trailFilter, out var trailId))
            {
                events = events.Where(e => e.TrailId == trailId);
            }

            List<Event> eventsList;
            if (sortOrder == "difficulty_asc" || sortOrder == "difficulty_desc")
            {










                eventsList = sortOrder == "difficulty_asc"
                    ? await events.OrderBy(e => e.TrailAdjustedRatingSnapshot).ToListAsync()
                    : await events.OrderByDescending(e => e.TrailAdjustedRatingSnapshot).ToListAsync();
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




            var statusByEventId = userRegistrations
                .GroupBy(r => r.EventId)
                .ToDictionary(
                    g => g.Key,
                    g => (g.FirstOrDefault(r => RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                          ?? g.OrderByDescending(r => r.RegisteredAt).First()).Status);

            var cardViewModels = eventsList
                .Select(e => new EventBrowseCardViewModel
                {
                    Event = e,
                    RegistrationStatus = statusByEventId.TryGetValue(e.Id, out var status) ? status : null
                })
                .ToList();

            return View(cardViewModels);
        }


        [HttpGet]
        public async Task<JsonResult> GetTrailEvents(int trailId)
        {


            if (trailId <= 0)
            {
                return Json(Array.Empty<object>());
            }

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

















        [HttpGet]
        public async Task<JsonResult> GetTrailPhotos(int trailId)
        {
            if (trailId <= 0)
            {
                return Json(Array.Empty<object>());
            }

            var trailExists = await _context.Trails.AnyAsync(t => t.Id == trailId);
            if (!trailExists)
            {
                return Json(Array.Empty<object>());
            }

            var photos = await _context.TrailPhotos
                .Where(p => p.TrailId == trailId)
                .OrderBy(p => p.DisplayOrder)
                .Select(p => new { url = p.ImageUrl })
                .ToListAsync();

            return Json(photos);
        }

        public async Task<IActionResult> Details(int id)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var registeredCount = await _context.EventRegistrations
                .Where(r => r.EventId == id && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .CountAsync();

            ViewBag.RegisteredCount = registeredCount;
            ViewBag.AvailableSlots = eventItem.Capacity - registeredCount;








            var joinedParticipants = await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.EventId == id && r.Status == "Accepted")
                .OrderBy(r => r.RegisteredAt)
                .Select(r => new ParticipantEventJoinedRowViewModel
                {
                    ParticipantName = r.ParticipantName,
                    ProfilePictureUrl = r.User != null ? r.User.ProfilePictureUrl : null
                })
                .ToListAsync();

            ViewBag.JoinedParticipants = joinedParticipants;








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

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;












            var ownRegistrations = await _context.EventRegistrations
                .Include(r => r.AlternativeEvent)
                .Where(r => r.EventId == id && r.UserId == userId)
                .AsNoTracking()
                .ToListAsync();

            var userRegistration = ownRegistrations.FirstOrDefault(r =>
                    RegistrationStatusHelper.ActiveStatuses.Contains(r.Status) || r.Status == "Alternative Recommended")
                ?? ownRegistrations.OrderByDescending(r => r.RegisteredAt).FirstOrDefault();

            ViewBag.UserRegistration = userRegistration;


            var hasGivenFeedback = false;
            if (userId != null && eventItem.Status == "Completed")
            {
                hasGivenFeedback = await _context.EventFeedbacks
                    .AnyAsync(f => f.EventId == id && f.UserId == userId);
            }
            ViewBag.HasGivenFeedback = hasGivenFeedback;

            return View(eventItem);
        }








        private const string FeedbackIneligibleMessage = "Feedback is available only after completing an event you joined.";


















        private async Task<EventRegistration?> GetEligibleFeedbackRegistrationAsync(Event eventItem)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return null;

            if (eventItem.Status != "Completed") return null;

            return await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.EventId == eventItem.Id && r.UserId == userId && r.Status == "Accepted")
                .OrderByDescending(r => r.RegisteredAt)
                .FirstOrDefaultAsync();
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





            var eligibleRegistration = await GetEligibleFeedbackRegistrationAsync(eventItem);
            if (eligibleRegistration == null)
            {
                TempData["Error"] = FeedbackIneligibleMessage;
                return RedirectToAction("Details", new { id = eventId });
            }

            var hasGivenFeedback = await _context.EventFeedbacks
                .AnyAsync(f => f.EventId == eventId && f.UserId == eligibleRegistration.UserId);

            if (hasGivenFeedback)
            {
                TempData["Error"] = "You have already given feedback for this event.";
                return RedirectToAction("Details", new { id = eventId });
            }

            ViewBag.Event = eventItem;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFeedback(
            int eventId,
            int Rating,
            bool? Completed,
            string? NonCompletionReason,
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







            var eligibleRegistration = await GetEligibleFeedbackRegistrationAsync(eventItem);
            if (eligibleRegistration == null)
            {
                TempData["Error"] = FeedbackIneligibleMessage;
                return RedirectToAction("Details", new { id = eventId });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"EventRegistrations\" WHERE \"Id\" = {eligibleRegistration.Id} FOR UPDATE");
            var hasGivenFeedback = await _context.EventFeedbacks
                .AnyAsync(f => f.EventId == eventId && f.UserId == eligibleRegistration.UserId);

            if (hasGivenFeedback)
            {
                TempData["Error"] = "You have already given feedback for this event.";
                return RedirectToAction("Details", new { id = eventId });
            }

            if (!FinalLabelService.IsValidCompletion(Completed, NonCompletionReason) ||
                !FinalLabelService.IsKnownOutcome(DifficultyExperience) || Rating < 1 || Rating > 5 ||
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

            var feedback = new EventFeedback
            {
                EventId = eventId,
                UserId = eligibleRegistration.UserId,
                Rating = Rating,
                Completed = Completed,
                NonCompletionReason = Completed == true ? "NotApplicable" : NonCompletionReason,
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








            await FinalLabelService.UpsertFinalLabel(_context, eligibleRegistration.Id);

            await transaction.CommitAsync();

            TempData["Success"] = "Thank you for your feedback!";
            return RedirectToAction("Details", new { id = eventId });
        }
    }
}
