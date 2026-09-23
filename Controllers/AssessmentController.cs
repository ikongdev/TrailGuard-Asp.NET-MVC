using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class AssessmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SuitabilityApiClient _suitabilityApi;
        private readonly ILogger<AssessmentController> _logger;

        public AssessmentController(ApplicationDbContext context, SuitabilityApiClient suitabilityApi, ILogger<AssessmentController> logger)
        {
            _context = context;
            _suitabilityApi = suitabilityApi;
            _logger = logger;
        }




        private async Task<Event?> PopulateAssessmentFormViewBagAsync(int eventId, string? userId)
        {


            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                return null;
            }

            var isRetake = await _context.Assessments
                .AnyAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            ViewBag.Event = eventItem;
            ViewBag.RetakeMode = isRetake;

            return eventItem;
        }

        [HttpGet]
        public async Task<IActionResult> Form(int eventId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var eventItem = await PopulateAssessmentFormViewBagAsync(eventId, userId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            TempData.Remove("Error");

            var activeRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));

            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            var existingAssessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (existingAssessment != null)
            {
                existingAssessment.IsActive = false;
                await _context.SaveChangesAsync();
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Form(
            int eventId,
            int? age,
            double? heightCm,
            double? weightKg,
            string? medicalConditions,
            string? exerciseFrequency,
            string? exerciseType,
            string? cardioEndurance,
            string? exerciseConsistency,
            string? mountainsClimbed,
            string? recencyOfHike,
            string? trailDifficultyCompleted,
            string[]? gearItems,
            bool consentGiven)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;



            var eventItem = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            if (!consentGiven)
            {
                TempData["Error"] = "You must give consent to proceed.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            if (string.IsNullOrWhiteSpace(medicalConditions))
            {
                TempData["Error"] = "Please answer the medical conditions question.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            if (gearItems == null || gearItems.Length == 0)
            {
                TempData["Error"] = "Please select at least one gear item, or \"None of the above\".";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            if (!string.IsNullOrEmpty(medicalConditions))
            {
                var medicalList = medicalConditions.Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();
                medicalConditions = string.Join(",", medicalList);
            }

            var gearItemsString = "";
            if (gearItems != null && gearItems.Length > 0)
            {
                gearItemsString = string.Join(",", gearItems.Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)));
            }

            SuitabilityPredictionRequest mlRequest;
            try
            {
                mlRequest = BuildMlRequest(
                    heightCm, weightKg, medicalConditions,
                    exerciseFrequency, cardioEndurance, exerciseConsistency,
                    mountainsClimbed, recencyOfHike, trailDifficultyCompleted,
                    gearItemsString, eventItem
                );
            }
            catch (InvalidOperationException ex)
            {





                _logger.LogError(ex, "Assessment submission for Event {EventId} (Trail snapshot '{TrailNameSnapshot}') could not build an ML request: invalid TrailClassSnapshot.", eventItem.Id, eventItem.TrailNameSnapshot);
                TempData["Error"] = "This event's trail details could not be validated. Please contact the organizer.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }



            var acsmClearanceRequired = AcsmClearanceService.RequiresMedicalClearance(
                hasSignsSymptoms: HasCondition(medicalConditions, "Vertigo")
                    || HasCondition(medicalConditions, "Chest pain")
                    || HasCondition(medicalConditions, "Shortness of breath"),
                hasCvd: HasCondition(medicalConditions, "Hypertension"));

            var predictionCall = await _suitabilityApi.PredictAsync(mlRequest);






            if (predictionCall.IsValidationFailure)
            {
                TempData["Error"] = "One of your assessment answers could not be recognized. Please review the form and try again.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            var mlResponse = predictionCall.Prediction;
            if (mlResponse == null)
            {
                TempData["Error"] = "The assessment service is temporarily unavailable. Please try again shortly.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            try
            {
                ShapHelper.ValidateResponseFeatures(mlResponse.ShapAll);
                ShapHelper.ValidateDisplayBreakdown(mlResponse.ShapBreakdown, mlResponse.ShapAll);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _logger.LogError(ex, "ML response for Event {EventId} did not satisfy the v3 SHAP contract.", eventId);
                TempData["Error"] = "The assessment service returned an invalid result. Please try again shortly.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            var result = NormalizeLabel(mlResponse.SuitabilityLabel);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var oldAssessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (oldAssessment != null)
            {
                oldAssessment.IsActive = false;
            }

            var assessment = new Assessment
            {
                EventId = eventId,
                UserId = userId ?? "",
                Age = age,
                HeightCm = heightCm,
                WeightKg = weightKg,
                MedicalConditions = medicalConditions,
                MedicalClearanceRequired = acsmClearanceRequired,
                ExerciseFrequency = exerciseFrequency,
                ExerciseType = exerciseType,
                CardioEndurance = cardioEndurance,
                ExerciseConsistency = exerciseConsistency,
                MountainsClimbed = mountainsClimbed,
                RecencyOfHike = recencyOfHike,
                TrailDifficultyCompleted = trailDifficultyCompleted,
                GearItems = gearItemsString,
                ConsentGiven = consentGiven,
                Result = result,
                IsActive = true,
                SubmittedAt = DateTime.Now
            };

            _context.Assessments.Add(assessment);
            await _context.SaveChangesAsync();

            var suitabilityResult = new SuitabilityResult
            {
                AssessmentId = assessment.Id,
                PredictedLabel = mlResponse.SuitabilityLabel,
                CompletionProbability = mlResponse.CompletionProbability,
                ModelVersion = mlResponse.ModelVersion,
                PredictedAt = DateTime.Now
            };

            _context.SuitabilityResults.Add(suitabilityResult);
            await _context.SaveChangesAsync();

            var displayByFeature = mlResponse.ShapBreakdown
                .Select((shap, index) => new { shap, index })
                .ToDictionary(item => item.shap.Feature);

            foreach (var shap in mlResponse.ShapAll)
            {
                displayByFeature.TryGetValue(shap.Feature, out var display);
                _context.ShapValues.Add(new ShapValue
                {
                    SuitabilityResultId = suitabilityResult.Id,
                    FeatureName = shap.Feature,
                    Category = shap.Category,
                    ImpactValue = shap.ShapValue,
                    RawValue = shap.RawValue.ToString(),
                    DisplayOrder = display?.index,
                    DisplaySharePct = display?.shap.SharePct,
                    DisplayFriendlyName = display?.shap.FriendlyName
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return RedirectToAction("Report", new { assessmentId = assessment.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Report(int assessmentId)
        {

            var assessment = await _context.Assessments
                .Include(a => a.Event)
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.IsActive == true);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found or has been replaced.";
                return RedirectToAction("Events", "Participant");
            }

            var eventItem = assessment.Event;

            var difficulty = eventItem?.Difficulty ?? "Moderate";

            var suitabilityResult = await _context.SuitabilityResults
                .Include(s => s.ShapValues)
                .FirstOrDefaultAsync(s => s.AssessmentId == assessmentId);

            var shapFactors = suitabilityResult != null
                ? ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues)
                : new List<ShapDisplayItem>();

            var recommendations = suitabilityResult != null
                ? ShapHelper.BuildRecommendations(suitabilityResult.ShapValues)
                : new List<string>();

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var alternativeEvents = await GetAlternativeEvents(
                eventItem?.Id ?? 0,
                difficulty,
                assessment.Result ?? "",
                userId
            );

            var viewModel = new AssessmentReportViewModel
            {
                AssessmentId = assessment.Id,
                EventId = assessment.EventId,
                EventTitle = eventItem?.EventTitle ?? "Event Not Found",
                EventDifficulty = difficulty,
                Result = assessment.Result ?? "Not Recommended",
                Recommendations = recommendations,
                AlternativeEvents = alternativeEvents,
                Answers = new Dictionary<string, string>
                {
                    { "Age", assessment.Age?.ToString() ?? "N/A" },
                    { "Height/Weight", $"{assessment.HeightCm?.ToString() ?? "N/A"}cm / {assessment.WeightKg?.ToString() ?? "N/A"}kg" },
                    { "Medical Conditions", assessment.MedicalConditions ?? "None" },
                    { "Exercise Frequency", assessment.ExerciseFrequency ?? "N/A" },
                    { "Exercise Type", assessment.ExerciseType ?? "N/A" },
                    { "Cardio Endurance", assessment.CardioEndurance ?? "N/A" },
                    { "Mountains Climbed", assessment.MountainsClimbed ?? "N/A" },
                    { "Recency of Hike", assessment.RecencyOfHike ?? "N/A" },
                    { "Trail Difficulty Completed", assessment.TrailDifficultyCompleted ?? "N/A" },
                    { "Gear Items", assessment.GearItems ?? "None" }
                },
                HasMlPrediction = suitabilityResult != null,
                CompletionProbability = suitabilityResult?.CompletionProbability ?? 0,
                ModelVersion = suitabilityResult?.ModelVersion ?? "",
                ShapFactors = shapFactors,
                AcsmMedicalClearanceRequired = assessment.MedicalClearanceRequired,
                RequiresMedicalClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment),
                RequiresPreparationPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment)
            };

            ViewBag.Assessment = assessment;
            return View(viewModel);
        }


        private string NormalizeLabel(string mlLabel) => mlLabel switch
        {
            "Good Match" => "Good-Match",
            "Borderline" => "Borderline",
            "Not Recommended" => "Not Recommended",
            _ => "Not Recommended"
        };
        private bool HasCondition(string? medicalConditions, string keyword)
        {
            if (string.IsNullOrEmpty(medicalConditions)) return false;
            return medicalConditions.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountGearItems(string? gearItems)
        {
            if (string.IsNullOrEmpty(gearItems)) return 0;

            return gearItems.Split(',')
                .Select(g => g.Trim())
                .Count(g => !string.IsNullOrEmpty(g) && !g.Equals("None of the above", StringComparison.OrdinalIgnoreCase));
        }







        private SuitabilityPredictionRequest BuildMlRequest(
            double? heightCm, double? weightKg,
            string? medicalConditions, string? exerciseFrequency, string? cardioEndurance,
            string? exerciseConsistency, string? mountainsClimbed, string? recencyOfHike,
            string? trailDifficultyCompleted, string? gearItems, Event eventItem)
        {
            var h = heightCm ?? 165;
            var w = weightKg ?? 60;
            var heightM = h / 100;
            var bmi = heightM > 0 ? w / (heightM * heightM) : 22.0;








            if (eventItem.TrailClassSnapshot < 1 || eventItem.TrailClassSnapshot > 4)
            {
                throw new InvalidOperationException(
                    $"Event {eventItem.Id} (Trail snapshot '{eventItem.TrailNameSnapshot}') has an invalid TrailClassSnapshot ({eventItem.TrailClassSnapshot}); expected 1-4.");
            }

            return new SuitabilityPredictionRequest
            {
                Bmi = Math.Round(bmi, 2),
                ExerciseFrequency = exerciseFrequency ?? string.Empty,
                CardioDuration = cardioEndurance ?? string.Empty,
                ExerciseConsistency = exerciseConsistency ?? string.Empty,
                HikingExperience = mountainsClimbed ?? string.Empty,
                LastHikeRecency = recencyOfHike ?? string.Empty,
                HardestTrailCompleted = trailDifficultyCompleted ?? string.Empty,
                GearScore = CountGearItems(gearItems),
                HasAsthma = HasCondition(medicalConditions, "Asthma") ? 1 : 0,
                HasCvd = HasCondition(medicalConditions, "Hypertension") ? 1 : 0,
                HasJointKneeInjury = HasCondition(medicalConditions, "Joint or knee") ? 1 : 0,
                HasSignsSymptoms = (HasCondition(medicalConditions, "Vertigo")
                                || HasCondition(medicalConditions, "Chest pain")
                                || HasCondition(medicalConditions, "Shortness of breath")) ? 1 : 0,
                DistanceKm = eventItem.TrailDistanceKmSnapshot,
                ElevationGainM = eventItem.TrailElevationGainMetersSnapshot,
                TrailClass = eventItem.TrailClassSnapshot,
                TypicalDurationHours = (double)eventItem.TrailDurationHoursSnapshot,
            };
        }

        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result, string? userId)
        {
            var difficultyLevels = DifficultyCalculator.Bands;
            var currentIndex = Array.IndexOf(difficultyLevels, currentDifficulty);

            if (currentIndex < 0)
            {
                currentIndex = 1;
            }

            var targetIndex = result switch
            {
                "Good-Match" => currentIndex,
                "Borderline" => Math.Max(0, currentIndex - 1),
                _ => 0
            };

            var registeredEventIds = string.IsNullOrEmpty(userId)
                ? new List<int>()
                : await _context.EventRegistrations
                    .Where(r => r.UserId == userId && r.Status != "Cancelled" && r.Status != "Rejected")
                    .Select(r => r.EventId)
                    .ToListAsync();

            for (var i = targetIndex; i >= 0; i--)
            {
                var events = await _context.Events
                    .Where(e =>
                        e.Id != eventId &&
                        e.Status == "Upcoming" &&
                        e.Difficulty == difficultyLevels[i] &&
                        e.EventDate >= DateTime.Today &&
                        !registeredEventIds.Contains(e.Id))
                    .OrderBy(e => e.EventDate)
                    .Take(5)
                    .ToListAsync();

                if (events.Any()) return events;
            }

            return new List<Event>();
        }
    }
}
