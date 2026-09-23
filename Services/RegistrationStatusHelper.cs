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
