using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        private readonly IUploadStorage? _storage;
        private IUploadStorage Storage => _storage ?? HttpContext.RequestServices.GetRequiredService<IUploadStorage>();
        private readonly ILogger<OrganizerController> _logger;
        private readonly ProfileAccessService _profileAccessService;
        private readonly IPhilippineClock _philippineClock;

        public OrganizerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment webHostEnvironment, ILogger<OrganizerController> logger, ProfileAccessService profileAccessService, IPhilippineClock? philippineClock = null, IUploadStorage? storage = null)
        {
            _context = context;
            _userManager = userManager;
            _storage = storage;
            _logger = logger;
            _profileAccessService = profileAccessService;
            _philippineClock = philippineClock ?? new PhilippineClock(TimeProvider.System);
        }

        public async Task<IActionResult> Index()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var now = DateTime.Now;
            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserId = currentUser?.Id ?? string.Empty;






            var ownedEvents = await _context.Events
                .AsNoTracking()
                .Where(e => e.OrganizerId != null && e.OrganizerId == currentUserId)
                .ToListAsync();

            var eventIds = ownedEvents.Select(e => e.Id).ToList();
            var activeEventIds = ownedEvents
                .Where(e => !RegistrationStatusHelper.IsEventCancelled(e))
                .Select(e => e.Id)
                .ToList();
            var registrationStatusCounts = await _context.EventRegistrations.AsNoTracking()
                .Where(r => eventIds.Contains(r.EventId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var registrationCountsByStatus = registrationStatusCounts.ToDictionary(x => x.Status, x => x.Count);
            var activeRegistrationStatusCounts = await _context.EventRegistrations.AsNoTracking()
                .Where(r => activeEventIds.Contains(r.EventId))
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var activeRegistrationCountsByStatus = activeRegistrationStatusCounts.ToDictionary(x => x.Status, x => x.Count);

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
                .Where(r => activeEventIds.Contains(r.EventId) && r.Status == "For Payment Verification")
                .OrderBy(r => r.PaymentReceiptUploadedAt ?? r.RegisteredAt).ThenBy(r => r.Id)
                .Select(r => new OrganizerAttentionItem { Title = r.ParticipantName, Detail = "Payment receipt is awaiting verification.", ActionLabel = "Verify payment", Controller = "Organizer", Action = "RegistrationDetails", Id = r.Id })
                .ToListAsync();
            var reviewAttention = await _context.EventRegistrations.AsNoTracking()
                .Where(r => activeEventIds.Contains(r.EventId) && r.Status == "Pending")
                .OrderBy(r => r.RegisteredAt).ThenBy(r => r.Id)
                .Select(r => new OrganizerAttentionItem { Title = r.ParticipantName, Detail = "Registration is awaiting your review.", ActionLabel = "Review registration", Controller = "Organizer", Action = "RegistrationDetails", Id = r.Id })
                .ToListAsync();

            var viewModel = new OrganizerDashboardViewModel
            {
                UpcomingEventsCount = joinableEvents.Count,
                PendingReviewCount = activeRegistrationCountsByStatus.GetValueOrDefault("Pending"),
                PaymentsToVerifyCount = activeRegistrationCountsByStatus.GetValueOrDefault("For Payment Verification"),
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




            var assessmentIds = registrationsList
                .Where(r => r.AssessmentId.HasValue)
                .Select(r => r.AssessmentId!.Value)
                .Distinct()
                .ToList();

            var resultCandidatesByAssessmentId = (await _context.SuitabilityResults
                .Include(sr => sr.ShapValues)
                .Where(sr => assessmentIds.Contains(sr.AssessmentId))
                .ToListAsync())
                .GroupBy(result => result.AssessmentId)
                .ToDictionary(group => group.Key, group => SuitabilityResultSelector.Select(group));

            var viewModel = registrationsList.Select(r => new RegistrationWithAssessmentViewModel
            {
                RegistrationId = r.Id,
                EventId = r.EventId,
                EventTitle = r.Event?.EventTitle ?? "Unknown Event",
                EventDate = r.Event?.EventDate.ToString("MMM dd, yyyy") ?? "",
                EventTime = r.Event?.FormattedEventTime ?? "",
                EventDifficulty = DifficultyCalculator.DisplayLabel(r.Event?.Difficulty),
                EventStatus = r.Event?.Status ?? string.Empty,
                EventCancelledAt = r.Event?.CancelledAt,
                EventCancellationReason = r.Event?.CancellationReason,
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
                ModelScore = r.AssessmentId.HasValue && resultCandidatesByAssessmentId.TryGetValue(r.AssessmentId.Value, out var resultSelection)
                    && !resultSelection.IsInvalidOrUnsupported && resultSelection.Result is not null
                    ? resultSelection.Result.ModelScore
                    : (double?)null,
                MedicalConditions = r.Assessment?.MedicalConditions,
                FitnessLevel = r.Assessment?.ExerciseFrequency,
                HikingExperience = r.Assessment?.MountainsClimbed,
                GearItems = r.Assessment?.GearItems,
                RequiresMedicalClearance = r.Assessment != null && RegistrationRulesHelper.RequiresMedicalClearance(r.Assessment),
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
                .Include(r => r.Assessment)
                .Include(r => r.User)
                .Include(r => r.AlternativeEvent)
                .FirstOrDefaultAsync(r => r.Id == id);






            if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
            {
                TempData["Error"] = "Registration not found";
                return RedirectToAction("Registrations");
            }

            ViewBag.ParticipantCurrentAge = registration.User is null
                ? null
                : ParticipantDemographics.CurrentAgeOrNull(registration.User.Birthday, _philippineClock.Today);
            ViewBag.ParticipantGender = registration.User is not null && ParticipantDemographics.IsValidGender(registration.User.Gender)
                ? registration.User.Gender
                : null;

            if (registration.Assessment != null && !RegistrationStatusHelper.IsEventCancelled(registration.Event))
            {
                ViewBag.AlternativeEvents = await GetAlternativeEvents(
                    registration.Event!.Id,
                    DifficultyCalculator.DisplayLabel(registration.Event.Difficulty),
                    registration.Assessment!.Result ?? ""
                );

                var resultSelection = SuitabilityResultSelector.Select(await _context.SuitabilityResults
                    .Include(s => s.ShapValues)
                    .Where(s => s.AssessmentId == registration.Assessment.Id)
                    .ToListAsync());
                if (resultSelection.IsInvalidOrUnsupported)
                {
                    TempData["Error"] = "This registration has an invalid or unsupported prediction record.";
                    return RedirectToAction("Registrations");
                }
                var suitabilityResult = resultSelection.Result;

                if (suitabilityResult != null)
                {
                    ViewBag.HasMlPrediction = true;
                    ViewBag.MlModelScore = suitabilityResult.ModelScore;
                    ViewBag.MlModelVersion = suitabilityResult.ModelVersion;
                    ViewBag.ShapFactors = resultSelection.IsRecognizedV2
                        ? TrailGuardV2Presentation.BuildV2Factors(suitabilityResult.ShapValues)
                        : ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues);
                    ViewBag.IsTrailGuardV2 = resultSelection.IsRecognizedV2;
                }
                else
                {
                    ViewBag.HasMlPrediction = false;
                }
            }






            var receiptResolved = await Storage.ReadDocumentAsync(RegistrationDocumentKind.Receipt, registration.PaymentReceiptUrl);
            ViewBag.ReceiptAvailable = receiptResolved != null;
            ViewBag.ReceiptIsImage = receiptResolved != null && DocumentFileSignature.IsImageType(receiptResolved.Type);

            var clearanceResolved = await Storage.ReadDocumentAsync(RegistrationDocumentKind.Clearance, registration.MedicalClearanceUrl);
            ViewBag.ClearanceAvailable = clearanceResolved != null;
            ViewBag.ClearanceIsImage = clearanceResolved != null && DocumentFileSignature.IsImageType(clearanceResolved.Type);













            ViewBag.CanViewParticipantProfile = false;
            if (registration.User != null)
            {
                var profileAccess = await _profileAccessService.ResolveAsync(currentUser, registration.User.PublicProfileId);
                if (profileAccess.Succeeded)
                {
                    ViewBag.CanViewParticipantProfile = true;
                    ViewBag.ParticipantPublicProfileId = profileAccess.TargetPublicProfileId;
                }
            }

            return View(registration);
        }




        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result)
        {
            var currentIndex = DifficultyCalculator.BucketRank(currentDifficulty);
            if (currentIndex > 2) currentIndex = 1;

            int targetIndex;
            if (result == "Good-Match")
                targetIndex = currentIndex;
            else if (result == "Borderline")
                targetIndex = Math.Max(0, currentIndex - 1);
            else
                targetIndex = Math.Max(0, currentIndex - 2);

            var candidates = await _context.Events
                .Where(e =>
                    e.Id != eventId &&
                    e.Status == "Upcoming" &&
                    e.EventDate >= DateTime.Today)
                .Take(5)
                .ToListAsync();
            return candidates.Where(e => DifficultyCalculator.BucketRank(e.Difficulty) == targetIndex).ToList();
        }

        public class RecommendAlternativeRequest
        {
            public int RegistrationId { get; set; }
            public int[] AlternativeEventIds { get; set; } = Array.Empty<int>();
            public string? Reason { get; set; }
        }






        private const int MaxDecisionReasonLength = 2000;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecommendAlternative([FromBody] RecommendAlternativeRequest request)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                var registration = await FindRegistrationForDecisionAsync(request.RegistrationId);



                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                {
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                }

                if (registration.Status != "Pending")
                {
                    return Json(new { success = false, message = "This registration is no longer pending review." });
                }

                await using var transaction = await BeginDecisionTransactionAsync();
                await AcquireDecisionEventLockAsync(registration.Event.Id);
                _context.ChangeTracker.Clear();
                registration = await FindRegistrationForDecisionAsync(request.RegistrationId);
                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                    return Json(new { success = false, message = "Registration not found" });
                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                if (registration.Status != "Pending")
                    return Json(new { success = false, message = "This registration is no longer pending review." });

                if (request.AlternativeEventIds == null || request.AlternativeEventIds.Length != 1)
                {
                    return Json(new { success = false, message = "Please select exactly one alternative event." });
                }

                if (request.Reason != null && request.Reason.Length > MaxDecisionReasonLength)
                {
                    return Json(new { success = false, message = "Decision reason is too long." });
                }

                var submittedAlternativeEventId = request.AlternativeEventIds[0];






                var candidateEvents = await GetAlternativeEvents(
                    registration.Event.Id,
                    DifficultyCalculator.DisplayLabel(registration.Event.Difficulty),
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
                await transaction.CommitAsync();

                return Json(new { success = true, message = "Alternative event recommended to the participant." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recommend an alternative event for registration {RegistrationId}.", request.RegistrationId);
                return Json(new { success = false, message = "Unable to recommend an alternative right now. Please try again." });
            }
        }

        public class UpdateRegistrationStatusRequest
        {
            public int Id { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? Reason { get; set; }
        }







        private static readonly string[] AllowedDecisionStatuses = { "Accepted", "Rejected" };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRegistrationStatus([FromBody] UpdateRegistrationStatusRequest request)
        {
            try
            {
                var currentUser = await GetCurrentUserForDecisionAsync();
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                var registration = await FindRegistrationForDecisionAsync(request.Id);




                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                {
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                }

                if (registration.Status != "Pending")
                {
                    return Json(new { success = false, message = "This registration is no longer pending review." });
                }

                await using var transaction = await BeginDecisionTransactionAsync();
                await AcquireDecisionEventLockAsync(registration.Event.Id);
                _context.ChangeTracker.Clear();
                registration = await FindRegistrationForDecisionAsync(request.Id);
                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                    return Json(new { success = false, message = "Registration not found" });
                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                if (registration.Status != "Pending")
                    return Json(new { success = false, message = "This registration is no longer pending review." });

                if (!AllowedDecisionStatuses.Contains(request.Status))
                {
                    return Json(new { success = false, message = "Invalid decision." });
                }

                if (request.Reason != null && request.Reason.Length > MaxDecisionReasonLength)
                {
                    return Json(new { success = false, message = "Decision reason is too long." });
                }




                if (request.Status == "Accepted" && registration.Assessment?.Result == "Not Recommended"
                    && string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Json(new { success = false, message = "A decision reason is required to approve a Not Recommended registration." });
                }



                if (request.Status == "Accepted")
                {
                    var approvedAt = DateTime.Now;
                    var deadline = approvedAt.Date.AddDays(3).AddHours(23).AddMinutes(59).AddSeconds(59);

                    var eveOfEventDeadline = registration.Event.EventDate.Date.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                    if (eveOfEventDeadline < deadline)
                    {
                        deadline = eveOfEventDeadline;
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
                await transaction.CommitAsync();

                return Json(new { success = true, message = $"Registration status updated to {registration.Status}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for registration {RegistrationId}.", request.Id);
                return Json(new { success = false, message = "Unable to update this registration right now. Please try again." });
            }
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
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                var registration = await FindRegistrationForDecisionAsync(request.Id);

                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                {
                    return Json(new { success = false, message = "Registration not found" });
                }

                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                {
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                }

                if (registration.Status != "For Payment Verification")
                {
                    return Json(new { success = false, message = "This registration is not awaiting payment verification." });
                }

                await using var transaction = await BeginDecisionTransactionAsync();
                await AcquireDecisionEventLockAsync(registration.Event.Id);
                _context.ChangeTracker.Clear();
                registration = await FindRegistrationForDecisionAsync(request.Id);
                if (registration == null || registration.Event == null || !OwnsEvent(registration.Event, currentUser))
                    return Json(new { success = false, message = "Registration not found" });
                if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                    return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                if (registration.Status != "For Payment Verification")
                    return Json(new { success = false, message = "This registration is not awaiting payment verification." });


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
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = request.Approved
                        ? "Payment verified. Registration accepted."
                        : "Payment rejected. Participant can re-upload their receipt."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify payment for registration {RegistrationId}.", request.Id);
                return Json(new { success = false, message = "Unable to verify payment right now. Please try again." });
            }
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

            return View(eventItem);
        }

        // Query seams keep the status-decision path testable without a database; production behavior remains the same EF queries.
        protected virtual Task<ApplicationUser?> GetCurrentUserForDecisionAsync() => _userManager.GetUserAsync(User);

        // Test seams preserve transaction -> event lock -> authoritative reload ordering.
        protected virtual Task<IDbContextTransaction> BeginDecisionTransactionAsync() => _context.Database.BeginTransactionAsync();

        protected virtual Task AcquireDecisionEventLockAsync(int eventId) => ParticipantEventWorkflowLock.AcquireEventCapacityAsync(_context, eventId);

        protected virtual Task<EventRegistration?> FindRegistrationForDecisionAsync(int registrationId) => _context.EventRegistrations
            .Include(r => r.Event)
            .Include(r => r.Assessment)
            .FirstOrDefaultAsync(r => r.Id == registrationId);














        private static bool OwnsEvent(Event eventItem, ApplicationUser currentUser)
        {
            return eventItem.OrganizerId != null && eventItem.OrganizerId == currentUser.Id;
        }






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







                if (!OwnsEvent(registration.Event, currentUser))
                {
                    return Json(new { success = false, message = "Registration not found." });
                }





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





                if (!FinalLabelService.IsValidCompletion(request.Completed, request.NonCompletionReason) ||
                    !FinalLabelService.IsKnownOutcome(request.DifficultyExperience))
                {
                    return Json(new { success = false, message = "Please select completion, a reason if not completed, and a valid difficulty experience." });
                }

                var userId = registration.UserId;

                await using var transaction = await _context.Database.BeginTransactionAsync();
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"EventRegistrations\" WHERE \"Id\" = {registration.Id} FOR UPDATE");
                var existingAssessment = await _context.PostEventAssessments
                    .FirstOrDefaultAsync(a => a.EventId == registration.EventId && a.UserId == userId);

                if (existingAssessment != null)
                {
                    existingAssessment.Completed = request.Completed;
                    existingAssessment.NonCompletionReason = request.Completed == true ? "NotApplicable" : request.NonCompletionReason;
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
                        Completed = request.Completed,
                        NonCompletionReason = request.Completed == true ? "NotApplicable" : request.NonCompletionReason,
                        DifficultyExperience = request.DifficultyExperience,
                        Notes = request.Notes,
                        CreatedAt = DateTime.Now
                    };
                    _context.PostEventAssessments.Add(assessment);
                }

                await _context.SaveChangesAsync();

                await FinalLabelService.UpsertFinalLabel(_context, registration.Id);
                await transaction.CommitAsync();

                return Json(new { success = true, message = "Assessment saved successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save post-event assessment for registration {RegistrationId}.", request.RegistrationId);
                return Json(new { success = false, message = "Unable to save the assessment right now. Please try again." });
            }
        }


        public class SubmitAssessmentRequest
        {
            public int EventId { get; set; }
            public int RegistrationId { get; set; }
            public bool? Completed { get; set; }
            public string? NonCompletionReason { get; set; }
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
                .FirstOrDefaultAsync(e => e.Id == eventId);







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

            var outcomes = await _context.FinalSuitabilityLabels
                .ToDictionaryAsync(l => l.AssessmentId, l => l);
            var assessmentIds = registrations.Where(r => r.AssessmentId.HasValue).Select(r => r.AssessmentId!.Value).ToList();
            var predictionSelections = (await _context.SuitabilityResults
                .Include(result => result.ShapValues)
                .Where(r => assessmentIds.Contains(r.AssessmentId))
                .ToListAsync())
                .GroupBy(result => result.AssessmentId)
                .ToDictionary(group => group.Key, group => SuitabilityResultSelector.Select(group));

            var results = new List<ComparisonResult>();

            foreach (var reg in registrations)
            {
                var userId = reg.UserId;
                participantFeedbacks.TryGetValue(userId, out var participantFeedback);
                organizerAssessments.TryGetValue(userId, out var organizerAssessment);
                outcomes.TryGetValue(reg.AssessmentId ?? 0, out var outcome);
                predictionSelections.TryGetValue(reg.AssessmentId ?? 0, out var predictionSelection);
                var predictionIsInvalidOrUnsupported = predictionSelection?.IsInvalidOrUnsupported == true;
                var prediction = predictionIsInvalidOrUnsupported ? null : predictionSelection?.Result;
                var selectedLabel = predictionIsInvalidOrUnsupported
                    ? "Invalid / unsupported prediction record"
                    : prediction?.UiLabel ?? reg.Assessment?.Result ?? "Not available";

                results.Add(new ComparisonResult
                {
                    ParticipantName = reg.User != null ? $"{reg.User.FirstName} {reg.User.LastName}" : reg.ParticipantName,
                    PreHikeAssessment = reg.Assessment?.Result ?? "Not available",
                    ParticipantDifficultyExperience = participantFeedback?.DifficultyExperience,
                    ParticipantCompletion = CompletionText(participantFeedback?.Completed, participantFeedback?.NonCompletionReason),
                    OrganizerDifficultyExperience = organizerAssessment?.DifficultyExperience,
                    OrganizerCompletion = CompletionText(organizerAssessment?.Completed, organizerAssessment?.NonCompletionReason),
                    ConservativeDifficultyExperience = outcome?.DifficultyExperience,
                    Completed = outcome?.Completed,
                    NonCompletionReason = outcome?.NonCompletionReason,
                    ModelUiLabel = selectedLabel,
                    ModelScore = prediction?.ModelScore,
                    ModelVersion = prediction?.ModelVersion,
                    UiPolicyVersion = prediction?.UiLabelPolicyVersion,
                    Comparison = predictionIsInvalidOrUnsupported
                        ? "Not available"
                        : ComparisonFor(prediction?.UiLabel ?? reg.Assessment?.Result, outcome?.Completed)
                });
            }

            ViewBag.Event = eventItem;
            return View(results);
        }

        private static string CompletionText(bool? completed, string? reason) => completed switch
        {
            true => "Completed",
            false => $"Did not complete — {reason}",
            _ => "Not submitted"
        };
        private static string ComparisonFor(string? label, bool? completed) => completed is null ? "Pending" : label switch
        {
            "Good Match" or "Good-Match" when completed.Value => "Accurate",
            "Good Match" or "Good-Match" => "Missed risk",
            "Not Recommended" when completed.Value => "Over-cautious",
            "Not Recommended" => "Accurate",
            "Borderline" => "Borderline outcome",
            _ => "Not available"
        };

    }
}
