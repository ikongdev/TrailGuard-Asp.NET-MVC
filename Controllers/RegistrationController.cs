using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class RegistrationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUploadStorage? _storage;
        private IUploadStorage Storage => _storage ?? HttpContext.RequestServices.GetRequiredService<IUploadStorage>();
        private readonly ILogger<RegistrationController> _logger;
        private readonly IPhilippineClock _philippineClock;

        public RegistrationController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, IPhilippineClock? philippineClock = null, IUploadStorage? storage = null, ILogger<RegistrationController>? logger = null)
        {
            _context = context;
            _storage = storage;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RegistrationController>.Instance;
            _philippineClock = philippineClock ?? new PhilippineClock(TimeProvider.System);
        }

        [HttpGet]
        public async Task<IActionResult> Register(int eventId, int assessmentId)
        {
            var eventItem = await FindEventAsync(eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;






            var activeRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId &&
                    (RegistrationStatusHelper.ActiveStatuses.Contains(r.Status) || r.Status == "Alternative Recommended"));

            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            if (!EventJoinabilityHelper.IsJoinable(eventItem))
            {
                TempData["Error"] = "This event is no longer open for registration.";
                return RedirectToAction("Events", "Participant");
            }

            var activeCount = await _context.EventRegistrations
                .CountAsync(r => r.EventId == eventId && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));

            if (activeCount >= eventItem.Capacity)
            {
                TempData["Error"] = "This event is at full capacity.";
                return RedirectToAction("Events", "Participant");
            }

            var assessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found. Please complete the assessment first.";
                return RedirectToAction("Form", "Assessment", new { eventId = eventId });
            }

            var user = await FindUserAsync(userId);
            PopulateReadOnlyDemographics(user);

            var resultSelection = SuitabilityResultSelector.Select(await _context.SuitabilityResults
                .Include(s => s.ShapValues)
                .Where(s => s.AssessmentId == assessmentId)
                .ToListAsync());
            if (resultSelection.IsInvalidOrUnsupported)
            {
                TempData["Error"] = "This assessment's prediction record is invalid or unsupported and cannot be used for registration.";
                return RedirectToAction("Report", "Assessment", new { assessmentId });
            }
            var suitabilityResult = resultSelection.Result;

            var viewModel = new AssessmentResultViewModel
            {
                AssessmentId = assessment.Id,
                EventId = eventItem.Id,
                EventTitle = eventItem.EventTitle,
                EventDifficulty = DifficultyCalculator.DisplayLabel(eventItem.Difficulty),
                Result = assessment.Result ?? "Not Recommended",
                HasMlPrediction = suitabilityResult != null,
                ModelScore = suitabilityResult?.ModelScore ?? 0,
                IsTrailGuardV2 = resultSelection.IsRecognizedV2,
                ScoreName = suitabilityResult?.ScoreName ?? "",
                TrailDuration = suitabilityResult is not null && resultSelection.IsRecognizedV2
                    ? TrailGuardV2Presentation.FormatDuration(suitabilityResult.TypicalDurationHours ?? 0)
                    : string.Empty
            };

            ViewBag.Event = eventItem;
            ViewBag.Assessment = assessment;
            ViewBag.User = user;
            ViewBag.ResultViewModel = viewModel;
            ViewBag.RequiresMedicalClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment);
            ViewBag.RequiresPreparationPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment);
            ViewBag.MedicalClearanceReason = RegistrationRulesHelper.MedicalClearanceReason(assessment);

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(
            int eventId,
            int assessmentId,
            string participantName,
            string email,
            string contactNumber,
            string emergencyContactName,
            string emergencyContactNumber,
            string pickupPoint,
            IFormFile? medicalClearance,
            string? preparationPlan)
        {
            var eventItem = await FindEventAsync(eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Forbid();
            }
            var user = await FindUserAsync(userId);

            var assessment = await FindActiveAssessmentAsync(assessmentId, eventId, userId);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found. Please complete the assessment first.";
                return RedirectToAction("Form", "Assessment", new { eventId = eventId });
            }

            if (!PhilippineMobileNumber.TryNormalize(contactNumber, out var canonicalContactNumber))
            {
                TempData["Error"] = $"Enter a complete contact number: {PhilippineMobileNumber.LocalExample}.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }

            if (!PhilippineMobileNumber.TryNormalize(emergencyContactNumber, out var canonicalEmergencyContactNumber))
            {
                TempData["Error"] = $"Enter a complete emergency contact number: {PhilippineMobileNumber.LocalExample}.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }







            var activeRegistration = await FindExistingRegistrationAsync(eventId, userId);

            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            if (!EventJoinabilityHelper.IsJoinable(eventItem))
            {
                TempData["Error"] = "This event is no longer open for registration.";
                return RedirectToAction("Events", "Participant");
            }

            var activeCount = await CountActiveRegistrationsAsync(eventId);

            if (activeCount >= eventItem.Capacity)
            {
                TempData["Error"] = "This event is at full capacity.";
                return RedirectToAction("Events", "Participant");
            }

            var requiresClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment);
            var requiresPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment);

            if (requiresClearance && (medicalClearance == null || medicalClearance.Length == 0))
            {
                TempData["Error"] = $"A medical clearance document is required. {RegistrationRulesHelper.MedicalClearanceReason(assessment)}";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }

            if (requiresPlan && string.IsNullOrWhiteSpace(preparationPlan))
            {
                TempData["Error"] = "A preparation plan is required because your assessment result is Not Recommended.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }







            var canonicalPickupPoint = PickupScheduleHelper.FindCanonicalMatch(eventItem.PickupPoints, pickupPoint);
            if (canonicalPickupPoint == null)
            {
                TempData["Error"] = "Please select a valid pickup schedule for this event.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }


            VerifiedFileType? verifiedClearanceType = null;
            if (medicalClearance != null && medicalClearance.Length > 0)
            {
                verifiedClearanceType = await DocumentUploadValidator.ValidateAsync(medicalClearance);
                if (verifiedClearanceType == null)
                {
                    TempData["Error"] = "Medical clearance must be a JPG, PNG, WEBP, or PDF file, up to 5 MiB.";
                    return RedirectToAction("Register", new { eventId, assessmentId });
                }
            }

            await using var uploads = new UploadAttempt(Storage, _logger);
            var transaction = await BeginRegistrationTransactionAsync();
            var commitStarted = false;
            try
            {
                await AcquireRegistrationEventCapacityLockAsync(eventId);
                await AcquireRegistrationParticipantEventLockAsync(eventId, userId);
                _context.ChangeTracker.Clear();

                var lockedEvent = await FindEventAsync(eventId);
                var lockedAssessment = await FindActiveAssessmentAsync(assessmentId, eventId, userId);
                if (lockedEvent == null || lockedAssessment == null)
                {
                    TempData["Error"] = "Your assessment is no longer active. Please review the latest assessment before registering.";
                    return RedirectToAction("Form", "Assessment", new { eventId });
                }

                if (await FindExistingRegistrationAsync(eventId, userId) != null)
                {
                    TempData["Success"] = "You are already registered for this event.";
                    return RedirectToAction("Details", "Participant", new { id = eventId });
                }

                if (!EventJoinabilityHelper.IsJoinable(lockedEvent))
                {
                    TempData["Error"] = "This event is no longer open for registration.";
                    return RedirectToAction("Events", "Participant");
                }

                if (await CountActiveRegistrationsAsync(eventId) >= lockedEvent.Capacity)
                {
                    TempData["Error"] = "This event is at full capacity.";
                    return RedirectToAction("Events", "Participant");
                }

                await OnCapacityCheckedForRegistrationAsync(eventId, userId);

                if (RegistrationRulesHelper.RequiresMedicalClearance(lockedAssessment)
                    && (medicalClearance == null || medicalClearance.Length == 0))
                {
                    TempData["Error"] = $"A medical clearance document is required. {RegistrationRulesHelper.MedicalClearanceReason(lockedAssessment)}";
                    return RedirectToAction("Register", new { eventId, assessmentId });
                }

                if (RegistrationRulesHelper.RequiresPreparationPlan(lockedAssessment)
                    && string.IsNullOrWhiteSpace(preparationPlan))
                {
                    TempData["Error"] = "A preparation plan is required because your assessment result is Not Recommended.";
                    return RedirectToAction("Register", new { eventId, assessmentId });
                }

                var lockedPickupPoint = PickupScheduleHelper.FindCanonicalMatch(lockedEvent.PickupPoints, pickupPoint);
                if (lockedPickupPoint == null)
                {
                    TempData["Error"] = "Please select a valid pickup schedule for this event.";
                    return RedirectToAction("Register", new { eventId, assessmentId });
                }

                var cancelledRegistration = await FindCancelledRegistrationAsync(eventId, userId);

            if (cancelledRegistration != null)
            {

                var oldAssessment = await _context.Assessments
                    .FirstOrDefaultAsync(a => a.Id == cancelledRegistration.AssessmentId);

                if (oldAssessment != null)
                {
                    oldAssessment.IsActive = false;
                }
            }

                if (string.IsNullOrEmpty(participantName))
            {
                participantName = user != null ? $"{user.FirstName} {user.LastName}" : "Participant";
            }

                string? medicalClearanceUrl = null;
                if (medicalClearance != null && verifiedClearanceType.HasValue)
            {






                await using var source = medicalClearance.OpenReadStream();
                var bytes = await UploadBytes.ReadAsync(source);
                medicalClearanceUrl = await uploads.UploadAsync(UploadCategory.MedicalClearances, bytes,
                    DocumentFileSignature.SafeExtensionFor(verifiedClearanceType.Value));
            }

                var registration = new EventRegistration
            {
                EventId = eventId,
                UserId = userId,
                ParticipantName = participantName,
                ContactNumber = canonicalContactNumber,
                Email = email,
                PickupPoint = lockedPickupPoint,
                Status = "Pending",
                AssessmentId = assessmentId,
                EmergencyContactName = emergencyContactName,
                EmergencyContactNumber = canonicalEmergencyContactNumber,
                MedicalClearanceUrl = medicalClearanceUrl,
                PreparationPlan = preparationPlan,
                RegisteredAt = DateTime.Now
            };

                _context.EventRegistrations.Add(registration);
                uploads.PersistenceStarted();
                await _context.SaveChangesAsync();
                commitStarted = true;
                await transaction.CommitAsync();
                uploads.Committed();

                TempData["Success"] = "Registration submitted successfully! Your registration is pending approval by the organizer.";
                return RedirectToAction("MyRegistrations");
            }
            catch (DbUpdateException ex) when (ParticipantEventWorkflowLock.IsUniqueConstraintConflict(ex))
            {
                await UploadPersistence.ConfirmRollbackAsync(transaction, uploads, commitStarted);
                TempData["Error"] = "Your registration changed while it was being submitted. Please review the latest status and try again.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }
            catch (Exception ex)
            {
                await UploadPersistence.ConfirmRollbackAsync(transaction, uploads, commitStarted);
                _logger.LogWarning("Registration upload/save failed ({ErrorType}).", ex.GetType().Name);
                TempData["Error"] = "Unable to submit your registration. Please review its status before trying again.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }
            finally
            {
                await uploads.DisposeTransactionAsync(transaction);
            }
        }

        private void PopulateReadOnlyDemographics(ApplicationUser? user)
        {
            ViewBag.RegistrationCurrentAge = ParticipantDemographics.CurrentAgeOrNull(user?.Birthday, _philippineClock.Today);
            ViewBag.RegistrationGender = user is not null && ParticipantDemographics.IsValidGender(user.Gender) ? user.Gender : null;
        }

        // Query seams keep the controller decision path testable without a database; production behavior remains the same EF queries.
        protected virtual Task<Event?> FindEventAsync(int eventId) => _context.Events
            .FirstOrDefaultAsync(e => e.Id == eventId);

        protected virtual Task<ApplicationUser?> FindUserAsync(string? userId) => _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        protected virtual Task<Assessment?> FindActiveAssessmentAsync(int assessmentId, int eventId, string? userId) => _context.Assessments
            .FirstOrDefaultAsync(a => a.Id == assessmentId && a.EventId == eventId && a.UserId == userId && a.IsActive == true);

        protected virtual Task<EventRegistration?> FindExistingRegistrationAsync(int eventId, string? userId) => _context.EventRegistrations
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId &&
                (RegistrationStatusHelper.ActiveStatuses.Contains(r.Status) || r.Status == "Alternative Recommended"));

        protected virtual Task<EventRegistration?> FindCancelledRegistrationAsync(int eventId, string? userId) => _context.EventRegistrations
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && r.Status == "Cancelled");

        protected virtual Task<int> CountActiveRegistrationsAsync(int eventId) => _context.EventRegistrations
            .CountAsync(r => r.EventId == eventId && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));

        protected virtual Task<IDbContextTransaction> BeginRegistrationTransactionAsync() =>
            _context.Database.BeginTransactionAsync();

        protected virtual Task AcquireRegistrationEventCapacityLockAsync(int eventId) =>
            ParticipantEventWorkflowLock.AcquireEventCapacityAsync(_context, eventId);

        protected virtual Task AcquireRegistrationParticipantEventLockAsync(int eventId, string userId) =>
            ParticipantEventWorkflowLock.AcquireAsync(_context, eventId, userId);

        // Test seam: production is a no-op. It runs only after the event-wide lock and
        // authoritative capacity check, before this controller creates a registration.
        [NonAction]
        protected virtual Task OnCapacityCheckedForRegistrationAsync(int eventId, string userId) => Task.CompletedTask;

        [HttpGet]
        public async Task<IActionResult> MyRegistrations()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.Assessment)
                .Include(r => r.AlternativeEvent)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RegisteredAt)
                .ToListAsync();





            var organizerNames = registrations
                .Where(r => r.Status == "Alternative Recommended" && !string.IsNullOrEmpty(r.Event?.OrganizedBy))
                .Select(r => r.Event!.OrganizedBy!)
                .Distinct()
                .ToList();

            var organizersByName = new Dictionary<string, ApplicationUser>();
            foreach (var name in organizerNames)
            {
                var organizer = await _context.Users.FirstOrDefaultAsync(u =>
                    (u.FirstName + " " + u.LastName) == name ||
                    (u.FirstName + " " + u.MiddleName + " " + u.LastName) == name ||
                    u.Email == name ||
                    u.Id == name);

                if (organizer != null)
                {
                    organizersByName[name] = organizer;
                }
            }

            ViewBag.AlternativeOrganizers = organizersByName;

            return View(registrations);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRegistration([FromBody] CancelRegistrationRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var candidate = await _context.EventRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.Id && r.UserId == userId);
            if (candidate == null)
                return Json(new { success = false, message = "Registration not found" });

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await ParticipantEventWorkflowLock.AcquireEventCapacityAsync(_context, candidate.EventId);
            _context.ChangeTracker.Clear();
            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == request.Id && r.UserId == userId);
            if (registration == null)
                return Json(new { success = false, message = "Registration not found" });
            if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
            if (registration.Status != "Pending" && registration.Status != "Awaiting Payment")
                return Json(new { success = false, message = "This registration can no longer be cancelled here. Please contact the organizer directly." });

            registration.Status = "Cancelled";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Json(new { success = true, message = "Registration cancelled successfully." });
        }

        public class CancelRegistrationRequest
        {
            public int Id { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePaymentReceipt(int id, IFormFile? paymentReceipt)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var candidate = await _context.EventRegistrations.AsNoTracking()
                .Include(r => r.Event)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (candidate == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (RegistrationStatusHelper.IsEventCancelled(candidate.Event))
            {
                return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
            }

            if (candidate.Status != "Awaiting Payment")
            {
                return Json(new { success = false, message = "Payment receipt can only be uploaded while your registration is awaiting payment." });
            }

            if (paymentReceipt != null && paymentReceipt.Length > 0)
            {




                var verifiedType = await DocumentUploadValidator.ValidateAsync(paymentReceipt);
                if (verifiedType == null)
                {
                    return Json(new { success = false, message = "Payment receipt must be a JPG, PNG, WEBP, or PDF file, up to 5 MiB." });
                }

                await using var uploads = new UploadAttempt(Storage, _logger);
                var transaction = await _context.Database.BeginTransactionAsync();
                var commitStarted = false;
                try
                {
                    await ParticipantEventWorkflowLock.AcquireEventCapacityAsync(_context, candidate.EventId);
                    _context.ChangeTracker.Clear();
                    var registration = await _context.EventRegistrations
                        .Include(r => r.Event)
                        .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
                    if (registration == null)
                        return Json(new { success = false, message = "Registration not found" });
                    if (RegistrationStatusHelper.IsEventCancelled(registration.Event))
                        return Json(new { success = false, message = "This event has been cancelled. Registration processing is closed." });
                    if (registration.Status != "Awaiting Payment")
                        return Json(new { success = false, message = "Payment receipt can only be uploaded while your registration is awaiting payment." });

                    var previousReference = registration.PaymentReceiptUrl;
                    await using var source = paymentReceipt.OpenReadStream();
                    var bytes = await UploadBytes.ReadAsync(source);
                    registration.PaymentReceiptUrl = await uploads.UploadAsync(UploadCategory.Receipts, bytes,
                        DocumentFileSignature.SafeExtensionFor(verifiedType.Value));
                    registration.PaymentReceiptUploadedAt = DateTime.Now;
                    registration.Status = "For Payment Verification";
                    uploads.PersistenceStarted();
                    await _context.SaveChangesAsync();
                    commitStarted = true;
                    await transaction.CommitAsync();
                    uploads.Committed();
                    await uploads.DeleteReplacedAsync(UploadCategory.Receipts, previousReference);
                    return Json(new { success = true, message = "Payment receipt uploaded. Waiting for organizer verification." });
                }
                catch (Exception ex)
                {
                    await UploadPersistence.ConfirmRollbackAsync(transaction, uploads, commitStarted);
                    _logger.LogWarning("Receipt upload/save failed ({ErrorType}).", ex.GetType().Name);
                    return Json(new { success = false, message = "Unable to save the receipt. Please review the registration status before trying again." });
                }
                finally
                {
                    await uploads.DisposeTransactionAsync(transaction);
                }
            }

            return Json(new { success = false, message = "No file uploaded." });
        }

        [HttpGet]
        public async Task<IActionResult> GetRegistrationDetails(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.Assessment)
                .Include(r => r.AlternativeEvent)
                .FirstOrDefaultAsync(r => r.Id == id);



            if (registration == null || registration.UserId != userId)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            SuitabilityResult? suitabilityResult = null;
            if (registration.Assessment != null)
            {
                var resultSelection = SuitabilityResultSelector.Select(await _context.SuitabilityResults
                    .Include(s => s.ShapValues)
                    .Where(s => s.AssessmentId == registration.Assessment.Id)
                    .ToListAsync());
                if (resultSelection.IsInvalidOrUnsupported)
                {
                    return Json(new { success = false, message = "Assessment prediction record is invalid or unsupported." });
                }
                suitabilityResult = resultSelection.Result;
            }

            var shapFactors = suitabilityResult != null
                ? suitabilityResult.ModelVersion == TrailGuardV2ResponseValidator.ExpectedModelVersion
                    ? TrailGuardV2Presentation.BuildV2Factors(suitabilityResult.ShapValues)
                    : ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues)
                : new List<ShapDisplayItem>();

            return Json(new
            {
                success = true,
                registration = new
                {
                    id = registration.Id,
                    eventId = registration.EventId,
                    eventTitle = registration.Event?.EventTitle,
                    eventDate = registration.Event?.EventDate.ToString("MMM dd, yyyy"),
                    eventTime = registration.Event?.FormattedEventTime,
                    eventLocation = registration.Event?.Location,
                    eventDifficulty = DifficultyCalculator.DisplayLabel(registration.Event?.Difficulty),
                    eventDuration = registration.Event?.EstimatedDuration,
                    eventStatus = registration.Event?.Status,
                    eventCancelled = RegistrationStatusHelper.IsEventCancelled(registration.Event),
                    cancellationReason = registration.Event?.CancellationReason,
                    cancelledAt = registration.Event?.CancelledAt?.ToString("MMM dd, yyyy h:mm tt"),


                    trailName = registration.Event?.TrailNameSnapshot,
                    trailDistance = registration.Event?.TrailDistanceKmSnapshot,
                    trailElevation = registration.Event?.TrailElevationGainMetersSnapshot,
                    trailTerrain = registration.Event?.TrailTerrainSnapshot,
                    participantName = registration.ParticipantName,
                    contactNumber = registration.ContactNumber,
                    email = registration.Email,
                    pickupPoint = registration.PickupPoint,
                    isPaid = registration.IsPaid,
                    emergencyContactName = registration.EmergencyContactName,
                    emergencyContactNumber = registration.EmergencyContactNumber,
                    assessmentResult = registration.Assessment?.Result,
                    hasMlPrediction = suitabilityResult != null,
                    modelScore = suitabilityResult?.ModelScore,
                    modelScoreDisplay = suitabilityResult is null
                        ? null
                        : TrailGuardV2Presentation.FormatModelScore(suitabilityResult.ModelScore),
                    shapFactors = shapFactors.Select(f => new
                    {
                        friendlyName = f.FriendlyName,
                        category = f.Category,
                        isPositive = f.IsPositive,
                        direction = f.Direction,
                        originalInputValue = f.OriginalInputValue,
                        barWidth = f.BarWidth
                    }),
                    status = registration.Status,
                    registeredAt = registration.RegisteredAt.ToString("MMM dd, yyyy hh:mm tt"),
                    alternativeEventId = registration.AlternativeEventId,
                    alternativeEventTitle = registration.AlternativeEvent?.EventTitle,
                    alternativeEventDate = registration.AlternativeEvent?.EventDate.ToString("MMM dd, yyyy"),
                    alternativeEventDifficulty = DifficultyCalculator.DisplayLabel(registration.AlternativeEvent?.Difficulty)
                }
            });
        }
    }
}
