using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace TrailGuard.Services;

public static class UploadPersistence
{
    public static async Task CommitAsync(DatabaseFacade database, UploadAttempt attempt, Func<Task> save)
    {
        var transaction = await database.BeginTransactionAsync();
        var commitStarted = false;
        try
        {
            attempt.PersistenceStarted();
            await save();
            commitStarted = true;
            await transaction.CommitAsync();
            attempt.Committed();
        }
        catch
        {
            await ConfirmRollbackAsync(transaction, attempt, commitStarted);
            throw;
        }
        finally
        {
            await attempt.DisposeTransactionAsync(transaction);
        }
    }

    public static async Task ConfirmRollbackAsync(IDbContextTransaction transaction, UploadAttempt attempt, bool commitStarted)
    {
        if (commitStarted) return; // A lost COMMIT acknowledgement is not proof of rollback.
        try { await transaction.RollbackAsync(); attempt.RollbackConfirmed(); }
        catch { /* Keep the primary error and retain files if rollback cannot be confirmed. */ }
    }
}
