using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

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
            
            // ✅ I-check kung may ACTIVE registration
            var activeRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && (r.Status == "Pending" || r.Status == "Accepted"));
            
            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            // ✅ I-check kung may ACTIVE assessment
            var assessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found. Please complete the assessment first.";
                return RedirectToAction("Form", "Assessment", new { eventId = eventId });
            }

            if (assessment.Result != "Good-Match" && assessment.Result != "Borderline")
            {
                TempData["Error"] = "You are not recommended for this event. Please check your assessment result.";
                return RedirectToAction("Report", "Assessment", new { assessmentId = assessment.Id });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

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
                AlternativeEvents = new List<Event>()
            };

            ViewBag.Event = eventItem;
            ViewBag.Assessment = assessment;
            ViewBag.User = user;
            ViewBag.ResultViewModel = viewModel;
            
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(
            int eventId, 
            int assessmentId,
            string participantName,
            string email,
            string contactNumber,
            string emergencyContactName,
            string emergencyContactNumber,
            string pickupPoint,
            IFormFile? paymentReceipt)
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

            // ✅ I-validate na active ang assessment
            var assessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.EventId == eventId && a.UserId == userId && a.IsActive == true);
            
            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found. Please complete the assessment first.";
                return RedirectToAction("Form", "Assessment", new { eventId = eventId });
            }

            // ✅ I-check kung may active registration
            var activeRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && (r.Status == "Pending" || r.Status == "Accepted"));
            
            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
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

            string? receiptUrl = null;
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

                receiptUrl = $"/uploads/receipts/{fileName}";
            }

            var registration = new EventRegistration
            {
                EventId = eventId,
                UserId = userId ?? "",
                ParticipantName = participantName,
                PickupPoint = pickupPoint,
                IsPaid = !string.IsNullOrEmpty(receiptUrl),
                Status = "Pending",
                AssessmentId = assessmentId,
                EmergencyContactName = emergencyContactName,
                EmergencyContactNumber = emergencyContactNumber,
                PaymentReceiptUrl = receiptUrl,
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
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var registrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Where(r => r.UserId == userId && r.Status != "Rejected")
                .OrderByDescending(r => r.RegisteredAt)
                .ToListAsync();

            return View(registrations);
        }

        [HttpPost]
        public async Task<IActionResult> CancelRegistration([FromBody] CancelRegistrationRequest request)
        {
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == request.Id);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
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
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
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
                registration.IsPaid = true;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Payment receipt updated successfully." });
            }

            return Json(new { success = false, message = "No file uploaded." });
        }

        [HttpGet]
        public async Task<IActionResult> GetRegistrationDetails(int id)
        {
            var registration = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e!.Trail)
                .Include(r => r.Assessment)
                .Include(r => r.AlternativeEvent)
                .ThenInclude(e => e!.Trail)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (registration == null)
            {
                return Json(new { success = false, message = "Registration not found" });
            }

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
                    pickupPoint = registration.PickupPoint,
                    isPaid = registration.IsPaid,
                    paymentReceiptUrl = registration.PaymentReceiptUrl,
                    emergencyContactName = registration.EmergencyContactName,
                    emergencyContactNumber = registration.EmergencyContactNumber,
                    assessmentResult = registration.Assessment?.Result,
                    assessmentScore = registration.Assessment?.TotalScore,
                    fitnessScore = registration.Assessment?.FitnessScore ?? 0,
                    experienceScore = registration.Assessment?.ExperienceScore ?? 0,
                    healthScore = registration.Assessment?.HealthScore ?? 0,
                    gearScore = registration.Assessment?.GearScore ?? 0,
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