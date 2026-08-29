using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using System.Linq;

namespace TrailGuard.Services
{
    public static class FinalLabelService
    {
        // Lower = worse (more conservative). Single source of truth — also used by
        // OrganizerController.GetConservativeResult via GetMoreConservativeFeedback.
        private static readonly Dictionary<string, int> ConservativeOrder = new()
        {
            { "Could not finish - injured", 1 },
            { "Could not finish - turned back", 2 },
            { "Much harder", 3 },
            { "Harder than expected", 4 },
            { "Matched but challenging", 5 },
            { "Matched perfectly", 6 },
            { "Much easier than expected", 7 }
        };

        // The three suitability categories, ordered easiest to most restrictive.
        // Single source of truth for anything comparing two labels ordinally
        // (ReportsController's accuracy/confusion-matrix/kappa computations).
        public static readonly string[] LabelCategories = { "Good-Match", "Borderline", "Not Recommended" };

        private static readonly Dictionary<string, int> LabelOrderMap =
            LabelCategories.Select((label, index) => (label, index)).ToDictionary(x => x.label, x => x.index);

        public static int? LabelOrder(string? label) =>
            label != null && LabelOrderMap.TryGetValue(label, out var order) ? order : null;

        // SuitabilityResult.ModelLabel is stored raw from the ML API ("Good Match", space)
        // — the same conversion AssessmentController.NormalizeLabel applies to the gated
        // result, kept separate here because that method is private to that controller.
        public static string? NormalizeModelLabel(string? modelLabel) => modelLabel switch
        {
            "Good Match" => "Good-Match",
            "Borderline" => "Borderline",
            "Not Recommended" => "Not Recommended",
            _ => null
        };

        // Single source of truth for pre-hike-vs-outcome classification, used by both
        // OrganizerController.EventComparison (per event) and ReportsController (aggregate)
        // so the two views can't independently invert the same comparison again.
        //
        // "Overestimated"/"Underestimated" were dropped as names because they're
        // ambiguous — overestimating the difficulty and overestimating the participant
        // are opposite directions, and that ambiguity is what let the two call sites
        // disagree on the same data:
        //   Over-cautious - predicted harder than it turned out. Inefficient, not unsafe.
        //   Missed risk    - predicted easier than it turned out. Safety-critical: a
        //                    participant was told they were ready, and they were not.
        // Null when either label isn't one of the three known categories.
        public static string? ClassifyAccuracy(string? predictedLabel, string? finalLabel)
        {
            var predictedOrder = LabelOrder(predictedLabel);
            var finalOrder = LabelOrder(finalLabel);
            if (predictedOrder == null || finalOrder == null) return null;

            if (predictedOrder == finalOrder) return "Accurate";
            return predictedOrder > finalOrder ? "Over-cautious" : "Missed risk";
        }

        // Cohen's kappa (unweighted) and quadratic weighted kappa over a k x k confusion
        // matrix (rows = predicted, columns = actual). Weighted kappa is the headline figure
        // for these three ordinal categories — unweighted kappa treats every disagreement
        // the same, so it can't tell a Good-Match/Borderline mixup from a Good-Match/Not
        // Recommended one.
        public static (double Unweighted, double Weighted) ComputeKappa(int[,] matrix)
        {
            var k = matrix.GetLength(0);
            var n = 0;
            for (var i = 0; i < k; i++)
                for (var j = 0; j < k; j++)
                    n += matrix[i, j];

            if (n == 0) return (0, 0);

            var rowTotals = new double[k];
            var colTotals = new double[k];
            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    rowTotals[i] += matrix[i, j];
                    colTotals[j] += matrix[i, j];
                }
            }

            double observedAgreement = 0;
            double expectedAgreement = 0;
            double observedWeighted = 0;
            double expectedWeighted = 0;

            for (var i = 0; i < k; i++)
            {
                for (var j = 0; j < k; j++)
                {
                    var expected = rowTotals[i] * colTotals[j] / n;
                    var weight = k > 1 ? Math.Pow(i - j, 2) / Math.Pow(k - 1, 2) : 0;

                    if (i == j) observedAgreement += matrix[i, j];
                    if (i == j) expectedAgreement += expected;

                    observedWeighted += weight * matrix[i, j];
                    expectedWeighted += weight * expected;
                }
            }

            var po = observedAgreement / n;
            var pe = expectedAgreement / n;
            var unweighted = pe >= 1.0 ? 0.0 : (po - pe) / (1 - pe);
            var weighted = expectedWeighted == 0 ? (observedWeighted == 0 ? 1.0 : 0.0) : 1 - (observedWeighted / expectedWeighted);

