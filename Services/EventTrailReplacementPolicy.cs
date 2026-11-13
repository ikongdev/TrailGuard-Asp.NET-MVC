using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;

namespace TrailGuard.Services;

/// <summary>Any participation history freezes an Event's trail identity, irrespective of status.</summary>
public static class EventTrailReplacementPolicy
{
    public const string LockedMessage = "The trail cannot be changed because this event already has assessments or registrations. Create a new event to use another trail.";

    // EditEvent must call this after its event-wide lock and authoritative reload.
    public static async Task<bool> HasParticipationRecordsAsync(ApplicationDbContext context, int eventId) =>
        await context.Assessments.AnyAsync(assessment => assessment.EventId == eventId)
        || await context.EventRegistrations.AnyAsync(registration => registration.EventId == eventId);
}
