using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class RegistrationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public RegistrationController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public async Task<IActionResult> Register(int eventId, int assessmentId)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
                .FirstOrDefaultAsync(e => e.Id == eventId);
            
            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // Rejected is deliberately left out of this block: it's a point-in-time outcome
            // (missing document, event was full, etc.) and those conditions can change, so a
            // rejected participant may try again. Alternative Recommended is a judgement call
            // about fit for this trail, not a fixable problem, so it blocks re-registration
            // here the same way an active registration would.
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            var suitabilityResult = await _context.SuitabilityResults
                .FirstOrDefaultAsync(s => s.AssessmentId == assessmentId);

            var viewModel = new AssessmentResultViewModel
            {
                AssessmentId = assessment.Id,
                EventId = eventItem.Id,
                EventTitle = eventItem.EventTitle,
                EventDifficulty = eventItem.Difficulty,
                Result = assessment.Result ?? "Not Recommended",
                TotalScore = assessment.TotalScore ?? 0,
                MaxScore = 44,
                FitnessScore = assessment.FitnessScore ?? 0,
                FitnessMax = 12,
                ExperienceScore = assessment.ExperienceScore ?? 0,
                ExperienceMax = 12,
                HealthScore = assessment.HealthScore ?? 0,
                HealthMax = 12,
                GearScore = assessment.GearScore ?? 0,
                GearMax = 8,
                RiskFlags = new List<string>(),
                Recommendations = new List<string>(),
                AlternativeEvents = new List<Event>(),
                HasMlPrediction = suitabilityResult != null,
                ConfidenceScore = suitabilityResult?.ConfidenceScore ?? 0
            };

            ViewBag.Event = eventItem;
            ViewBag.Assessment = assessment;
            ViewBag.User = user;
            ViewBag.ResultViewModel = viewModel;
            ViewBag.RequiresMedicalClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment);
            ViewBag.RequiresPreparationPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment);
            ViewBag.HasMedicalCondition = RegistrationRulesHelper.HasAnyMedicalCondition(assessment.MedicalConditions);
            ViewBag.GateReason = suitabilityResult?.GateReason;

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
            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            var assessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found. Please complete the assessment first.";
                return RedirectToAction("Form", "Assessment", new { eventId = eventId });
            }

            // ✅ I-check kung may active registration
            // Rejected is deliberately left out of this block: it's a point-in-time outcome
            // (missing document, event was full, etc.) and those conditions can change, so a
            // rejected participant may try again. Alternative Recommended is a judgement call
            // about fit for this trail, not a fixable problem, so it blocks re-registration
            // here the same way an active registration would.
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

            var requiresClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment);
            var requiresPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment);

            if (requiresClearance && (medicalClearance == null || medicalClearance.Length == 0))
            {
                TempData["Error"] = "A medical clearance document is required based on your assessment.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }

            if (requiresPlan && string.IsNullOrWhiteSpace(preparationPlan))
            {
                TempData["Error"] = "A preparation plan is required because your assessment result is Not Recommended.";
                return RedirectToAction("Register", new { eventId, assessmentId });
            }

            // ✅ I-check kung may cancelled registration, i-soft delete ang assessment nito
            var cancelledRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && r.Status == "Cancelled");

            if (cancelledRegistration != null)
            {
                // I-soft delete ang assessment ng cancelled registration
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
            if (medicalClearance != null && medicalClearance.Length > 0)
            {
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "medical-clearances");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(medicalClearance.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await medicalClearance.CopyToAsync(stream);
                }

                medicalClearanceUrl = $"/uploads/medical-clearances/{fileName}";
            }

            var registration = new EventRegistration
            {
                EventId = eventId,
                UserId = userId ?? "",
                ParticipantName = participantName,
                ContactNumber = contactNumber,
                Email = email,
                PickupPoint = pickupPoint,
                Status = "Pending",
                AssessmentId = assessmentId,
                EmergencyContactName = emergencyContactName,
                EmergencyContactNumber = emergencyContactNumber,
                MedicalClearanceUrl = medicalClearanceUrl,
                PreparationPlan = preparationPlan,
                RegisteredAt = DateTime.Now
            };

            _context.EventRegistrations.Add(registration);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Registration submitted successfully! Your registration is pending approval by the organizer.";
            return RedirectToAction("MyRegistrations");
        }

        [HttpGet]
        public async Task<IActionResult> MyRegistrations()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Include(r => r.AlternativeEvent)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RegisteredAt)
                .ToListAsync();

            // Alternative Recommended is a closed door back to the original event - the
            // participant is pointed at the organizer instead, so this page needs the
            // organizer's contact details. Resolved only for that status; every other
            // status has no need for it.
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
        public async Task<IActionResult> CancelRegistration([FromBody] CancelRegistrationRequest request)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            // Same message whether the ID doesn't exist or just isn't this user's —
            // no reason to confirm someone else's registration ID is valid.
            if (registration == null || registration.UserId != userId)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (registration.Status != "Pending" && registration.Status != "Awaiting Payment")
            {
                return Json(new { success = false, message = "This registration can no longer be cancelled here. Please contact the organizer directly." });
            }

            registration.Status = "Cancelled";
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Registration cancelled successfully." });
        }

        public class CancelRegistrationRequest
        {
            public int Id { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePaymentReceipt(int id, IFormFile? paymentReceipt)
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == id);

            // Same message whether the ID doesn't exist or just isn't this user's —
            // no reason to confirm someone else's registration ID is valid.
            if (registration == null || registration.UserId != userId)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            if (registration.Status != "Awaiting Payment")
            {
                return Json(new { success = false, message = "Payment receipt can only be uploaded while your registration is awaiting payment." });
            }

            if (paymentReceipt != null && paymentReceipt.Length > 0)
            {
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "receipts");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(paymentReceipt.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await paymentReceipt.CopyToAsync(stream);
                }

                registration.PaymentReceiptUrl = $"/uploads/receipts/{fileName}";
                registration.PaymentReceiptUploadedAt = DateTime.Now;
                registration.Status = "For Payment Verification";
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Payment receipt uploaded. Waiting for organizer verification." });
            }

            return Json(new { success = false, message = "No file uploaded." });
        }

        [HttpGet]
        public async Task<IActionResult> GetRegistrationDetails(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Include(r => r.AlternativeEvent)
                .ThenInclude(e => e!.Trail)
                .FirstOrDefaultAsync(r => r.Id == id);

            // Same message whether the ID doesn't exist or just isn't this user's —
            // no reason to confirm someone else's registration ID is valid.
            if (registration == null || registration.UserId != userId)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

            SuitabilityResult? suitabilityResult = null;
            if (registration.Assessment != null)
            {
                suitabilityResult = await _context.SuitabilityResults
                    .Include(s => s.ShapValues)
                    .FirstOrDefaultAsync(s => s.AssessmentId == registration.Assessment.Id);
            }

            var shapFactors = suitabilityResult != null
                ? ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues, 3)
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
                    eventDifficulty = registration.Event?.Difficulty,
                    eventDuration = registration.Event?.EstimatedDuration,
                    trailName = registration.Event?.Trail?.Name,
                    trailDistance = registration.Event?.Trail?.DistanceKm,
                    trailElevation = registration.Event?.Trail?.ElevationGainMeters,
                    trailTerrain = registration.Event?.Trail?.Terrain,
                    participantName = registration.ParticipantName,
                    contactNumber = registration.ContactNumber,
                    email = registration.Email,
                    pickupPoint = registration.PickupPoint,
                    isPaid = registration.IsPaid,
                    paymentReceiptUrl = registration.PaymentReceiptUrl,
                    emergencyContactName = registration.EmergencyContactName,
                    emergencyContactNumber = registration.EmergencyContactNumber,
                    assessmentResult = registration.Assessment?.Result,
                    assessmentScore = registration.Assessment?.TotalScore,
                    hasMlPrediction = suitabilityResult != null,
                    confidenceScore = suitabilityResult?.ConfidenceScore,
                    shapFactors = shapFactors.Select(f => new
                    {
                        friendlyName = f.FriendlyName,
                        isPositive = f.IsPositive,
                        barWidth = f.BarWidth
                    }),
                    status = registration.Status,
                    registeredAt = registration.RegisteredAt.ToString("MMM dd, yyyy hh:mm tt"),
                    alternativeEventId = registration.AlternativeEventId,
                    alternativeEventTitle = registration.AlternativeEvent?.EventTitle,
                    alternativeEventDate = registration.AlternativeEvent?.EventDate.ToString("MMM dd, yyyy"),
                    alternativeEventDifficulty = registration.AlternativeEvent?.Difficulty
                }
            });
        }
    }
}