            return (unweighted, weighted);
        }

        public static string? MapFeedbackToClass(string? feedback)
        {
            return feedback switch
            {
                "Much easier than expected" => "Good-Match",
                "Matched perfectly" => "Good-Match",
                "Matched but challenging" => "Borderline",
                "Harder than expected" => "Borderline",
                "Much harder" => "Not Recommended",
                "Could not finish - turned back" => "Not Recommended",
                "Could not finish - injured" => "Not Recommended",
                _ => null
            };
        }

        // Exact-membership check for the seven canonical hike-outcome/feedback
        // strings the switch above maps - kept beside it so the two can never
        // drift apart. The switch is an exact, case-sensitive, non-trimming
        // string match with a single "_ => null" fallthrough for everything
        // else, so a null result from MapFeedbackToClass is a genuine,
        // reliable "not one of the seven" signal (never a coincidence of its
        // own display-class fallback) - this wrapper exists purely so a
        // caller doing validation (see OrganizerController.SubmitPostEventAssessment)
        // doesn't have to reason about that itself, and so a second,
        // independent list of the same seven strings never needs to exist
        // anywhere else in the codebase.
        public static bool IsKnownOutcome(string? feedback) => MapFeedbackToClass(feedback) != null;

        public static string? GetMoreConservativeFeedback(string? feedbackA, string? feedbackB)
        {
            var hasA = feedbackA != null && ConservativeOrder.ContainsKey(feedbackA);
            var hasB = feedbackB != null && ConservativeOrder.ContainsKey(feedbackB);

            if (hasA && hasB)
            {
                return ConservativeOrder[feedbackA!] <= ConservativeOrder[feedbackB!] ? feedbackA : feedbackB;
            }

            if (hasA) return feedbackA;
            if (hasB) return feedbackB;

            return null;
        }

        public static string? ResolveFinalLabel(string? participantFeedback, string? organizerAssessment)
        {
            return MapFeedbackToClass(GetMoreConservativeFeedback(participantFeedback, organizerAssessment));
        }

        public static async Task UpsertFinalLabel(ApplicationDbContext context, int registrationId)
        {
            var registration = await context.EventRegistrations
                .Include(r => r.Assessment)
                .FirstOrDefaultAsync(r => r.Id == registrationId);

            var existing = await context.FinalSuitabilityLabels
                .FirstOrDefaultAsync(l => l.RegistrationId == registrationId);

            if (registration == null || registration.Status != "Accepted" || registration.Assessment == null)
            {
                if (existing != null)
                {
                    context.FinalSuitabilityLabels.Remove(existing);
                    await context.SaveChangesAsync();
                }
                return;
            }

            var participantFeedback = await context.EventFeedbacks
                .Where(f => f.EventId == registration.EventId && f.UserId == registration.UserId)
                .Select(f => f.DifficultyExperience)
                .FirstOrDefaultAsync();

            var organizerAssessment = await context.PostEventAssessments
                .Where(a => a.EventId == registration.EventId && a.UserId == registration.UserId)
                .Select(a => a.DifficultyExperience)
                .FirstOrDefaultAsync();

            var rawModelLabel = await context.SuitabilityResults
                .Where(s => s.AssessmentId == registration.Assessment.Id)
                .Select(s => s.ModelLabel)
                .FirstOrDefaultAsync();
            var modelPreHikeLabel = NormalizeModelLabel(rawModelLabel);

            var finalLabel = ResolveFinalLabel(participantFeedback, organizerAssessment);

            if (finalLabel == null)
            {
                if (existing != null)
                {
                    context.FinalSuitabilityLabels.Remove(existing);
                    await context.SaveChangesAsync();
                }
                return;
            }

            if (existing != null)
            {
                existing.ParticipantFeedback = participantFeedback;
                existing.OrganizerAssessment = organizerAssessment;
                existing.FinalLabel = finalLabel;
                existing.PreHikeLabel = registration.Assessment.Result ?? existing.PreHikeLabel;
                existing.ModelPreHikeLabel = modelPreHikeLabel;
                existing.AssessmentId = registration.Assessment.Id;
                existing.ResolvedAt = DateTime.Now;
            }
            else
            {
                context.FinalSuitabilityLabels.Add(new FinalSuitabilityLabel
                {
                    RegistrationId = registration.Id,
                    EventId = registration.EventId,
                    UserId = registration.UserId,
                    AssessmentId = registration.Assessment.Id,
                    PreHikeLabel = registration.Assessment.Result ?? "N/A",
                    ModelPreHikeLabel = modelPreHikeLabel,
                    ParticipantFeedback = participantFeedback,
                    OrganizerAssessment = organizerAssessment,
                    FinalLabel = finalLabel,
                    ResolvedAt = DateTime.Now
                });
            }

            await context.SaveChangesAsync();
        }
    }
}
