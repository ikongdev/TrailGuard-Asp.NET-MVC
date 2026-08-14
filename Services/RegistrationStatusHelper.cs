using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;

namespace TrailGuard.Services
{
    public static class RegistrationStatusHelper
    {
        // Non-terminal statuses: a registration in one of these still holds a capacity slot
        // and blocks the participant from starting a new registration for the same event.
        public static readonly string[] ActiveStatuses =
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Accepted"
        };

        public static async Task ExpireOverdueRegistrations(ApplicationDbContext context)
        {
            var now = DateTime.Now;
            var overdue = await context.EventRegistrations
                .Where(r => r.Status == "Awaiting Payment" && r.PaymentDeadline != null && r.PaymentDeadline < now)
                .ToListAsync();

            if (overdue.Count == 0) return;

            foreach (var registration in overdue)
            {
                registration.Status = "Voided";
            }

            await context.SaveChangesAsync();
        }
    }
}
