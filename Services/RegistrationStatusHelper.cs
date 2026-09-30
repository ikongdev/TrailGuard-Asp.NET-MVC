using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;

namespace TrailGuard.Services
{
    public static class RegistrationStatusHelper
    {


        public static readonly string[] ActiveStatuses =
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Accepted"
        };

        public static async Task ExpireOverdueRegistrations(ApplicationDbContext context)
        {
            var now = DateTime.Now;
            var eventIds = await context.EventRegistrations
                .Where(r => r.Status == "Awaiting Payment" && r.PaymentDeadline != null && r.PaymentDeadline < now)
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
                .Where(r => eventIds.Contains(r.EventId) && r.Status == "Awaiting Payment" && r.PaymentDeadline != null && r.PaymentDeadline < now)
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
