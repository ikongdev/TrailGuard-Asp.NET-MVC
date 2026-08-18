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
            string? gender,
            double? heightCm,
            double? weightKg,
            string? medicalConditions,
            string? exerciseFrequency,
            string? exerciseType,
            string? cardioEndurance,
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

            var trailDifficulty = eventItem.Difficulty ?? "Moderate";

            var fitnessScore = ComputeFitnessScore(exerciseFrequency, exerciseType, cardioEndurance);
            var experienceScore = ComputeExperienceScore(mountainsClimbed, recencyOfHike, trailDifficultyCompleted);
            var healthScore = ComputeHealthScore(medicalConditions, age, heightCm, weightKg);
            var gearScore = ComputeGearScore(gearItemsString);
            
            var totalScore = fitnessScore + experienceScore + healthScore + gearScore;

            string result;
            SuitabilityPredictionResponse? mlResponse = null;

            if (eventItem.Trail != null)
            {
                var mlRequest = BuildMlRequest(
                    age, heightCm, weightKg, medicalConditions,
                    exerciseFrequency, exerciseType, cardioEndurance,
                    mountainsClimbed, recencyOfHike, trailDifficultyCompleted,
                    gearItemsString, eventItem.Trail, eventItem.EstimatedDuration
                );

                mlResponse = await _suitabilityApi.PredictAsync(mlRequest);
            }

            if (mlResponse != null)
            {
                result = NormalizeLabel(mlResponse.SuitabilityLabel);
            }
            else
            {
                result = GetResult(totalScore, trailDifficulty);
            }

            var riskFlags = ComputeRiskFlags(
                cardioEndurance,
                exerciseFrequency,
                heightCm,
                weightKg,
                age,
                trailDifficultyCompleted,
                medicalConditions,
                gearItemsString,
                eventItem.Trail?.Terrain ?? "",
                eventItem.EstimatedDuration
            );

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
                ExerciseFrequency = exerciseFrequency,
                ExerciseType = exerciseType,
                CardioEndurance = cardioEndurance,
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

            if (mlResponse != null)
            {
                var suitabilityResult = new SuitabilityResult
                {
                    AssessmentId = assessment.Id,
                    PredictedLabel = mlResponse.SuitabilityLabel,
                    ConfidenceScore = mlResponse.ConfidenceScore,
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
            }

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
                ShapFactors = shapFactors
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
        private int MapExerciseFrequency(string? value) => value switch
        {
            "5 or more times per week" => 3,
            "3 to 4 times per week" => 2,
            "1 to 2 times per week" => 1,
            _ => 0
        };

        private int MapExerciseType(string? value) => value switch
        {
            "Combination of cardio and strength training" => 3,
            "Cardio or endurance only" => 2,
            "Strength or resistance only" => 2,
            _ => 1
        };

        private int MapCardioEndurance(string? value) => value switch
        {
            "More than 60 minutes" => 3,
            "31 to 60 minutes" => 2,
            "15 to 30 minutes" => 1,
            _ => 0
        };

        private int MapMountainsClimbed(string? value) => value switch
        {
            "More than 10 mountains / Experienced" => 3,
            "4 to 10 mountains / Intermediate" => 2,
            "1 to 3 mountains / Beginner" => 1,
            _ => 0
        };

        private int MapRecencyOfHike(string? value) => value switch
        {
            "Within the past 1 to 3 months" => 3,
            "Within the past 4 to 12 months" => 2,
            "More than 1 year ago" => 1,
            _ => 0
        };

        private int MapTrailDifficultyCompleted(string? value) => value switch
        {
            "Multi-day or overnight expeditions" => 3,
            "Major hikes with steep assault sections" => 2,
            "Minor day hikes only" => 1,
            _ => 0
        };

        private int MapTerrainType(string? terrain)
        {
            var t = terrain?.ToLower() ?? "";
            if (t.Contains("rocky") || t.Contains("volcanic") || t.Contains("technical"))
                return 3;
            if (t.Contains("grassland") || t.Contains("pine forest"))
                return 1;
            return 2;
        }

        private bool HasCondition(string? medicalConditions, string keyword)
        {
            if (string.IsNullOrEmpty(medicalConditions)) return false;
            return medicalConditions.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasGear(string? gearItems, string itemName)
        {
            if (string.IsNullOrEmpty(gearItems)) return false;
            return gearItems.Split(',')
                .Select(g => g.Trim())
                .Any(g => g.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        }

        private SuitabilityPredictionRequest BuildMlRequest(
            int? age, double? heightCm, double? weightKg,
            string? medicalConditions, string? exerciseFrequency, string? exerciseType,
            string? cardioEndurance, string? mountainsClimbed, string? recencyOfHike,
            string? trailDifficultyCompleted, string? gearItems,
            Trail trail, double estimatedDuration)
        {
            var h = heightCm ?? 165;
            var w = weightKg ?? 60;
            var heightM = h / 100;
            var bmi = heightM > 0 ? w / (heightM * heightM) : 22.0;

            var gearWater = HasGear(gearItems, "Enough water") ? 1 : 0;
            var gearFood = HasGear(gearItems, "Trail food") ? 1 : 0;
            var gearFirstAid = HasGear(gearItems, "First aid kit") ? 1 : 0;
            var gearFlashlight = HasGear(gearItems, "Flashlight") ? 1 : 0;
            var gearWhistle = HasGear(gearItems, "Whistle") ? 1 : 0;
            var gearRaincoat = HasGear(gearItems, "Raincoat") ? 1 : 0;
            var gearNavigation = HasGear(gearItems, "Navigation tool") ? 1 : 0;
            var gearShoes = HasGear(gearItems, "Proper hiking shoes") ? 1 : 0;

            return new SuitabilityPredictionRequest
            {
                Age = age ?? 25,
                HeightCm = h,
                WeightKg = w,
                Bmi = Math.Round(bmi, 2),
                HasAsthma = HasCondition(medicalConditions, "Asthma") ? 1 : 0,
                HasHypertensionHeartCondition = HasCondition(medicalConditions, "Hypertension") ? 1 : 0,
                HasJointKneeInjury = HasCondition(medicalConditions, "Joint or knee") ? 1 : 0,
                HasVertigo = HasCondition(medicalConditions, "Vertigo") ? 1 : 0,
                ExerciseFrequencyScore = MapExerciseFrequency(exerciseFrequency),
                ExerciseTypeCategory = MapExerciseType(exerciseType),
                ContinuousCardioDurationScore = MapCardioEndurance(cardioEndurance),
                HikingExperienceScore = MapMountainsClimbed(mountainsClimbed),
                LastHikeRecencyScore = MapRecencyOfHike(recencyOfHike),
                HardestTrailCompletedScore = MapTrailDifficultyCompleted(trailDifficultyCompleted),
                GearWater = gearWater,
                GearTrailFood = gearFood,
                GearFirstAidMedicine = gearFirstAid,
                GearFlashlightHeadlamp = gearFlashlight,
                GearWhistle = gearWhistle,
                GearRaincoatPoncho = gearRaincoat,
                GearNavigation = gearNavigation,
                GearProperShoes = gearShoes,
                GearScore = gearWater + gearFood + gearFirstAid + gearFlashlight
                        + gearWhistle + gearRaincoat + gearNavigation + gearShoes,
                TrailDistanceKm = trail.DistanceKm,
                TrailElevationGainM = trail.ElevationGainMeters,
                TrailTerrainType = MapTerrainType(trail.Terrain),
                TrailEstimatedDurationHr = estimatedDuration
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
                "31 to 60 minutes" => 3,
                "15 to 30 minutes" => 2,
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

        private int ComputeGearScore(string? gearItems)
        {
            Console.WriteLine($"===== COMPUTE GEAR SCORE =====");
            Console.WriteLine($"Input gearItems: '{gearItems}'");
            
            if (string.IsNullOrEmpty(gearItems)) 
            {
                Console.WriteLine("gearItems is null or empty");
                return 0;
            }
            
            var items = gearItems.Split(',')
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrEmpty(g))
                .ToList();
            
            Console.WriteLine($"Items after split: {string.Join(", ", items)}");
            Console.WriteLine($"Items count: {items.Count}");
            
            var score = Math.Min(items.Count, 8);
            Console.WriteLine($"Final score: {score}");
            Console.WriteLine($"================================");
            
            return score;
        }

        private string GetResult(int totalScore, string difficulty)
        {
            var threshold = difficulty switch
            {
                "Technical" => 40,
                "Difficult" => 36,
                "Moderate" => 32,
                "Easy" => 28,
                _ => 32
            };

            var borderlineThreshold = threshold - 8;

            Console.WriteLine($"Difficulty: {difficulty}, Threshold: {threshold}, Score: {totalScore}");

            if (totalScore >= threshold)
                return "Good-Match";
            else if (totalScore >= borderlineThreshold)
                return "Borderline";
            else
                return "Not Recommended";
        }

        private List<string> ComputeRiskFlags(
            string? cardioEndurance,
            string? exerciseFrequency,
            double? heightCm,
            double? weightKg,
            int? age,
            string? trailDifficultyCompleted,
            string? medicalConditions,
            string? gearItems,
            string terrain,
            double estimatedDuration)
        {
            var flags = new List<string>();

            if (cardioEndurance == "Less than 15 minutes" || cardioEndurance == "15 to 30 minutes" ||
                exerciseFrequency == "I do not exercise / Sedentary" || exerciseFrequency == "1 to 2 times per week")
            {
                flags.Add("Low Cardio");
            }

            var bmi = 0.0;
            if (heightCm.HasValue && heightCm.Value > 0 && weightKg.HasValue && weightKg.Value > 0)
            {
                var heightM = heightCm.Value / 100;
                bmi = weightKg.Value / (heightM * heightM);
            }

            if (bmi > 25 && (age > 50 || trailDifficultyCompleted == "None" || trailDifficultyCompleted == "First-timer"))
            {
                flags.Add("Elevation Challenge");
            }

            if (estimatedDuration > 6 && 
                (cardioEndurance == "Less than 15 minutes" || cardioEndurance == "15 to 30 minutes"))
            {
                flags.Add("Moderate Endurance Gap");
            }

            if ((terrain == "Difficult" || terrain == "Technical") && 
                (trailDifficultyCompleted == "None" || trailDifficultyCompleted == "First-timer" || 
                trailDifficultyCompleted == "Minor day hikes only"))
            {
                flags.Add("Terrain Difficulty");
            }

            if (!string.IsNullOrEmpty(medicalConditions) && medicalConditions != "None of the above" &&
                (string.IsNullOrEmpty(gearItems) || !gearItems.Contains("First aid kit")))
            {
                flags.Add("Medical Risk");
            }

            var gearCount = string.IsNullOrEmpty(gearItems) ? 0 : gearItems.Split(',').Length;
            if (gearCount < 4)
            {
                flags.Add("Gear Gap");
            }

            return flags;
        }


        private string GetFriendlyFeatureName(string featureName) => featureName switch
        {
            "age" => "Age",
            "height_cm" => "Height",
            "weight_kg" => "Weight",
            "bmi" => "Body Mass Index",
            "has_asthma" => "Asthma condition",
            "has_hypertension_heart_condition" => "Hypertension / heart condition",
            "has_joint_knee_injury" => "Joint or knee injury",
            "has_vertigo" => "Vertigo",
            "exercise_frequency_score" => "Exercise frequency",
            "exercise_type_category" => "Type of exercise",
            "continuous_cardio_duration_score" => "Cardio endurance",
            "hiking_experience_score" => "Hiking experience",
            "last_hike_recency_score" => "Recency of last hike",
            "hardest_trail_completed_score" => "Hardest trail completed",
            "gear_water" => "Water supply",
            "gear_trail_food" => "Trail food",
            "gear_first_aid_medicine" => "First aid kit",
            "gear_flashlight_headlamp" => "Flashlight / headlamp",
            "gear_whistle" => "Whistle",
            "gear_raincoat_poncho" => "Raincoat / poncho",
            "gear_navigation" => "Navigation tool",
            "gear_proper_shoes" => "Proper hiking shoes",
            "gear_score" => "Overall gear preparedness",
            "trail_distance_km" => "Trail distance",
            "trail_elevation_gain_m" => "Trail elevation gain",
            "trail_terrain_type" => "Terrain difficulty",
            "trail_estimated_duration_hr" => "Estimated hike duration",
            _ => featureName
        };

        private static readonly HashSet<string> TrailSideFeatures = new HashSet<string>
        {
            "trail_distance_km", "trail_elevation_gain_m", "trail_terrain_type", "trail_estimated_duration_hr"
        };

        private static readonly HashSet<string> HealthFlagFeatures = new HashSet<string>
        {
            "has_asthma", "has_hypertension_heart_condition", "has_joint_knee_injury", "has_vertigo"
        };

        private string? GetRecommendationForFeature(string featureName)
        {
            if (TrailSideFeatures.Contains(featureName)) return null;
            if (HealthFlagFeatures.Contains(featureName)) return "Consult a physician before joining a hike of this difficulty";
            if (featureName.StartsWith("gear_", StringComparison.OrdinalIgnoreCase)) return "Complete your gear checklist before the hike";

            return featureName switch
            {
                "exercise_frequency_score" => "Increase how often you exercise each week",
                "continuous_cardio_duration_score" => "Build up how long you can sustain cardio without stopping",
                "hiking_experience_score" => "Gain experience on easier trails before attempting this one",
                "last_hike_recency_score" => "Consider a shorter warm-up hike before this event",
                "hardest_trail_completed_score" => "Work up through easier trail types first",
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