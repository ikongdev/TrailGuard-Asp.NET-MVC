using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Narrow persistence boundary for Event lifecycle transitions. Every operation
/// uses the existing event-wide PostgreSQL advisory lock in production.
/// </summary>
public interface IEventLifecycleStore
{
    Task<Event?> FindEventAsync(int eventId);
    Task<IEventLifecycleTransaction> BeginTransactionAsync();
    Task AcquireEventLockAsync(int eventId);
    void ClearTracking();
    Task<Event?> ReloadEventAsync(int eventId);
    Task<List<EventRegistration>> GetRegistrationsToVoidAsync(int eventId);
    Task SaveChangesAsync();
}

public interface IEventLifecycleTransaction : IAsyncDisposable
{
    Task CommitAsync();
}

public sealed class EventLifecycleStore(ApplicationDbContext context) : IEventLifecycleStore
{
    public Task<Event?> FindEventAsync(int eventId) => context.Events.FindAsync(eventId).AsTask();

    public async Task<IEventLifecycleTransaction> BeginTransactionAsync() =>
        new EventLifecycleTransaction(await context.Database.BeginTransactionAsync());

    public Task AcquireEventLockAsync(int eventId) =>
        ParticipantEventWorkflowLock.AcquireEventCapacityAsync(context, eventId);

    public void ClearTracking() => context.ChangeTracker.Clear();

    public Task<Event?> ReloadEventAsync(int eventId) =>
        context.Events.FirstOrDefaultAsync(eventItem => eventItem.Id == eventId);

    public Task<List<EventRegistration>> GetRegistrationsToVoidAsync(int eventId) =>
        context.EventRegistrations
            .Where(registration => registration.EventId == eventId &&
                (registration.Status == "Pending" || registration.Status == "Awaiting Payment" || registration.Status == "For Payment Verification"))
            .ToListAsync();

    public Task SaveChangesAsync() => context.SaveChangesAsync();

    private sealed class EventLifecycleTransaction(IDbContextTransaction transaction) : IEventLifecycleTransaction
    {
        public Task CommitAsync() => transaction.CommitAsync();
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
