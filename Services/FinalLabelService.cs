using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using System.Linq;

namespace TrailGuard.Services
{
    public static class FinalLabelService
    {
        // Difficulty is an experience rather than a single observed fact. If either
        // party reports a harder experience, preserve that conservative signal.
        private static readonly Dictionary<string, int> ConservativeDifficultyOrder = new()
        {
            { "Could not finish - injured", 1 },
            { "Could not finish - turned back", 2 },
            { "Much harder", 3 },
            { "Harder than expected", 4 },
            { "Matched but challenging", 5 },
            { "Matched perfectly", 6 },
            { "Much easier than expected", 7 }
        };

        public static bool IsKnownOutcome(string? feedback)
        {
            return feedback is "Much easier than expected" or "Matched perfectly"
                or "Matched but challenging" or "Harder than expected" or "Much harder"
                or "Could not finish - turned back" or "Could not finish - injured";
        }

        public static string? ResolveConservativeDifficulty(string? participantDifficulty, string? organizerDifficulty)
        {
            var participantKnown = participantDifficulty != null && ConservativeDifficultyOrder.ContainsKey(participantDifficulty);
            var organizerKnown = organizerDifficulty != null && ConservativeDifficultyOrder.ContainsKey(organizerDifficulty);
            if (participantKnown && organizerKnown)
                return ConservativeDifficultyOrder[participantDifficulty!] <= ConservativeDifficultyOrder[organizerDifficulty!]
                    ? participantDifficulty
                    : organizerDifficulty;
            return participantKnown ? participantDifficulty : organizerKnown ? organizerDifficulty : null;
        }

        // Completion and difficulty are independent observations. Keep each party's
        // completion/reason together; the organizer observes the group and is the
        // authoritative source when present, regardless of submission order.
        public static bool IsValidCompletion(bool? completed, string? reason) => completed switch
        {
            true => string.IsNullOrEmpty(reason) || reason == "NotApplicable",
            false => reason is "Readiness" or "External" or "Withdrawal",
            _ => false
        };

        public static (bool Completed, string Reason)? ResolveOutcome(
            EventFeedback? participant, PostEventAssessment? organizer)
        {
            bool? completed = organizer?.Completed ?? participant?.Completed;
            if (completed == null) return null;
            var organizerPresent = organizer?.Completed != null;
            var reason = organizerPresent ? organizer!.NonCompletionReason : participant!.NonCompletionReason;
            if (!IsValidCompletion(completed, reason))
                throw new InvalidOperationException("Invalid stored completion/reason pair.");
            return (completed.Value, completed.Value ? "NotApplicable" : reason!);
        }

        public static async Task UpsertFinalLabel(ApplicationDbContext context, int registrationId)
        {
            // Both callers hold the registration lock and transaction across source
            // save and this upsert. Concurrent submissions cannot resolve stale inputs.
            if (context.Database.CurrentTransaction == null)
                throw new InvalidOperationException("Outcome resolution requires the source transaction.");
            var registration = await context.EventRegistrations.Include(r => r.Assessment)
                .Include(r => r.Event).FirstOrDefaultAsync(r => r.Id == registrationId);
            if (registration?.Assessment == null || registration.Status != "Accepted" || registration.Event?.Status != "Completed")
                throw new InvalidOperationException("An accepted registration with an assessment and completed event is required.");
            var participant = await context.EventFeedbacks.FirstOrDefaultAsync(f => f.EventId == registration.EventId && f.UserId == registration.UserId);
            var organizer = await context.PostEventAssessments.FirstOrDefaultAsync(f => f.EventId == registration.EventId && f.UserId == registration.UserId);
            var outcome = ResolveOutcome(participant, organizer);
            if (outcome == null) throw new InvalidOperationException("No completion answer available.");
            var row = await context.FinalSuitabilityLabels
                .FirstOrDefaultAsync(f => f.AssessmentId == registration.Assessment.Id);
            if (row == null)
            {
                row = new FinalSuitabilityLabel { AssessmentId = registration.Assessment.Id };
                context.FinalSuitabilityLabels.Add(row);
            }
            row.Completed = outcome.Value.Completed;
            row.NonCompletionReason = outcome.Value.Reason;
            row.ParticipantFeedback = participant?.DifficultyExperience;
            row.OrganizerAssessment = organizer?.DifficultyExperience;
            row.DifficultyExperience = ResolveConservativeDifficulty(
                row.ParticipantFeedback, row.OrganizerAssessment);
            row.RecordedAt = DateTime.Now;
            await context.SaveChangesAsync();
        }
    }
}
