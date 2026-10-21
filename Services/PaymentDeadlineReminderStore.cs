using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services;

// The processor depends on this small unit-of-work seam so its real orchestration
// (discovery, claim, recheck, dispatch, and terminal/retry updates) can be driven
// without a database or SMTP server. The production implementation remains EF Core.
public interface IPaymentDeadlineReminderStore
{
    Task<IReadOnlyList<int>> FindCandidateIdsAsync(DateTime now, DateTime nowUtc, CancellationToken cancellationToken);
    Task<EventRegistration?> FindRegistrationAsync(int registrationId, CancellationToken cancellationToken);
    Task<bool> IsLatestAttemptAsync(EventRegistration registration, CancellationToken cancellationToken);
    Task<bool> HasVerifiedParticipantAsync(ApplicationUser? user, CancellationToken cancellationToken);
    Task<PaymentDeadlineReminder?> EnsureReminderAsync(int registrationId, DateTime deadline, DateTime nowUtc, CancellationToken cancellationToken);
    Task<bool> TryClaimAsync(long reminderId, Guid lease, DateTime nowUtc, CancellationToken cancellationToken);
    Task FinishAsync(long reminderId, Guid lease, string state, DateTime nowUtc, string? outcome, CancellationToken cancellationToken);
    Task ReleaseForConfigurationRecoveryAsync(long reminderId, Guid lease, DateTime nowUtc, CancellationToken cancellationToken);
    Task RetryOrFailAsync(long reminderId, Guid lease, DateTime nowUtc, string outcome, CancellationToken cancellationToken);
}

public sealed class EfPaymentDeadlineReminderStore(
    ApplicationDbContext context,
    UserManager<ApplicationUser> users,
    IOptions<PaymentReminderOptions> options) : IPaymentDeadlineReminderStore
{
    private readonly PaymentReminderOptions _options = options.Value;

    public async Task<IReadOnlyList<int>> FindCandidateIdsAsync(DateTime now, DateTime nowUtc, CancellationToken cancellationToken)
        => await PaymentDeadlineReminderCandidateQuery.Build(context, _options, now, nowUtc)
            .Select(r => r.Id).ToListAsync(cancellationToken);

    public Task<EventRegistration?> FindRegistrationAsync(int registrationId, CancellationToken cancellationToken) =>
        context.EventRegistrations.Include(r => r.Event).Include(r => r.User)
            .SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

    public async Task<bool> IsLatestAttemptAsync(EventRegistration registration, CancellationToken cancellationToken) =>
        await context.EventRegistrations.Where(other => other.UserId == registration.UserId && other.EventId == registration.EventId)
            .OrderByDescending(other => other.RegisteredAt).ThenByDescending(other => other.Id)
            .Select(other => other.Id).FirstAsync(cancellationToken) == registration.Id;

    public async Task<bool> HasVerifiedParticipantAsync(ApplicationUser? user, CancellationToken cancellationToken)
    {
        if (user is null || !user.IsActive || !user.EmailConfirmed || !TrailGuardEmailAddress.TryNormalize(user.Email, out _, out _)) return false;
        var roles = await users.GetRolesAsync(user);
        return OperationalRolePolicy.Evaluate(roles).Status == RoleIntegrityStatus.Participant;
    }

    public async Task<PaymentDeadlineReminder?> EnsureReminderAsync(int registrationId, DateTime deadline, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await context.PaymentDeadlineReminders.SingleOrDefaultAsync(r => r.EventRegistrationId == registrationId &&
            r.ReminderType == PaymentDeadlineReminder.PaymentDeadlineApproaching && r.PaymentDeadline == deadline, cancellationToken);
        if (existing is not null) return existing;
        var created = new PaymentDeadlineReminder { EventRegistrationId = registrationId, PaymentDeadline = deadline, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        context.PaymentDeadlineReminders.Add(created);
        try { await context.SaveChangesAsync(cancellationToken); return created; }
        catch (DbUpdateException exception) when (ParticipantEventWorkflowLock.IsUniqueConstraintConflict(exception))
        {
            context.ChangeTracker.Clear();
            return await context.PaymentDeadlineReminders.SingleAsync(r => r.EventRegistrationId == registrationId &&
                r.ReminderType == PaymentDeadlineReminder.PaymentDeadlineApproaching && r.PaymentDeadline == deadline, cancellationToken);
        }
    }

    public async Task<bool> TryClaimAsync(long reminderId, Guid lease, DateTime nowUtc, CancellationToken cancellationToken) =>
        await context.PaymentDeadlineReminders.Where(r => r.Id == reminderId && r.SentAtUtc == null && r.SupersededAtUtc == null &&
                r.AttemptCount < _options.MaxAttempts && (r.NextAttemptAtUtc == null || r.NextAttemptAtUtc <= nowUtc) &&
                (r.LeaseExpiresAtUtc == null || r.LeaseExpiresAtUtc <= nowUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.State, PaymentDeadlineReminder.Sending)
                .SetProperty(r => r.LeaseId, lease)
                .SetProperty(r => r.LeaseExpiresAtUtc, nowUtc.Add(_options.LeaseDuration))
                .SetProperty(r => r.AttemptCount, r => r.AttemptCount + 1)
                .SetProperty(r => r.LastAttemptAtUtc, nowUtc)
                .SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken) == 1;

    public Task FinishAsync(long reminderId, Guid lease, string state, DateTime nowUtc, string? outcome, CancellationToken cancellationToken) =>
        context.PaymentDeadlineReminders.Where(r => r.Id == reminderId && r.LeaseId == lease).ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.State, state).SetProperty(r => r.LeaseId, (Guid?)null).SetProperty(r => r.LeaseExpiresAtUtc, (DateTime?)null)
            .SetProperty(r => r.SentAtUtc, r => state == PaymentDeadlineReminder.Sent ? nowUtc : r.SentAtUtc)
            .SetProperty(r => r.SupersededAtUtc, r => state == PaymentDeadlineReminder.Superseded ? nowUtc : r.SupersededAtUtc)
            .SetProperty(r => r.LastOutcome, outcome).SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);

    public Task ReleaseForConfigurationRecoveryAsync(long reminderId, Guid lease, DateTime nowUtc, CancellationToken cancellationToken) =>
        context.PaymentDeadlineReminders.Where(r => r.Id == reminderId && r.LeaseId == lease).ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.State, PaymentDeadlineReminder.Pending).SetProperty(r => r.LeaseId, (Guid?)null)
            .SetProperty(r => r.LeaseExpiresAtUtc, (DateTime?)null).SetProperty(r => r.NextAttemptAtUtc, nowUtc.AddMinutes(5))
            // Claim increments first. A mid-iteration configuration loss is not a delivery attempt.
            .SetProperty(r => r.AttemptCount, r => r.AttemptCount > 0 ? r.AttemptCount - 1 : 0)
            .SetProperty(r => r.LastOutcome, "unavailable").SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);

    public Task RetryOrFailAsync(long reminderId, Guid lease, DateTime nowUtc, string outcome, CancellationToken cancellationToken) =>
        context.PaymentDeadlineReminders.Where(r => r.Id == reminderId && r.LeaseId == lease).ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.State, r => r.AttemptCount >= _options.MaxAttempts ? PaymentDeadlineReminder.Failed : PaymentDeadlineReminder.Pending)
            .SetProperty(r => r.NextAttemptAtUtc, r => r.AttemptCount >= _options.MaxAttempts ? (DateTime?)null : nowUtc.AddMinutes(5 * r.AttemptCount))
            .SetProperty(r => r.LeaseId, (Guid?)null).SetProperty(r => r.LeaseExpiresAtUtc, (DateTime?)null)
            .SetProperty(r => r.LastOutcome, outcome).SetProperty(r => r.UpdatedAtUtc, nowUtc), cancellationToken);
}

