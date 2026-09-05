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

            // Personal-best difficulty/distance/elevation below stay on this
            // already-loaded, per-user registrations list rather than moving to
            // ParticipantProgressService - Max/OrderByDescending selections are
            // unaffected by a duplicate Accepted+Completed row for the same Event, so
            // this list needs no separate GroupBy-by-EventId deduplication step to stay
            // correct. CompletedHikes below, by contrast, is a plain count, so it comes
            // from the shared service instead of registrations.Count.
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
                    ConfidenceScore = suitabilityResult?.ConfidenceScore ?? 0,
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

            // Personal bests read each completed hike's own frozen Trail
            // Snapshot (TrailDistanceKmSnapshot/TrailElevationGainMetersSnapshot),
            // never the live Trail - editing a Trail's distance/elevation must
            // never retroactively change a participant's already-earned
            // personal-best record. See CLAUDE.md, "Event Trail Snapshot".
            if (completedRegistrations.Any())
            {
                personalBestDifficulty = completedRegistrations
                    .Select(r => r.Event!.Difficulty)
                    .OrderByDescending(d => Array.IndexOf(difficultyLevels, d))
                    .FirstOrDefault();

                personalBestDistanceKm = completedRegistrations.Max(r => r.Event!.TrailDistanceKmSnapshot);
                personalBestElevationMeters = completedRegistrations.Max(r => r.Event!.TrailElevationGainMetersSnapshot);
            }

            // Sole source for the completed-hike count and all-time Trail Points
            // ranking - see ParticipantProgressService/ParticipantProgressPolicy.
            // Leaderboard eligibility is decided entirely inside the service; the
            // controller has no say in whether this account gets ranked.
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

            // Event.Location is the Event's own canonical, immutable snapshot of
            // the Trail's location at capture time - not a live read through
            // Event.Trail. See CLAUDE.md, "Event Trail Snapshot".
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
            // Active Trails only - a deactivated Trail must disappear from Browse
            // Trails, though it remains fully intact in the database and on any
            // Event that already references it. See CLAUDE.md, "Trail
            // Deactivation".
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

            // Matches EventController.Index's established safe-parsing convention: an
            // invalid, non-numeric, zero, or negative trailFilter simply fails to match
            // any real Trail.Id and falls through as the default "All" state - never a
            // thrown FormatException from a malformed manually-supplied query value.
            if (!string.IsNullOrEmpty(trailFilter) && trailFilter != "All" && int.TryParse(trailFilter, out var trailId))
            {
                events = events.Where(e => e.TrailId == trailId);
            }

            List<Event> eventsList;
            if (sortOrder == "difficulty_asc" || sortOrder == "difficulty_desc")
            {
                // Event.Difficulty is a band name ("Easy", "Minor Climb", ...),
                // not a rank - an alphabetical OrderBy on the string only happened to match
                // severity order for today's exact band names and would silently break the
                // moment a label changed. Sorting on the stored adjusted-rating snapshot
                // (TrailAdjustedRatingSnapshot - the same value the band was derived from at
                // capture time) can't drift that way, and it's the only way to order two
                // events that share a band. This must be the stored snapshot, never a live
                // recalculation from the current Trail - see CLAUDE.md, "Event Trail
                // Snapshot" and "Difficulty sorting": an Event's ordering must not change
                // just because its Trail was edited afterward.
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
            // A non-positive id can never match a real Trail.Id - short-circuit rather
            // than let the query run and (harmlessly, but pointlessly) return empty.
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

        // GET: Participant/GetTrailPhotos (for the read-only Trail Details modal's
        // Additional Photos gallery). A narrow, Participant-scoped counterpart to
        // TrailController.GetTrailPhotos (Admin/Organizer-only) - never call or relax
        // that endpoint's authorization for this. Returns only the photo URL: no
        // TrailPhoto.Id (that's a delete target on the Organizer side and this gallery
        // has no delete capability), no uploader/account data, no file-system path.
        //
        // Browse Trails (ParticipantController.Trails, above) now lists Active
        // Trails only, so a normal click-through never reaches this for a
        // deactivated Trail - but this endpoint deliberately still doesn't filter
        // on IsActive itself. A deactivated Trail retains its photos (see
        // CLAUDE.md, "Trail Deactivation") and this is a narrow, read-only,
        // already-Participant-authorized lookup with nothing sensitive to
        // protect; gating it here would only add a second, easily-drifting
        // definition of "visible" without serving any actual catalog-visibility
        // purpose Browse Trails' own filtering doesn't already cover.
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

            // Joined Participants: Accepted-only, minimal safe projection (name +
            // avatar), filtered and ordered in the database - never the full
            // EventRegistration/ApplicationUser entities other participants'
            // sensitive data lives on. See CLAUDE.md, "Joined Participants" /
            // Models/ParticipantEventJoinedRowViewModel. Distinct from
            // registeredCount above, which still counts every ActiveStatuses
            // row for capacity - Accepted-only is a narrower set than that.
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

            // Stable Organizer resolution: OrganizerId is the actual ownership/
            // identity key on Event (see Models/Event.cs) - OrganizedBy is a
            // mutable display-name snapshot that can drift from the account it
            // once matched. A populated but invalid OrganizerId never falls back
            // to a different account that happens to match the display text;
            // only a genuinely legacy Event (OrganizerId null/empty) uses the
            // name/email/id matching fallback. Read-only, so AsNoTracking.
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

            // Scoped to this event and the authenticated user's own stable ID -
            // never a client-supplied ID - so this can never surface another
            // participant's recommendation. Includes AlternativeEvent (not
            // needed by the minimal joinedParticipants projection above, which
            // only drives the public Joined Participants list) so the
            // recommendation panel can render without a second round-trip. A
            // participant can hold more than one
            // row for this event (cancel, then register again), so this can't
            // be a plain FirstOrDefault - the row that's still "live" (active,
            // or the organizer's Alternative Recommended decision) wins, same
            // rule ParticipantController.Events already applies per-card.
            var ownRegistrations = await _context.EventRegistrations
                .Include(r => r.AlternativeEvent)
                .Where(r => r.EventId == id && r.UserId == userId)
                .AsNoTracking()
                .ToListAsync();

            var userRegistration = ownRegistrations.FirstOrDefault(r =>
                    RegistrationStatusHelper.ActiveStatuses.Contains(r.Status) || r.Status == "Alternative Recommended")
                ?? ownRegistrations.OrderByDescending(r => r.RegisteredAt).FirstOrDefault();

            ViewBag.UserRegistration = userRegistration;

            // ✅ I-check kung nagbigay na ng feedback ang participant
            var hasGivenFeedback = false;
            if (userId != null && eventItem.Status == "Completed")
            {
                hasGivenFeedback = await _context.EventFeedbacks
                    .AnyAsync(f => f.EventId == id && f.UserId == userId);
            }
            ViewBag.HasGivenFeedback = hasGivenFeedback;

            return View(eventItem);
        }

        // Single generic rejection message for every feedback-eligibility failure
        // (not Completed, no Accepted registration, missing claim) - never
        // distinguished from each other, so a caller probing eventId values can't
        // learn anything about another participant's registration state. This is
        // a different message from "Event not found" (a separate, earlier
        // failure mode - see GetEligibleFeedbackRegistrationAsync's callers) and
        // from the duplicate-feedback message below, both of which stay distinct.
        private const string FeedbackIneligibleMessage = "Feedback is available only after completing an event you joined.";

        // Single source of truth for feedback eligibility, called independently
        // by both Feedback (GET) and SubmitFeedback (POST) so the two can never
        // drift into different rules - see CLAUDE.md, "Feedback" > "Eligibility".
        // Derives eligibility exclusively from persisted server-side data: the
        // already-loaded Event's own Status, the authenticated user's stable
        // NameIdentifier claim, and a fresh database read of that user's
        // Accepted registration for this Event. Never trusts a posted user ID,
        // posted registration ID/status, a query-string value, or the view's own
        // Give Feedback button visibility. Returns null on any failure - not
        // Completed, no claim, or no Accepted row - without revealing which one.
        // Read-only, so AsNoTracking(); if more than one Accepted row exists for
        // the same participant/event (malformed historical data - registrations
        // are not otherwise unique per participant/event), the newest by
        // RegisteredAt wins deterministically rather than an unordered
        // FirstOrDefault. Never selects a Pending, Awaiting Payment, For Payment
        // Verification, Rejected, Cancelled, Voided, or Alternative Recommended
        // row.
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

            // A direct URL to this page must not bypass eligibility - the Give
            // Feedback button's own visibility on Details is a UX convenience,
            // never the authorization boundary. See CLAUDE.md, "Feedback" >
            // "Eligibility".
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

            // Independently re-checked here, never inferred from the GET having
            // rendered the form - a stale form, a replayed POST, or a crafted
            // direct request must all be rejected the same way GET would reject
            // them. Nothing below this point is trusted until eligibility
            // succeeds: no EventFeedback is added and FinalLabelService is never
            // called for a rejected request.
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

            var feedback = new EventFeedback
            {
                EventId = eventId,
                UserId = eligibleRegistration.UserId,
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

            // The exact Accepted registration that established eligibility above -
            // never a second, broader FirstOrDefault(EventId + UserId) lookup,
            // which could silently resolve to a historical Cancelled/Rejected/
            // Voided/Pending/Alternative Recommended row for the same
            // participant/event and cause UpsertFinalLabel to no-op even though
            // an Accepted registration genuinely exists. See CLAUDE.md,
            // "Feedback" > "Eligibility".
            await FinalLabelService.UpsertFinalLabel(_context, eligibleRegistration.Id);

            TempData["Success"] = "Thank you for your feedback!";
            return RedirectToAction("Details", new { id = eventId });
        }
    }
}