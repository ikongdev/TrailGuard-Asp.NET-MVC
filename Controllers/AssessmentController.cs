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
        private readonly TrailGuardV2ApiClient _trailGuardV2Api;
        private readonly TrailGuardV2AssessmentRequestMapper _trailGuardV2Mapper;
        private readonly ILogger<AssessmentController> _logger;
        private readonly IPhilippineClock _philippineClock;

        public AssessmentController(ApplicationDbContext context, SuitabilityApiClient suitabilityApi,
            TrailGuardV2ApiClient trailGuardV2Api, TrailGuardV2AssessmentRequestMapper trailGuardV2Mapper,
            ILogger<AssessmentController> logger, IPhilippineClock? philippineClock = null)
        {
            _context = context;
            _suitabilityApi = suitabilityApi;
            _trailGuardV2Api = trailGuardV2Api;
            _trailGuardV2Mapper = trailGuardV2Mapper;
            _logger = logger;
            _philippineClock = philippineClock ?? new PhilippineClock(TimeProvider.System);
        }




        protected virtual async Task<Event?> PopulateAssessmentFormViewBagAsync(int eventId, string? userId)
        {


            var eventItem = await FindEventAsync(eventId);

            if (eventItem == null)
            {
                return null;
            }

            var isRetake = await HasActiveAssessmentAsync(eventId, userId);

            ViewBag.Event = eventItem;
            ViewBag.RetakeMode = isRetake;

            return eventItem;
        }

        [HttpGet]
        public async Task<IActionResult> Form(int eventId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!AssessmentSubmissionGuards.HasAuthenticatedUserId(userId))
            {
                return Forbid();
            }

            var eventItem = await PopulateAssessmentFormViewBagAsync(eventId, userId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            TempData.Remove("Error");

            if (await HasActiveRegistrationAsync(eventId, userId))
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            var input = new TrailGuardV2AssessmentFormInput { EventId = eventId };
            var demographics = await HydrateAuthoritativeDemographicsAsync(input, userId);
            if (!demographics.IsComplete)
            {
                TempData["Error"] = $"{demographics.Error} Use Settings to provide your demographics.";
                return RedirectToAction("Index", "Settings");
            }

            if (input.Age is < 18 or > 60)
            {
                TempData["Error"] = "Your current age must be between 18 and 60 to complete an assessment. You can update your Birthday in Settings.";
                return RedirectToAction("Index", "Settings");
            }

            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Form(TrailGuardV2AssessmentFormInput input)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!AssessmentSubmissionGuards.HasAuthenticatedUserId(userId))
            {
                return Forbid();
            }

            // Demographics are profile-owned. Ignore any forged posted values before validation.
            ModelState.Remove(nameof(input.Age));
            ModelState.Remove(nameof(input.Gender));

            var eventItem = await FindEventAsync(input.EventId);

            if (eventItem == null)
            {
                TempData["Error"] = "Event not found";
                return RedirectToAction("Events", "Participant");
            }

            if (await HasActiveRegistrationAsync(input.EventId, userId))
            {
                ModelState.AddModelError(string.Empty, "You are already registered for this event.");
                await HydrateAuthoritativeDemographicsAsync(input, userId);
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            var demographics = await HydrateAuthoritativeDemographicsAsync(input, userId);
            if (!demographics.IsComplete)
            {
                ModelState.AddModelError(string.Empty, $"{demographics.Error} Use Settings to provide your demographics.");
            }
            else if (input.Age is < 18 or > 60)
            {
                ModelState.AddModelError(nameof(input.Age), "Your current age must be between 18 and 60 to complete an assessment.");
            }

            if (!input.ConsentGiven || !input.DataPrivacyConsent)
            {
                ModelState.AddModelError(nameof(input.ConsentGiven), "Both acknowledgements are required to proceed.");
            }
            var medicalSelection = AssessmentSubmissionGuards.ValidateMedicalSelections(input.MedicalConditions);
            if (!medicalSelection.IsValid)
            {
                ModelState.AddModelError(nameof(input.MedicalConditions), medicalSelection.Error!);
            }
            if (!ModelState.IsValid)
            {
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            if (await HasActiveRegistrationAsync(input.EventId, userId))
            {
                ModelState.AddModelError(string.Empty, "You are already registered for this event.");
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            var mapping = _trailGuardV2Mapper.Map(new TrailGuardV2AssessmentAnswers
            {
                ExerciseFrequency = input.ExerciseFrequency, CardioDuration = input.CardioEndurance,
                ExerciseConsistency = input.ExerciseConsistency, HikingExperience = input.MountainsClimbed,
                HikingRecency = input.RecencyOfHike, HardestTrailCompleted = input.TrailDifficultyCompleted,
                GearItems = input.GearItems
            }, eventItem);
            if (!mapping.IsValid)
            {
                AddMappingErrors(mapping.Errors);
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            var selectedMedicalConditions = medicalSelection.CanonicalSelections;
            var medicalConditions = string.Join(",", selectedMedicalConditions.Select(value => value.Trim()));
            var acsmClearanceRequired = AcsmClearanceService.RequiresMedicalClearance(
                selectedMedicalConditions.Any(value => value.Contains("Vertigo", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("Chest pain", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("Shortness of breath", StringComparison.OrdinalIgnoreCase)),
                selectedMedicalConditions.Any(value => value.Contains("Hypertension", StringComparison.OrdinalIgnoreCase)));
            var predictionCall = await _trailGuardV2Api.PredictAsync(mapping.Request!);
            if (!predictionCall.IsSuccess || predictionCall.Prediction is null)
            {
                ModelState.AddModelError(string.Empty, predictionCall.Failure == TrailGuardV2PredictionFailure.InvalidInput
                    ? "One or more assessment answers could not be recognized." : "The assessment service is temporarily unavailable.");
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            if (await HasActiveRegistrationAsync(input.EventId, userId))
            {
                ModelState.AddModelError(string.Empty, "You are already registered for this event.");
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await ParticipantEventWorkflowLock.AcquireAsync(_context, input.EventId, userId);
                _context.ChangeTracker.Clear();

                if (await HasActiveRegistrationAsync(input.EventId, userId))
                {
                    ModelState.AddModelError(string.Empty, "You are already registered for this event.");
                    await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                    return View(input);
                }

                var oldAssessment = await _context.Assessments
                    .FirstOrDefaultAsync(a => a.EventId == input.EventId && a.UserId == userId && a.IsActive);
                await OnActiveAssessmentLoadedForPersistenceAsync(oldAssessment);

                var assessment = new Assessment
                {
                    EventId = input.EventId, UserId = userId, Age = demographics.Age, HeightCm = input.HeightCm,
                    WeightKg = input.WeightKg, MedicalConditions = medicalConditions, MedicalClearanceRequired = acsmClearanceRequired,
                    ExerciseFrequency = input.ExerciseFrequency, ExerciseType = input.ExerciseType, CardioEndurance = input.CardioEndurance,
                    ExerciseConsistency = input.ExerciseConsistency, MountainsClimbed = input.MountainsClimbed,
                    RecencyOfHike = input.RecencyOfHike, TrailDifficultyCompleted = input.TrailDifficultyCompleted,
                    GearItems = string.Join(",", input.GearItems!), ConsentGiven = input.ConsentGiven,
                    Result = NormalizeLabel(predictionCall.Prediction.UiLabel!), IsActive = true, SubmittedAt = DateTime.Now
                };
                TrailGuardV2SuitabilityResultFactory.Create(assessment, DateTimeOffset.UtcNow, mapping.Request!, predictionCall.Prediction);
                if (oldAssessment is not null) oldAssessment.IsActive = false;
                _context.Assessments.Add(assessment);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return RedirectToAction("Report", new { assessmentId = assessment.Id });
            }
            catch (DbUpdateException ex) when (ParticipantEventWorkflowLock.IsUniqueConstraintConflict(ex))
            {
                _logger.LogWarning(ex, "TrailGuard v2 assessment write conflicted for Event {EventId}.", input.EventId);
                ModelState.AddModelError(string.Empty, "Your assessment was updated by another request. Please review the latest result and try again.");
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrailGuard v2 assessment persistence failed for Event {EventId}.", input.EventId);
                ModelState.AddModelError(string.Empty, "Your assessment could not be saved. Please try again.");
                await PopulateAssessmentFormViewBagAsync(input.EventId, userId);
                return View(input);
            }
        }

        protected virtual Task<Event?> FindEventAsync(int eventId) => _context.Events
            .FirstOrDefaultAsync(e => e.Id == eventId);

        protected virtual Task<bool> HasActiveAssessmentAsync(int eventId, string? userId) => _context.Assessments
            .AnyAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

        protected virtual Task<bool> HasActiveRegistrationAsync(int eventId, string userId) => _context.EventRegistrations
            .AnyAsync(r => r.EventId == eventId
                && r.UserId == userId
                && RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));

        protected virtual async Task<ParticipantDemographicsResult> HydrateAuthoritativeDemographicsAsync(
            TrailGuardV2AssessmentFormInput input, string userId)
        {
            var profile = await _context.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.Birthday, user.Gender })
                .FirstOrDefaultAsync();

            var demographics = profile is null
                ? ParticipantDemographicsResult.Incomplete("Update your account details in Settings before continuing.")
                : ParticipantDemographics.Resolve(profile.Birthday, profile.Gender, _philippineClock.Today);

            input.Age = demographics.Age;
            input.Gender = demographics.Gender;
            ModelState.Remove(nameof(input.Age));
            ModelState.Remove(nameof(input.Gender));
            return demographics;
        }

        /// <summary>Test-only coordination point after the authoritative active assessment is loaded under the workflow lock.</summary>
        protected virtual Task OnActiveAssessmentLoadedForPersistenceAsync(Assessment? oldAssessment) => Task.CompletedTask;

        [HttpGet]
        public async Task<IActionResult> Report(int assessmentId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!AssessmentSubmissionGuards.HasAuthenticatedUserId(userId))
            {
                return Forbid();
            }

            var assessment = await _context.Assessments
                .Include(a => a.Event)
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.IsActive == true && a.UserId == userId);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found or has been replaced.";
                return RedirectToAction("Events", "Participant");
            }

            var eventItem = assessment.Event;

            var difficulty = DifficultyCalculator.DisplayLabel(eventItem?.Difficulty);

            var suitabilityResults = await _context.SuitabilityResults
                .Include(s => s.ShapValues)
                .Where(s => s.AssessmentId == assessmentId)
                .ToListAsync();
            var selection = SuitabilityResultSelector.Select(suitabilityResults);
            if (selection.IsInvalidOrUnsupported)
            {
                TempData["Error"] = "This assessment's prediction record is invalid or unsupported and cannot be displayed.";
                return RedirectToAction("Events", "Participant");
            }
            var suitabilityResult = selection.Result;

            var shapFactors = suitabilityResult != null
                ? selection.IsRecognizedV2
                    ? TrailGuardV2Presentation.BuildV2Factors(suitabilityResult.ShapValues)
                    : ShapHelper.BuildDisplayItems(suitabilityResult.ShapValues)
                : new List<ShapDisplayItem>();

            var recommendations = suitabilityResult != null
                ? selection.IsRecognizedV2
                    ? TrailGuardV2Presentation.BuildV2Suggestions(suitabilityResult.ShapValues)
                    : ShapHelper.BuildRecommendations(suitabilityResult.ShapValues)
                : new List<string>();

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
                ModelScore = suitabilityResult?.ModelScore ?? 0,
                ModelVersion = suitabilityResult?.ModelVersion ?? "",
                IsTrailGuardV2 = selection.IsRecognizedV2,
                ScoreName = suitabilityResult?.ScoreName ?? "",
                TrailDuration = suitabilityResult is not null && selection.IsRecognizedV2
                    ? TrailGuardV2Presentation.FormatDuration(suitabilityResult.TypicalDurationHours ?? 0)
                    : string.Empty,
                ShapFactors = shapFactors,
                AcsmMedicalClearanceRequired = assessment.MedicalClearanceRequired,
                RequiresMedicalClearance = RegistrationRulesHelper.RequiresMedicalClearance(assessment),
                RequiresPreparationPlan = RegistrationRulesHelper.RequiresPreparationPlan(assessment),
                ShowNotRecommendedOrganizerNotice = assessment.Result == "Not Recommended"
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

        private void AddMappingErrors(IEnumerable<TrailGuardV2MappingError> errors)
        {
            var formFieldByFeature = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["exercise_frequency"] = nameof(TrailGuardV2AssessmentFormInput.ExerciseFrequency),
                ["cardio_duration"] = nameof(TrailGuardV2AssessmentFormInput.CardioEndurance),
                ["exercise_consistency"] = nameof(TrailGuardV2AssessmentFormInput.ExerciseConsistency),
                ["hiking_experience"] = nameof(TrailGuardV2AssessmentFormInput.MountainsClimbed),
                ["hiking_recency"] = nameof(TrailGuardV2AssessmentFormInput.RecencyOfHike),
                ["hardest_trail_completed"] = nameof(TrailGuardV2AssessmentFormInput.TrailDifficultyCompleted),
                ["gear_items"] = nameof(TrailGuardV2AssessmentFormInput.GearItems)
            };

            foreach (var error in errors)
            {
                var field = formFieldByFeature.GetValueOrDefault(error.Field, string.Empty);
                ModelState.AddModelError(field, error.Message);
            }
        }
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
            var currentIndex = DifficultyCalculator.BucketRank(currentDifficulty);
            if (currentIndex > 2) currentIndex = 1;

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
                        e.EventDate >= DateTime.Today &&
                        !registeredEventIds.Contains(e.Id))
                    .OrderBy(e => e.EventDate)
                    .ToListAsync();

                events = events.Where(e => DifficultyCalculator.BucketRank(e.Difficulty) == i).Take(5).ToList();

                if (events.Any()) return events;
            }

            return new List<Event>();
        }
    }
}