// This exposes the exact query executed by EfPaymentDeadlineReminderStore so the
// Npgsql provider can translate it in verification without opening a database connection.
public static class PaymentDeadlineReminderCandidateQuery
{
    public static IQueryable<EventRegistration> Build(ApplicationDbContext context, PaymentReminderOptions options, DateTime now, DateTime nowUtc)
    {
        var windowEnd = now.AddHours(24);
        return context.EventRegistrations.AsNoTracking()
            .Where(r => r.Id == context.EventRegistrations
                .Where(other => other.UserId == r.UserId && other.EventId == r.EventId)
                .OrderByDescending(other => other.RegisteredAt).ThenByDescending(other => other.Id)
                .Select(other => other.Id).First())
            .Where(r => r.Status == "Awaiting Payment" && !r.IsPaid && r.PaymentDeadline != null &&
                r.PaymentDeadline > now && r.PaymentDeadline <= windowEnd && r.Event != null && r.Event.Status == "Upcoming")
            // Completed, exhausted, delayed, and actively leased rows must not consume the
            // bounded discovery batch; a later due recipient must still be discoverable.
            .Where(r => !context.PaymentDeadlineReminders.Any(reminder =>
                reminder.EventRegistrationId == r.Id &&
                reminder.ReminderType == PaymentDeadlineReminder.PaymentDeadlineApproaching &&
                reminder.PaymentDeadline == r.PaymentDeadline &&
                (reminder.SentAtUtc != null || reminder.SupersededAtUtc != null ||
                 reminder.AttemptCount >= options.MaxAttempts ||
                 (reminder.NextAttemptAtUtc != null && reminder.NextAttemptAtUtc > nowUtc) ||
                 (reminder.LeaseExpiresAtUtc != null && reminder.LeaseExpiresAtUtc > nowUtc))))
            .OrderBy(r => r.PaymentDeadline).ThenBy(r => r.Id).Take(options.BatchSize);
    }
}
