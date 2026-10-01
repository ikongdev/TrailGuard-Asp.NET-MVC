namespace TrailGuard.Services;

public sealed class UploadStorage(UploadStorageOptions options, UploadReferences references,
    SupabaseFileStore cloud, ILogger<UploadStorage> logger) : IUploadStorage
{
    public async Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0 || bytes.Length > UploadStorageOptions.MaxFileBytes) throw new IOException("File must be 1 byte to 5 MiB.");
        if (!DocumentFileSignature.TryGetExpectedTypeForExtension(extension, out var expected)
            || DocumentFileSignature.Sniff(bytes, Math.Min(12, bytes.Length)) != expected)
            throw new IOException("Invalid file type.");
        var reference = references.Create(category, extension);
        if (reference.Cloud)
        {
            try { await cloud.UploadAsync(reference, bytes, DocumentFileSignature.ContentTypeFor(expected), cancellationToken); }
            catch
            {
                // The server may have accepted a timed-out request. No DB reference has been saved yet.
                logger.LogWarning("Cloud upload failed; check object {ObjectKey} during manual reconciliation.", reference.Key);
                throw;
            }
        }
        else
        {
            var path = references.LocalPath(reference);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var created = false;
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                created = true;
                await stream.WriteAsync(bytes, cancellationToken);
            }
            catch
            {
                if (created) await TryDeleteAsync(category, reference.Value);
                throw;
            }
        }
        return reference.Value;
    }

    public async Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default)
    {
        var category = kind == RegistrationDocumentKind.Receipt ? UploadCategory.Receipts : UploadCategory.MedicalClearances;
        try
        {
            var owned = references.Parse(reference, category);
            byte[]? bytes;
            if (owned != null)
                bytes = owned.Cloud ? await cloud.ReadAsync(owned, cancellationToken) : await ReadLocalAsync(references.LocalPath(owned), cancellationToken);
            else
            {
                // Read-only compatibility: legacy references can never reach cloud requests or deletion.
                if (reference == null) return null;
                var path = UploadReferences.ConfinedPath(options.WebRoot, reference.TrimStart('/'));
                var legacy = await DocumentStorageResolver.TryResolveAsync(options.WebRoot, kind, reference);
                if (legacy == null) return null;
                bytes = await ReadLocalAsync(path, cancellationToken);
            }
            if (bytes == null || !DocumentFileSignature.TryGetExpectedTypeForExtension(Path.GetExtension(reference), out var expected)) return null;
            var actual = DocumentFileSignature.Sniff(bytes, Math.Min(bytes.Length, 12));
            return actual == expected ? new(bytes, actual) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("Document storage unavailable ({ErrorType}).", ex.GetType().Name);
            return null;
        }
    }

    public async Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default)
    {
        var owned = references.Parse(reference, UploadCategory.Trails) ?? references.Parse(reference, UploadCategory.Profiles);
        if (owned == null || owned.Cloud) return null;
        try { return await ReadLocalAsync(references.LocalPath(owned), cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static async Task<byte[]?> ReadLocalAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await UploadBytes.ReadAsync(stream, cancellationToken);
    }

    public async Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default)
    {
        var owned = references.Parse(reference, category);
        if (owned == null) return; // Never delete legacy/local sample assets or other datasets.
        if (owned.Cloud) await cloud.DeleteAsync(owned, cancellationToken);
        else File.Delete(references.LocalPath(owned));
    }

    private async Task TryDeleteAsync(UploadCategory category, string reference)
    {
        try { await DeleteOwnedAsync(category, reference); }
        catch { logger.LogWarning("Failed upload cleanup retained an object for manual reconciliation."); }
    }
}
