using Microsoft.EntityFrameworkCore.Storage;

namespace TrailGuard.Services;

// Commit uncertainty is deliberately sticky until the database explicitly confirms rollback.
public sealed class UploadAttempt(IUploadStorage storage, ILogger logger) : IAsyncDisposable
{
    private readonly List<(UploadCategory Category, string Reference)> created = [];
    private bool persistenceStarted;
    private bool committed;
    private bool rolledBack;
    public async Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension)
    {
        var reference = await storage.UploadAsync(category, bytes, extension);
        created.Add((category, reference));
        return reference;
    }
    public void PersistenceStarted() => persistenceStarted = true;
    public void Committed() => committed = true;
    public void RollbackConfirmed() => rolledBack = true;
    public async ValueTask DisposeTransactionAsync(IDbContextTransaction transaction)
    {
        // Disposal is cleanup, not evidence of commit or rollback. Preserve the established outcome.
        try { await transaction.DisposeAsync(); }
        catch { logger.LogWarning("Upload transaction disposal failed; persistence outcome is unchanged."); }
    }
    public async Task DeleteReplacedAsync(UploadCategory category, string? reference)
    {
        if (!committed || reference == null || category == UploadCategory.Trails) return;
        await CleanupAsync(category, reference);
    }
    public async ValueTask DisposeAsync()
    {
        if (committed) return;
        if (persistenceStarted && !rolledBack)
        {
            foreach (var item in created)
                logger.LogWarning("Upload persistence outcome is uncertain; retain {Reference} for manual reconciliation.", item.Reference);
            return;
        }
        foreach (var item in created) await CleanupAsync(item.Category, item.Reference);
    }
    private async Task CleanupAsync(UploadCategory category, string reference)
    {
        try { await storage.DeleteOwnedAsync(category, reference); }
        catch { logger.LogWarning("Upload cleanup failed; retain {Reference} for manual reconciliation.", reference); }
    }
}
