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

        public AssessmentController(ApplicationDbContext context, SuitabilityApiClient suitabilityApi)
        {
            _context = context;
            _suitabilityApi = suitabilityApi;
        }

        // Loads the event and sets ViewBag.Event / ViewBag.RetakeMode the same way
        // for the GET form and every POST validation-failure redisplay, so the two
        // paths can't drift apart. Returns null if the event doesn't exist.
        private async Task<Event?> PopulateAssessmentFormViewBagAsync(int eventId, string? userId)
        {
            var eventItem = await _context.Events
                .Include(e => e.Trail)
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
                .Include(e => e.Trail)
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

            var fitnessScore = ComputeFitnessScore(exerciseFrequency, exerciseType, cardioEndurance);
            var experienceScore = ComputeExperienceScore(mountainsClimbed, recencyOfHike, trailDifficultyCompleted);
            var healthScore = ComputeHealthScore(medicalConditions, age, heightCm, weightKg);
            var gearScore = ComputeMlGearScore(gearItemsString);

            var totalScore = fitnessScore + experienceScore + healthScore + gearScore;

            SuitabilityPredictionResponse? mlResponse = null;

            if (eventItem.Trail != null)
            {
                var mlRequest = BuildMlRequest(
                    heightCm, weightKg, medicalConditions,
                    exerciseFrequency, cardioEndurance, exerciseConsistency,
                    mountainsClimbed, recencyOfHike, trailDifficultyCompleted,
                    gearItemsString, eventItem.Trail
                );

                mlResponse = await _suitabilityApi.PredictAsync(mlRequest);
            }

            // No rule-based fallback: GetResult() was a v1 heuristic with its own
            // notion of trail demand, agreeing with neither the model nor the ACSM/
            // NPS-based ground truth in generate_synthetic_dataset.py. Producing a
            // result from it would be a third, unvalidated answer to the same
            // question. If the model can't answer, neither do we.
            if (mlResponse == null)
            {
                TempData["Error"] = "The assessment service is temporarily unavailable. Please try again shortly.";
                await PopulateAssessmentFormViewBagAsync(eventId, userId);
                return View();
            }

            var result = NormalizeLabel(mlResponse.SuitabilityLabel);

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
                MedicalClearanceRequired = mlResponse.MedicalClearanceRequired,
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
                TotalScore = totalScore,
                FitnessScore = fitnessScore,
                ExperienceScore = experienceScore,
                HealthScore = healthScore,
                GearScore = gearScore,
                IsActive = true,
                SubmittedAt = DateTime.Now
            };

            _context.Assessments.Add(assessment);
            await _context.SaveChangesAsync();

            var suitabilityResult = new SuitabilityResult
            {
                AssessmentId = assessment.Id,
                PredictedLabel = mlResponse.SuitabilityLabel,
                ModelLabel = mlResponse.ModelLabel,
                ConfidenceScore = mlResponse.ConfidenceScore,
                GateApplied = mlResponse.GateApplied,
                GateReason = mlResponse.GateReason,
                NpsScore = mlResponse.NpsScore,
                NpsBand = mlResponse.NpsBand,
                ModelVersion = mlResponse.ModelVersion,
                PredictedAt = DateTime.Now
            };

            _context.SuitabilityResults.Add(suitabilityResult);
            await _context.SaveChangesAsync();

            foreach (var shap in mlResponse.ShapBreakdown)
            {
                _context.ShapValues.Add(new ShapValue
                {
                    SuitabilityResultId = suitabilityResult.Id,
                    FeatureName = shap.Feature,
                    ImpactValue = shap.Impact,
                    RawValue = shap.RawValue.ToString()
                });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Report", new { assessmentId = assessment.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Report(int assessmentId)
        {

            var assessment = await _context.Assessments
                .Include(a => a.Event)
                .ThenInclude(e => e!.Trail)
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.IsActive == true);

            if (assessment == null)
            {
                TempData["Error"] = "Assessment not found or has been replaced.";
                return RedirectToAction("Events", "Participant");
            }

            var eventItem = assessment.Event;
            var trail = eventItem?.Trail;

            var difficulty = eventItem?.Difficulty ?? "Moderate";

            var suitabilityResult = await _context.SuitabilityResults
                .Include(s => s.ShapValues)
                .FirstOrDefaultAsync(s => s.AssessmentId == assessmentId);

            var shapFactors = new List<ShapDisplayItem>();
            if (suitabilityResult != null && suitabilityResult.ShapValues.Any())
            {
                var topShapValues = suitabilityResult.ShapValues
                    .OrderByDescending(s => Math.Abs(s.ImpactValue))
                    .Take(6)
                    .ToList();

                var totalAbsImpact = topShapValues.Sum(s => Math.Abs(s.ImpactValue));
                if (totalAbsImpact == 0) totalAbsImpact = 1;

                shapFactors = topShapValues
                    .Select(s => new ShapDisplayItem
                    {
                        FeatureName = s.FeatureName,
                        FriendlyName = GetFriendlyFeatureName(s.FeatureName),
                        RawValue = s.RawValue ?? "",
                        Impact = s.ImpactValue,
                        BarWidth = Math.Round(Math.Abs(s.ImpactValue) / totalAbsImpact * 100, 1)
                    })
                    .ToList();
            }

            var recommendations = ComputeRecommendations(assessment.Result ?? "", shapFactors, suitabilityResult != null);

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
                ConfidenceScore = suitabilityResult?.ConfidenceScore ?? 0,
                ModelVersion = suitabilityResult?.ModelVersion ?? "",
                ShapFactors = shapFactors,
                NpsScore = suitabilityResult?.NpsScore ?? 0,
                NpsBand = suitabilityResult?.NpsBand ?? "",
                GateApplied = suitabilityResult?.GateApplied ?? false,
                GateReason = suitabilityResult?.GateReason ?? "",
                MedicalClearanceRequired = assessment.MedicalClearanceRequired
            };

            ViewBag.Assessment = assessment;
            return View(viewModel);
        }

        // for ML
        private string NormalizeLabel(string mlLabel) => mlLabel switch
        {
            "Good Match" => "Good-Match",
            "Borderline" => "Borderline",
            "Not Recommended" => "Not Recommended",
            _ => "Not Recommended"
        };
        // Exact-string lookups for the ML request's ordinal fields. Keyed on the
        // literal option values from Views/Assessment/Form.cshtml. An answer that
        // doesn't match throws rather than silently defaulting to 0 - a silent
        // default would make an unfit participant look unfit for the wrong reason,
        // and a fit one look unfit too.
        private static readonly Dictionary<string, int> ExerciseFrequencyMap = new()
        {
            ["I do not exercise / Sedentary"] = 0,
            ["1 to 2 times per week"] = 1,
            ["3 to 4 times per week"] = 2,
            ["5 or more times per week"] = 3,
        };

        private static readonly Dictionary<string, int> CardioEnduranceMap = new()
        {
            ["Less than 15 minutes"] = 0,
            ["15 to 29 minutes"] = 1,
            ["30 to 60 minutes"] = 2,
            ["More than 60 minutes"] = 3,
        };

        private static readonly Dictionary<string, int> ExerciseConsistencyMap = new()
        {
            ["Less than 1 month"] = 0,
            ["1 to 2 months"] = 1,
            ["3 months or more"] = 2,
        };

        private static readonly Dictionary<string, int> MountainsClimbedMap = new()
        {
            ["This will be my first time / First-timer"] = 0,
            ["1 to 3 mountains / Beginner"] = 1,
            ["4 to 10 mountains / Intermediate"] = 2,
            ["More than 10 mountains / Experienced"] = 3,
        };

        private static readonly Dictionary<string, int> RecencyOfHikeMap = new()
        {
            ["I have never climbed a mountain before"] = 0,
            ["More than 1 year ago"] = 1,
            ["Within the past 4 to 12 months"] = 2,
            ["Within the past 1 to 3 months"] = 3,
        };

        private static readonly Dictionary<string, int> TrailDifficultyCompletedMap = new()
        {
            ["None / I have never climbed before"] = 0,
            ["Minor day hikes only"] = 1,
            ["Major hikes with steep assault sections"] = 2,
            ["Multi-day or overnight expeditions"] = 3,
        };

        private static int MapScore(IReadOnlyDictionary<string, int> map, string? value, string fieldName)
        {
            if (value != null && map.TryGetValue(value, out var score))
            {
                return score;
            }

            throw new ArgumentException($"Unrecognized {fieldName} value: '{value ?? "null"}'");
        }

        private bool HasCondition(string? medicalConditions, string keyword)
        {
            if (string.IsNullOrEmpty(medicalConditions)) return false;
            return medicalConditions.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private int ComputeMlGearScore(string? gearItems)
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
            string? trailDifficultyCompleted, string? gearItems, Trail trail)
        {
            var h = heightCm ?? 165;
            var w = weightKg ?? 60;
            var heightM = h / 100;
            var bmi = heightM > 0 ? w / (heightM * heightM) : 22.0;

            if (trail.TerrainType < 1 || trail.TerrainType > 3)
            {
                throw new InvalidOperationException(
                    $"Trail '{trail.Name}' (Id={trail.Id}) has an invalid TerrainType ({trail.TerrainType}); expected 1-3.");
            }

            return new SuitabilityPredictionRequest
            {
                Bmi = Math.Round(bmi, 2),
                ExerciseFrequencyScore = MapScore(ExerciseFrequencyMap, exerciseFrequency, "exerciseFrequency"),
                ContinuousCardioDurationScore = MapScore(CardioEnduranceMap, cardioEndurance, "cardioEndurance"),
                ExerciseConsistencyScore = MapScore(ExerciseConsistencyMap, exerciseConsistency, "exerciseConsistency"),
                HikingExperienceScore = MapScore(MountainsClimbedMap, mountainsClimbed, "mountainsClimbed"),
                LastHikeRecencyScore = MapScore(RecencyOfHikeMap, recencyOfHike, "recencyOfHike"),
                HardestTrailCompletedScore = MapScore(TrailDifficultyCompletedMap, trailDifficultyCompleted, "trailDifficultyCompleted"),
                GearScore = ComputeMlGearScore(gearItems),
                HasAsthma = HasCondition(medicalConditions, "Asthma") ? 1 : 0,
                HasCvd = HasCondition(medicalConditions, "Hypertension") ? 1 : 0,
                HasJointKneeInjury = HasCondition(medicalConditions, "Joint or knee") ? 1 : 0,
                HasCvdSymptoms = (HasCondition(medicalConditions, "Vertigo")
                                || HasCondition(medicalConditions, "Chest pain")
                                || HasCondition(medicalConditions, "Shortness of breath")) ? 1 : 0,
                TrailDistanceKm = trail.DistanceKm,
                TrailElevationGainM = trail.ElevationGainMeters,
                TrailTerrainType = trail.TerrainType,
            };
        }

        private int ComputeFitnessScore(string? exerciseFrequency, string? exerciseType, string? cardioEndurance)
        {
            int score = 0;

            score += exerciseFrequency switch
            {
                "5 or more times per week" => 4,
                "3 to 4 times per week" => 3,
                "1 to 2 times per week" => 2,
                _ => 1 // Sedentary
            };

            score += exerciseType switch
            {
                "Combination of cardio and strength training" => 4,
                "Cardio or endurance only" => 2,
                "Strength or resistance only" => 2,
                _ => 1
            };

            score += cardioEndurance switch
            {
                "More than 60 minutes" => 4,
                "30 to 60 minutes" => 3,
                "15 to 29 minutes" => 2,
                _ => 1
            };

            return score;
        }

        private int ComputeExperienceScore(string? mountainsClimbed, string? recencyOfHike, string? trailDifficultyCompleted)
        {
            int score = 0;

            score += mountainsClimbed switch
            {
                "More than 10 mountains / Experienced" => 4,
                "4 to 10 mountains / Intermediate" => 3,
                "1 to 3 mountains / Beginner" => 2,
                _ => 1
            };

            score += recencyOfHike switch
            {
                "Within the past 1 to 3 months" => 4,
                "Within the past 4 to 12 months" => 3,
                "More than 1 year ago" => 2,
                _ => 1 // Never climbed
            };

            score += trailDifficultyCompleted switch
            {
                "Multi-day or overnight expeditions" => 4,
                "Major hikes with steep assault sections" => 3,
                "Minor day hikes only" => 2,
                _ => 1
            };

            return score;
        }

        private int ComputeHealthScore(string? medicalConditions, int? age, double? heightCm, double? weightKg)
        {
            int score = 0;

            var conditions = medicalConditions?.Split(',').Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
            var conditionCount = conditions.Count(c => c != "None of the above");

            score += conditionCount switch
            {
                0 => 4,
                1 => 3,
                2 => 2,
                _ => 1 
            };

            if (age.HasValue)
            {
                score += age.Value switch
                {
                    >= 18 and <= 35 => 4,
                    >= 36 and <= 50 => 3,
                    >= 51 and <= 65 => 2,
                    _ => 1
                };
            }

            if (heightCm.HasValue && heightCm.Value > 0 && weightKg.HasValue && weightKg.Value > 0)
            {
                var heightM = heightCm.Value / 100;
                var bmi = weightKg.Value / (heightM * heightM);

                score += bmi switch
                {
                    >= 18.5 and < 25 => 4,
                    >= 25 and < 30 => 2,
                    < 18.5 => 2,
                    _ => 1
                };
            }

            return score;
        }

        // Exactly the 14 v2 SHAP feature names. Throws rather than falling through
        // to the raw snake_case string - a name we don't recognize means the
        // Python feature contract moved and this mapping wasn't updated with it,
        // and a raw "has_cvd_symptoms" shown to a participant is worse than a
        // crash caught in development.
        private string GetFriendlyFeatureName(string featureName) => featureName switch
        {
            "bmi" => "Body Mass Index",
            "exercise_frequency_score" => "Exercise frequency",
            "continuous_cardio_duration_score" => "Cardio endurance",
            "exercise_consistency_score" => "How long you've been exercising",
            "hiking_experience_score" => "Hiking experience",
            "last_hike_recency_score" => "Recency of last hike",
            "hardest_trail_completed_score" => "Hardest trail completed",
            "gear_score" => "Gear preparedness",
            "has_asthma" => "Asthma or lung condition",
            "has_cvd" => "Hypertension or heart condition",
            "has_joint_knee_injury" => "Joint or knee injury",
            "has_cvd_symptoms" => "Reported symptoms",
            "trail_shenandoah_score" => "Trail difficulty rating",
            "trail_terrain_type" => "Terrain type",
            _ => throw new ArgumentException($"Unrecognized SHAP feature name: '{featureName}'")
        };

        private static readonly HashSet<string> TrailSideFeatures = new HashSet<string>
        {
            "trail_shenandoah_score", "trail_terrain_type"
        };

        private static readonly HashSet<string> HealthFlagFeatures = new HashSet<string>
        {
            "has_asthma", "has_cvd", "has_joint_knee_injury", "has_cvd_symptoms"
        };

        private string? GetRecommendationForFeature(string featureName)
        {
            if (TrailSideFeatures.Contains(featureName)) return null;

            // Reported symptoms are the one case the ACSM algorithm gates on
            // regardless of fitness or intensity - the generic health-flag advice
            // below undersells that, so this gets its own line.
            if (featureName == "has_cvd_symptoms")
                return "See a doctor before joining any hike — the symptoms you reported need to be checked first.";

            if (HealthFlagFeatures.Contains(featureName)) return "Consult a physician before joining a hike of this difficulty";

            return featureName switch
            {
                "exercise_frequency_score" => "Increase how often you exercise each week",
                "continuous_cardio_duration_score" => "Build up how long you can sustain cardio without stopping",
                "exercise_consistency_score" => "Build a consistent routine — aim for at least three months at your current frequency.",
                "hiking_experience_score" => "Gain experience on easier trails before attempting this one",
                "last_hike_recency_score" => "Consider a shorter warm-up hike before this event",
                "hardest_trail_completed_score" => "Work up through easier trail types first",
                "gear_score" => "Complete your gear checklist before the hike",
                "bmi" => "General fitness preparation may help",
                _ => null
            };
        }

        private List<string> ComputeRecommendations(string result, List<ShapDisplayItem> shapFactors, bool hasMlPrediction)
        {
            var recommendations = new List<string>();

            if (!hasMlPrediction)
            {
                recommendations.Add("Personalized recommendations aren't available for this result.");
                return recommendations;
            }

            var negativeAdvice = shapFactors
                .Where(f => !f.IsPositive)
                .Select(f => GetRecommendationForFeature(f.FeatureName))
                .Where(advice => advice != null)
                .Select(advice => $"{advice} — this was one of the main factors working against your result.")
                .Distinct()
                .ToList();

            if (negativeAdvice.Any())
            {
                recommendations.AddRange(negativeAdvice!);
            }
            else
            {
                recommendations.Add("Nothing significant is working against your result for this event.");
            }

            return recommendations;
        }

        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result, string? userId)
        {
            var difficultyLevels = new List<string> { "Easy", "Moderate", "Difficult" };
            var currentIndex = difficultyLevels.IndexOf(currentDifficulty);

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
                    .Include(e => e.Trail)
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