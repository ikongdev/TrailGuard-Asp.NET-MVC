using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

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
