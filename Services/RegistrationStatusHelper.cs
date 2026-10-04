using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    public static class RegistrationStatusHelper
    {

        public const string CancelledEventStatus = "Cancelled";


        public static readonly string[] ActiveStatuses =
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Accepted"
        };

        // An Event cancellation closes its registration workflow without
        // rewriting the registration's historical status or payment records.
        public static bool IsEventCancelled(Event? eventItem) =>
            eventItem?.Status == CancelledEventStatus;

        public static bool CountsAsActiveRegistration(EventRegistration registration) =>
            !IsEventCancelled(registration.Event) && ActiveStatuses.Contains(registration.Status);

        public static bool NeedsParticipantAction(EventRegistration registration) =>
            !IsEventCancelled(registration.Event)
            && (registration.Status == "Pending" || registration.Status == "Awaiting Payment");

        public static bool IsClosedForParticipantSummary(EventRegistration registration) =>
            IsEventCancelled(registration.Event)
            || registration.Status == "Rejected"
            || registration.Status == "Voided"
            || registration.Status == "Cancelled"
            || registration.Status == "Alternative Recommended";

        public static async Task ExpireOverdueRegistrations(ApplicationDbContext context)
        {
            var now = DateTime.Now;
            var eventIds = await context.EventRegistrations
                .Where(r => r.Status == "Awaiting Payment" && r.PaymentDeadline != null && r.PaymentDeadline < now
                    && r.Event != null && r.Event.Status != CancelledEventStatus)
                .Select(r => r.EventId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();

            if (eventIds.Count == 0) return;

            await using var transaction = await context.Database.BeginTransactionAsync();
            foreach (var eventId in eventIds)
            {
                await ParticipantEventWorkflowLock.AcquireEventCapacityAsync(context, eventId);
            }
            context.ChangeTracker.Clear();

            var overdue = await context.EventRegistrations
                .Include(r => r.Event)
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Awaiting Payment" && r.PaymentDeadline != null && r.PaymentDeadline < now
                    && r.Event != null && r.Event.Status != CancelledEventStatus)
                .ToListAsync();

            foreach (var registration in overdue)
            {
                registration.Status = "Voided";
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}
