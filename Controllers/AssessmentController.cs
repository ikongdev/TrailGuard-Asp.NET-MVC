using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Participant")]
    public class AssessmentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AssessmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Form(int eventId)
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
            
            // ✅ I-clear ang lumang error messages
            TempData.Remove("Error");
            
            // ✅ I-check kung may ACTIVE registration (Pending or Accepted)
            var activeRegistration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId && (r.Status == "Pending" || r.Status == "Accepted"));
            
            if (activeRegistration != null)
            {
                TempData["Success"] = "You are already registered for this event.";
                return RedirectToAction("Details", "Participant", new { id = eventId });
            }

            // ✅ I-check kung may ACTIVE assessment
            var existingAssessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (existingAssessment != null)
            {
                // ✅ I-soft delete ang lumang assessment para mag-retake
                existingAssessment.IsActive = false;
                await _context.SaveChangesAsync();
                
                // ✅ I-redirect sa Assessment Form para sa bagong assessment
                ViewBag.Event = eventItem;
                ViewBag.RetakeMode = true; // Para malaman ng view na retake ito
                return View();
            }

            ViewBag.Event = eventItem;
            ViewBag.RetakeMode = false;
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
            // ✅ I-declare ang eventItem dito
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
                ViewBag.Event = eventItem;
                return View();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // ✅ I-process ang medicalConditions
            if (!string.IsNullOrEmpty(medicalConditions))
            {
                var medicalList = medicalConditions.Split(',').Select(m => m.Trim()).Where(m => !string.IsNullOrEmpty(m)).ToList();
                medicalConditions = string.Join(",", medicalList);
            }

            // ✅ I-process ang gearItems (array → comma-separated string)
            var gearItemsString = "";
            if (gearItems != null && gearItems.Length > 0)
            {
                gearItemsString = string.Join(",", gearItems.Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)));
            }

            // ✅ Kunin ang trail difficulty
            var trailDifficulty = eventItem.Difficulty ?? "Moderate";

            // Compute scores
            var fitnessScore = ComputeFitnessScore(exerciseFrequency, exerciseType, cardioEndurance);
            var experienceScore = ComputeExperienceScore(mountainsClimbed, recencyOfHike, trailDifficultyCompleted);
            var healthScore = ComputeHealthScore(medicalConditions, age, heightCm, weightKg);
            var gearScore = ComputeGearScore(gearItemsString);
            
            var totalScore = fitnessScore + experienceScore + healthScore + gearScore;
            
            // ✅ Pass ang difficulty sa GetResult
            var result = GetResult(totalScore, trailDifficulty);

            // Compute risk flags
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

            // ✅ I-soft delete ang lumang assessment
            var oldAssessment = await _context.Assessments
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.UserId == userId && a.IsActive == true);

            if (oldAssessment != null)
            {
                oldAssessment.IsActive = false;
            }

            // ✅ Gumawa ng bagong assessment
            var assessment = new Assessment
            {
                EventId = eventId,
                UserId = userId ?? "",
                Age = age,
                Gender = gender,
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
            var threshold = difficulty switch
            {
                "Technical" => 40,
                "Difficult" => 36,
                "Moderate" => 32,
                "Easy" => 28,
                _ => 32
            };

            var recommendations = ComputeRecommendations(
                assessment.Result ?? "",
                assessment.CardioEndurance,
                assessment.ExerciseFrequency,
                assessment.MountainsClimbed,
                trail?.Terrain ?? "",
                eventItem?.EstimatedDuration ?? 0,
                assessment.TotalScore ?? 0,
                threshold
            );

            var alternativeEvents = await GetAlternativeEvents(
                eventItem?.Id ?? 0,
                eventItem?.Difficulty ?? "",
                assessment.Result ?? ""
            );

            var viewModel = new AssessmentResultViewModel
            {
                AssessmentId = assessment.Id,
                EventId = assessment.EventId,
                EventTitle = eventItem?.EventTitle ?? "Event Not Found",
                EventDifficulty = difficulty,
                Result = assessment.Result ?? "Not Recommended",
                TotalScore = assessment.TotalScore ?? 0,
                MaxScore = 44,
                Threshold = threshold,
                FitnessScore = assessment.FitnessScore ?? 0,
                FitnessMax = 12,
                ExperienceScore = assessment.ExperienceScore ?? 0,
                ExperienceMax = 12,
                HealthScore = assessment.HealthScore ?? 0,
                HealthMax = 12,
                GearScore = assessment.GearScore ?? 0,
                GearMax = 8,
                RiskFlags = ComputeRiskFlags(
                    assessment.CardioEndurance,
                    assessment.ExerciseFrequency,
                    assessment.HeightCm,
                    assessment.WeightKg,
                    assessment.Age,
                    assessment.TrailDifficultyCompleted,
                    assessment.MedicalConditions,
                    assessment.GearItems,
                    trail?.Terrain ?? "",
                    eventItem?.EstimatedDuration ?? 0
                ),
                Recommendations = recommendations,
                AlternativeEvents = alternativeEvents,
                Answers = new Dictionary<string, string>
                {
                    { "Age", assessment.Age?.ToString() ?? "N/A" },
                    { "Gender", assessment.Gender ?? "N/A" },
                    { "Height/Weight", $"{assessment.HeightCm?.ToString() ?? "N/A"}cm / {assessment.WeightKg?.ToString() ?? "N/A"}kg" },
                    { "Medical Conditions", assessment.MedicalConditions ?? "None" },
                    { "Exercise Frequency", assessment.ExerciseFrequency ?? "N/A" },
                    { "Exercise Type", assessment.ExerciseType ?? "N/A" },
                    { "Cardio Endurance", assessment.CardioEndurance ?? "N/A" },
                    { "Mountains Climbed", assessment.MountainsClimbed ?? "N/A" },
                    { "Recency of Hike", assessment.RecencyOfHike ?? "N/A" },
                    { "Trail Difficulty Completed", assessment.TrailDifficultyCompleted ?? "N/A" },
                    { "Gear Items", assessment.GearItems ?? "None" }
                }
            };

            ViewBag.Assessment = assessment;
            return View(viewModel);
        }

        // ===== COMPUTATION METHODS =====

        private int ComputeFitnessScore(string? exerciseFrequency, string? exerciseType, string? cardioEndurance)
        {
            int score = 0;

            // Exercise Frequency (max 4)
            score += exerciseFrequency switch
            {
                "5 or more times per week" => 4,
                "3 to 4 times per week" => 3,
                "1 to 2 times per week" => 2,
                _ => 1 // Sedentary
            };

            // Exercise Type (max 4)
            score += exerciseType switch
            {
                "Combination of cardio and strength training" => 4,
                "Cardio or endurance only" => 2,
                "Strength or resistance only" => 2,
                _ => 1 // None
            };

            // Cardio Endurance (max 4)
            score += cardioEndurance switch
            {
                "More than 60 minutes" => 4,
                "31 to 60 minutes" => 3,
                "15 to 30 minutes" => 2,
                _ => 1 // Less than 15 minutes
            };

            return score;
        }

        private int ComputeExperienceScore(string? mountainsClimbed, string? recencyOfHike, string? trailDifficultyCompleted)
        {
            int score = 0;

            // Mountains Climbed (max 4)
            score += mountainsClimbed switch
            {
                "More than 10 mountains / Experienced" => 4,
                "4 to 10 mountains / Intermediate" => 3,
                "1 to 3 mountains / Beginner" => 2,
                _ => 1 // First-timer
            };

            // Recency of Hike (max 4)
            score += recencyOfHike switch
            {
                "Within the past 1 to 3 months" => 4,
                "Within the past 4 to 12 months" => 3,
                "More than 1 year ago" => 2,
                _ => 1 // Never climbed
            };

            // Trail Difficulty Completed (max 4)
            score += trailDifficultyCompleted switch
            {
                "Multi-day or overnight expeditions" => 4,
                "Major hikes with steep assault sections" => 3,
                "Minor day hikes only" => 2,
                _ => 1 // None
            };

            return score;
        }

        private int ComputeHealthScore(string? medicalConditions, int? age, double? heightCm, double? weightKg)
        {
            int score = 0;

            // Medical Conditions (max 4)
            var conditions = medicalConditions?.Split(',').Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
            var conditionCount = conditions.Count(c => c != "None of the above");

            score += conditionCount switch
            {
                0 => 4, // None
                1 => 3,
                2 => 2,
                _ => 1 // 3 or more
            };

            // Age Factor (max 4)
            if (age.HasValue)
            {
                score += age.Value switch
                {
                    >= 18 and <= 35 => 4,
                    >= 36 and <= 50 => 3,
                    >= 51 and <= 65 => 2,
                    _ => 1 // 65+
                };
            }

            // BMI Factor (max 4)
            if (heightCm.HasValue && heightCm.Value > 0 && weightKg.HasValue && weightKg.Value > 0)
            {
                var heightM = heightCm.Value / 100;
                var bmi = weightKg.Value / (heightM * heightM);

                score += bmi switch
                {
                    >= 18.5 and < 25 => 4, // Normal
                    >= 25 and < 30 => 2,   // Overweight
                    < 18.5 => 2,           // Underweight
                    _ => 1                 // Obese
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
            // ✅ Threshold based sa difficulty ng trail
            var threshold = difficulty switch
            {
                "Technical" => 40,
                "Difficult" => 36,
                "Moderate" => 32,
                "Easy" => 28,
                _ => 32 // Default
            };

            // ✅ Borderline threshold (8 points below Good-Match)
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

            // Low Cardio
            if (cardioEndurance == "Less than 15 minutes" || cardioEndurance == "15 to 30 minutes" ||
                exerciseFrequency == "I do not exercise / Sedentary" || exerciseFrequency == "1 to 2 times per week")
            {
                flags.Add("Low Cardio");
            }

            // Elevation Challenge
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

            // Moderate Endurance Gap
            if (estimatedDuration > 6 && 
                (cardioEndurance == "Less than 15 minutes" || cardioEndurance == "15 to 30 minutes"))
            {
                flags.Add("Moderate Endurance Gap");
            }

            // Terrain Difficulty
            if ((terrain == "Difficult" || terrain == "Technical") && 
                (trailDifficultyCompleted == "None" || trailDifficultyCompleted == "First-timer" || 
                trailDifficultyCompleted == "Minor day hikes only"))
            {
                flags.Add("Terrain Difficulty");
            }

            // Medical Risk
            if (!string.IsNullOrEmpty(medicalConditions) && medicalConditions != "None of the above" &&
                (string.IsNullOrEmpty(gearItems) || !gearItems.Contains("First aid kit")))
            {
                flags.Add("Medical Risk");
            }

            // Gear Gap
            var gearCount = string.IsNullOrEmpty(gearItems) ? 0 : gearItems.Split(',').Length;
            if (gearCount < 4)
            {
                flags.Add("Gear Gap");
            }

            return flags;
        }

        private List<string> ComputeRecommendations(
            string result,
            string? cardioEndurance,
            string? exerciseFrequency,
            string? mountainsClimbed,
            string terrain,
            double estimatedDuration,
            int totalScore,
            int threshold)
        {
            var recommendations = new List<string>();

            if (result == "Good-Match")
            {
                recommendations.Add("Great! You are well-prepared for this event.");
                recommendations.Add($"Your score of {totalScore} meets the requirement of {threshold}.");
                recommendations.Add("Continue your current fitness routine.");
                recommendations.Add("Stay hydrated and enjoy the hike!");
            }
            else if (result == "Borderline")
            {
                var gap = threshold - totalScore;
                recommendations.Add($"Your score of {totalScore} is {gap} points below the required {threshold}.");
                recommendations.Add("Increase cardio training to 3-4x per week.");
                recommendations.Add("Practice hiking with a weighted pack.");
                recommendations.Add("Consider trying an easier trail first.");
                
                if (cardioEndurance == "Less than 15 minutes" || cardioEndurance == "15 to 30 minutes")
                {
                    recommendations.Add("Build your cardio endurance by running or cycling.");
                }
                
                if (terrain == "Difficult" || terrain == "Technical")
                {
                    recommendations.Add("Familiarize yourself with steep terrain before the event.");
                }
            }
            else // Not Recommended
            {
                var gap = threshold - totalScore;
                recommendations.Add($"Your score of {totalScore} is {gap} points below the required {threshold}.");
                recommendations.Add("Start with easier trails first.");
                recommendations.Add("Build your fitness gradually.");
                recommendations.Add("Consider joining a beginner-friendly event.");
                
                if (mountainsClimbed == "This will be my first time / First-timer")
                {
                    recommendations.Add("Try a shorter, easier trail for your first hike.");
                }
                
                if (estimatedDuration > 6)
                {
                    recommendations.Add("Start with shorter hikes before attempting long-duration events.");
                }
            }

            return recommendations;
        }

        private async Task<List<Event>> GetAlternativeEvents(int eventId, string currentDifficulty, string result)
        {
            var alternativeEvents = new List<Event>();

            // I-determine ang target difficulty based sa result
            var difficultyLevels = new List<string> { "Easy", "Moderate", "Difficult", "Technical" };
            var currentIndex = difficultyLevels.IndexOf(currentDifficulty);

            int targetIndex;
            if (result == "Good-Match")
            {
                targetIndex = currentIndex; // Same difficulty
            }
            else if (result == "Borderline")
            {
                targetIndex = Math.Max(0, currentIndex - 1); // One level lower
            }
            else // Not Recommended
            {
                targetIndex = Math.Max(0, currentIndex - 2); // Two levels lower
            }

            var targetDifficulty = difficultyLevels[targetIndex];

            alternativeEvents = await _context.Events
                .Include(e => e.Trail)
                .Where(e => 
                    e.Id != eventId && 
                    e.Status == "Upcoming" &&
                    e.Difficulty == targetDifficulty &&
                    e.EventDate >= DateTime.Today)
                .Take(5)
                .ToListAsync();

            return alternativeEvents;
        }
    }
}