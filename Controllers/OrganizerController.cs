using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Organizer")]
    public class OrganizerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<OrganizerController> _logger;

        public OrganizerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ILogger<OrganizerController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var now = DateTime.Now;
            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserId = currentUser?.Id ?? string.Empty;

            // Scoped at the query level (stable OrganizerId, never the
            // legacy OrganizedBy display name) so this never fetches every
            // Organizer's Events and filters afterward - a null-owned
            // legacy Event never matches and is never attributed to
            // whichever Organizer happens to be viewing the dashboard.
            var ownedEvents = await _context.Events
                .AsNoTracking()
                .Where(e => e.OrganizerId != null && e.OrganizerId == currentUserId)
                .ToListAsync();

            var eventIds = ownedEvents.Select(e => e.Id).ToList();
            var registrationStatusCounts = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var registrationCountsByStatus = registrationStatusCounts.ToDictionary(x => x.Status, x => x.Count);

            var joinableEvents = ownedEvents
                .Where(EventJoinabilityHelper.IsJoinable)
                .OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id)
                .ToList();
            var upcomingEvents = joinableEvents;

            var currentMonthStart = new DateTime(now.Year, now.Month, 1);
            var chartStart = currentMonthStart.AddMonths(-5);
            var nextMonthStart = currentMonthStart.AddMonths(1);
            var registrationsByMonth = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId) && r.RegisteredAt >= chartStart && r.RegisteredAt < nextMonthStart)
                .GroupBy(r => new { r.RegisteredAt.Year, r.RegisteredAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();
            var monthCounts = registrationsByMonth.ToDictionary(x => (x.Year, x.Month), x => x.Count);
            var trendData = Enumerable.Range(0, 6).Select(offset =>
            {
                var month = chartStart.AddMonths(offset);
                return new MonthlyTrendData { Month = month.ToString("MMM"), Count = monthCounts.GetValueOrDefault((month.Year, month.Month)) };
            }).ToList();

            var suitabilityCounts = await (
                from assessment in _context.Assessments.AsNoTracking()
                join registration in _context.EventRegistrations.AsNoTracking() on assessment.Id equals registration.AssessmentId
                where eventIds.Contains(registration.EventId) && assessment.IsActive &&
                    (assessment.Result == "Good-Match" || assessment.Result == "Borderline" || assessment.Result == "Not Recommended")
                group assessment by assessment.Result into grouped
                select new { Result = grouped.Key!, Count = grouped.Count() }
            ).ToListAsync();
            var suitabilityCountsByResult = suitabilityCounts.ToDictionary(x => x.Result, x => x.Count);
            var totalAssessments = suitabilityCounts.Sum(x => x.Count);
            var suitabilityData = new[] { "Good-Match", "Borderline", "Not Recommended" }.Select(result =>
            {
                var count = suitabilityCountsByResult.GetValueOrDefault(result);
                return new SuitabilityData { Result = result, Count = count, Percentage = totalAssessments > 0 ? (int)Math.Round((double)count / totalAssessments * 100) : 0 };
            }).ToList();

            var overdueEvents = ownedEvents.Where(EventJoinabilityHelper.RequiresManualClosure)
                .OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id)
                .Select(e => new OrganizerAttentionItem
                {
                    Title = e.EventTitle,
                    Detail = $"Event date passed on {e.EventDate:MMM dd}; mark it completed or reschedule it.",
                    ActionLabel = "Manage event",
                    Controller = "Event",
                    Action = "Details",
                    Id = e.Id
                }).ToList();
            var paymentAttention = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "For Payment Verification")
                .OrderBy(r => r.PaymentReceiptUploadedAt ?? r.RegisteredAt).ThenBy(r => r.Id)
                .Select(r => new OrganizerAttentionItem { Title = r.ParticipantName, Detail = "Payment receipt is awaiting verification.", ActionLabel = "Verify payment", Controller = "Organizer", Action = "RegistrationDetails", Id = r.Id })
                .ToListAsync();
            var reviewAttention = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Pending")
                .OrderBy(r => r.RegisteredAt).ThenBy(r => r.Id)
                .Select(r => new OrganizerAttentionItem { Title = r.ParticipantName, Detail = "Registration is awaiting your review.", ActionLabel = "Review registration", Controller = "Organizer", Action = "RegistrationDetails", Id = r.Id })
                .ToListAsync();

            var viewModel = new OrganizerDashboardViewModel
            {
                UpcomingEventsCount = joinableEvents.Count,
                PendingReviewCount = registrationCountsByStatus.GetValueOrDefault("Pending"),
                PaymentsToVerifyCount = registrationCountsByStatus.GetValueOrDefault("For Payment Verification"),
                AcceptedRegistrationsCount = registrationCountsByStatus.GetValueOrDefault("Accepted"),
                TrendData = trendData,
                SuitabilityBreakdown = suitabilityData,
                TotalAssessments = totalAssessments,
                AttentionItems = overdueEvents.Concat(paymentAttention).Concat(reviewAttention).ToList(),
                UpcomingEvents = upcomingEvents.Select(e => new OrganizerUpcomingEventData
                {
                    EventId = e.Id, EventTitle = e.EventTitle, EventDate = e.EventDate, EventTime = e.EventTime,
                    WeatherRiskLevel = e.WeatherRiskLevel
                }).ToList()
            };

            return View(viewModel);
        }

        // Event management itself lives on EventController.Index (shared by
        // Admin and Organizer, with its own access rules) - this action only
        // forwards there. It used to also build and immediately discard an
        // Event query of its own (legacy OrganizedBy matching, never
        // returned or rendered); removed rather than reworked into an
        // OrganizerId-scoped query, since no Event data is actually needed
        // for a redirect.
        public IActionResult Events(string searchString, string status, string sortOrder)
        {
            return RedirectToAction("Index", "Event", new { searchString, status, sortOrder });
        }

        public async Task<IActionResult> Registrations(string searchString, string statusFilter, string sortOrder)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = statusFilter;
            ViewData["CurrentSort"] = sortOrder;

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserId = currentUser?.Id ?? string.Empty;

            // Scoped at the query level (stable OrganizerId, never the
            // legacy OrganizedBy display name) - see the Index dashboard
            // action's identical reasoning.
            var eventIds = await _context.Events
                .Where(e => e.OrganizerId != null && e.OrganizerId == currentUserId)
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
                GearItems = r.Assessment?.GearItems,
                MedicalClearanceRequired = r.Assessment?.MedicalClearanceRequired ?? false,
                MedicalClearanceUrl = r.MedicalClearanceUrl
            }).ToList();

            return View(viewModel);
        }

        public async Task<IActionResult> RegistrationDetails(int id)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["Error"] = "Registration not found";
                return RedirectToAction("Registrations");
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);

            // Ownership checked before anything about this registration -
            // participant identity, medical/suitability data, ML
            // explanation, alternative-event recommendations - is exposed.
            // A missing registration and one belonging to another
            // Organizer's Event return the exact same generic response.
            if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
            {
                TempData["Error"] = "Registration not found";
                return RedirectToAction("Registrations");
            }

            if (registration.Assessment != null)
            {
                ViewBag.AlternativeEvents = await GetAlternativeEvents(
                    registration.Event!.Id,
                    registration.Event.Difficulty ?? "",
                    registration.Assessment!.Result ?? ""
                );

                var suitabilityResult = await _context.SuitabilityResults
                    .Include(s => s.ShapValues)
                    .FirstOrDefaultAsync(s => s.AssessmentId == registration.Assessment.Id);

                if (suitabilityResult != null)
                {
                    ViewBag.HasMlPrediction = true;
                    ViewBag.MlConfidence = suitabilityResult.ConfidenceScore;
                    ViewBag.MlModelVersion = suitabilityResult.ModelVersion;
                    ViewBag.ShapFactors = ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues);
                    ViewBag.GateReason = suitabilityResult.GateReason;
                }
                else
                {
                    ViewBag.HasMlPrediction = false;
                }
            }

            return View(registration);
        }

        // Alternative recommendations intentionally include suitable public Events from all Organizers.
        // Also the authoritative candidate rule for RecommendAlternative's
        // server-side validation - do not duplicate this predicate elsewhere.
        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result)
        {
            var difficultyLevels = DifficultyCalculator.Bands;
            var currentIndex = Array.IndexOf(difficultyLevels, currentDifficulty);
            if (currentIndex < 0) currentIndex = 1;

            int targetIndex;
            if (result == "Good-Match")
                targetIndex = currentIndex;
            else if (result == "Borderline")
                targetIndex = Math.Max(0, currentIndex - 1);
            else
                targetIndex = Math.Max(0, currentIndex - 2);

            var targetDifficulty = difficultyLevels[targetIndex];

            // Mirrors EventJoinabilityHelper.IsJoinable (inlined for EF SQL translation).
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecommendAlternative([FromBody] RecommendAlternativeRequest request)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.Assessment)
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId);

            if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (request.AlternativeEventIds == null || request.AlternativeEventIds.Length == 0)
            {
                return Json(new { success = false, message = "Pumili muna ng kahit isang alternative event." });
            }

            var submittedAlternativeEventId = request.AlternativeEventIds.First();

            // Re-derives the same candidate set GetAlternativeEvents already
            // builds for this registration, so the UI's candidate list and
            // this validation can never independently drift. A submitted ID
            // outside that set (tampered, stale, or otherwise never offered)
            // is rejected without distinguishing why.
            var candidateEvents = await GetAlternativeEvents(
                registration.Event.Id,
                registration.Event.Difficulty ?? "",
                registration.Assessment?.Result ?? "");

            if (!candidateEvents.Any(e => e.Id == submittedAlternativeEventId))
            {
                return Json(new { success = false, message = "That event is not an available alternative for this participant." });
            }

            registration.Status = "Alternative Recommended";
            registration.AlternativeEventId = submittedAlternativeEventId;

            if (!string.IsNullOrWhiteSpace(request.Reason))
            {
                registration.DecisionReason = request.Reason;
            }

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRegistrationStatus([FromBody] UpdateRegistrationStatusRequest request)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (request.Status == "Accepted")
            {
                var approvedAt = DateTime.Now;
                var deadline = approvedAt.Date.AddDays(3).AddHours(23).AddMinutes(59).AddSeconds(59);

                if (registration.Event != null)
                {
                    var eveOfEventDeadline = registration.Event.EventDate.Date.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                    if (eveOfEventDeadline < deadline)
                    {
                        deadline = eveOfEventDeadline;
                    }
                }

                registration.ApprovedAt = approvedAt;
                registration.PaymentDeadline = deadline;
                registration.Status = "Awaiting Payment";
            }
            else
            {
                registration.Status = request.Status;
            }

            if (!string.IsNullOrWhiteSpace(request.Reason))
            {
                registration.DecisionReason = request.Reason;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Registration status updated to {registration.Status}" });
        }

        public class VerifyPaymentRequest
        {
            public int Id { get; set; }
            public bool Approved { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (registration.Status != "For Payment Verification")
            {
                return Json(new { success = false, message = "This registration is not awaiting payment verification." });
            }

            if (request.Approved)
            {
                registration.IsPaid = true;
                registration.Status = "Accepted";
            }
            else
            {
                registration.Status = "Awaiting Payment";
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = request.Approved
                    ? "Payment verified. Registration accepted."
                    : "Payment rejected. Participant can re-upload their receipt."
            });
        }

        public async Task<IActionResult> EventDetails(int id)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventItem == null || !OwnsEvent(eventItem, currentUser))
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var capacityRegistrations = await _context.EventRegistrations
                .Where(r => r.EventId == id && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status))
                .ToListAsync();

            var allRegistrations = await _context.EventRegistrations
                .Include(r => r.User)
                .Include(r => r.Assessment)
                .Where(r => r.EventId == id && r.Status != "Rejected" && r.Status != "Cancelled")
                .ToListAsync();

            ViewBag.Registrations = allRegistrations;
            ViewBag.RegisteredCount = capacityRegistrations.Count;
            ViewBag.AvailableSlots = eventItem.Capacity - capacityRegistrations.Count;
            ViewBag.Trail = eventItem.Trail;

            return View(eventItem);
        }

        // Single ownership rule for every Organizer-facing Event/Registration
        // action in this controller (dashboard, registration list/details,
        // event details, comparison, and the registration-mutation actions
        // below): the whole controller is already gated to the Organizer
        // role by the class-level [Authorize], and unlike EventController
        // (which grants an Admin full access regardless of ownership),
        // nobody gets a free pass here - a dual-role Admin+Organizer account
        // reaching one of these routes is still just "an Organizer here" and
        // may only act on an Event whose stable OrganizerId matches their own
        // account. A null OrganizerId (an unresolved legacy Event - see
        // CLAUDE.md) or a different Organizer's ID are both denied
        // identically - ownership is never inferred from OrganizedBy, email,
        // or display name.
        private static bool OwnsEvent(Event eventItem, ApplicationUser currentUser)
        {
            return eventItem.OrganizerId != null && eventItem.OrganizerId == currentUser.Id;
        }

        // Assess Participants carries one further, stricter rule on top of
        // OwnsEvent: it is explicitly unavailable to Admin (and therefore to
        // a dual-role Admin+Organizer account, which always also holds the
        // Admin role and follows the Admin branch everywhere else in this
        // app), even for an Event that account otherwise owns.
        private async Task<bool> CanAssessEventAsync(Event eventItem, ApplicationUser currentUser)
        {
            if (await _userManager.IsInRoleAsync(currentUser, "Admin")) return false;
            return OwnsEvent(eventItem, currentUser);
        }

        public async Task<IActionResult> PostEventAssessment(int eventId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["Error"] = "Unable to verify your account. Please sign in again.";
                return RedirectToAction("Events");
            }

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null || !await CanAssessEventAsync(eventItem, currentUser))
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitPostEventAssessment([FromBody] SubmitAssessmentRequest request)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Unable to verify your account. Please sign in again." });
                }

                // Assess Participants is Organizer-only - a dual-role
                // Admin+Organizer account is denied the same as an
                // Admin-only account, before any registration/event data is
                // looked up or revealed.
                if (await _userManager.IsInRoleAsync(currentUser, "Admin"))
                {
                    return Json(new { success = false, message = "Registration not found." });
                }

                var registration = await _context.EventRegistrations
                    .Include(r => r.User)
                    .Include(r => r.Event)
                    .FirstOrDefaultAsync(r => r.Id == request.RegistrationId);

                if (registration == null || registration.Event == null)
                {
                    return Json(new { success = false, message = "Registration not found." });
                }

                // OwnsEvent is the same shared ownership check every other
                // Organizer-facing action in this controller uses, checked
                // before anything else about this registration/event is
                // revealed - a null OrganizerId (an unresolved legacy Event)
                // or a different Organizer's ID are both denied the same way
                // as a registration that doesn't exist at all.
                if (!OwnsEvent(registration.Event, currentUser))
                {
                    return Json(new { success = false, message = "Registration not found." });
                }

                // The registration's own, server-loaded EventId is what's actually
                // used below - this only confirms the client's submitted EventId
                // (and therefore the participant it thinks it's saving) actually
                // belongs to it, rather than trusting request.EventId on its own.
                if (registration.EventId != request.EventId)
                {
                    return Json(new { success = false, message = "This registration does not belong to the specified event." });
                }

                if (registration.Event.Status != "Completed")
                {
                    return Json(new { success = false, message = "This event is not yet completed." });
                }

                if (registration.Status != "Accepted")
                {
                    return Json(new { success = false, message = "This participant is not an accepted registrant for this event." });
                }

                // FinalLabelService.IsKnownOutcome is the existing canonical
                // exact-membership check for this value - it is false for
                // anything outside the seven outcome strings the form can
                // submit (unknown, blank, differently cased, or fabricated).
                if (!FinalLabelService.IsKnownOutcome(request.DifficultyExperience))
                {
                    return Json(new { success = false, message = "Please select a valid hike outcome." });
                }

                var userId = registration.UserId;

                var existingAssessment = await _context.PostEventAssessments
                    .FirstOrDefaultAsync(a => a.EventId == registration.EventId && a.UserId == userId);

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
                        EventId = registration.EventId,
                        UserId = userId,
                        DifficultyExperience = request.DifficultyExperience,
                        Notes = request.Notes,
                        CreatedAt = DateTime.Now
                    };
                    _context.PostEventAssessments.Add(assessment);
                }

                await _context.SaveChangesAsync();

                await FinalLabelService.UpsertFinalLabel(_context, registration.Id);

                return Json(new { success = true, message = "Assessment saved successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save post-event assessment for registration {RegistrationId}.", request.RegistrationId);
                return Json(new { success = false, message = "Unable to save the assessment right now. Please try again." });
            }
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
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events");
            }

            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            // A pure Organizer and a dual-role Admin+Organizer account are
            // treated identically here - OwnsEvent grants no Admin bypass,
            // so entering this controller never yields cross-Organizer
            // comparison data regardless of what other roles the account
            // also holds. An Admin-only account never reaches this action at
            // all, per the controller's own [Authorize(Roles = "Organizer")].
            if (eventItem == null || !OwnsEvent(eventItem, currentUser))
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

            var finalLabels = await _context.FinalSuitabilityLabels
                .Where(l => l.EventId == eventId)
                .ToDictionaryAsync(l => l.RegistrationId, l => l.FinalLabel);

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
                var finalCategory = FinalLabelService.MapFeedbackToClass(finalResult);
                var classification = FinalLabelService.ClassifyAccuracy(preHike, finalCategory);
                var comparison = ComparisonDisplay(classification);

                results.Add(new ComparisonResult
                {
                    ParticipantName = reg.User != null ? $"{reg.User.FirstName} {reg.User.LastName}" : reg.ParticipantName,
                    PreHikeAssessment = preHike,
                    ParticipantFeedback = participantFeedback,
                    OrganizerAssessment = organizerAssessment,
                    FinalResult = finalResult,
                    FinalLabel = finalLabels.ContainsKey(reg.Id) ? finalLabels[reg.Id] : null,
                    Comparison = comparison.Item1,
                    ComparisonColor = comparison.Item2,
                    ComparisonIcon = comparison.Item3,
                    IsMissedRisk = classification == "Missed risk"
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

            return FinalLabelService.GetMoreConservativeFeedback(participantFeedback, organizerAssessment) ?? "Insufficient data";
        }

        // Display mapping for FinalLabelService.ClassifyAccuracy's three outcomes, shared
        // in spirit with ReportsController — same classification, same names, so the
        // per-event and aggregate views can't independently invert this comparison again.
        private static Tuple<string, string, string> ComparisonDisplay(string? classification) => classification switch
        {
            "Accurate" => Tuple.Create("✅ Accurate", "text-green-400", "fa-check-circle"),
            "Over-cautious" => Tuple.Create("⚠️ Over-cautious", "text-yellow-400", "fa-triangle-exclamation"),
            "Missed risk" => Tuple.Create("🚨 Missed risk", "text-red-400", "fa-shield-halved"),
            _ => Tuple.Create("Insufficient Data", "text-gray-400", "fa-minus-circle")
        };

    }
}
