namespace TrailGuard.Services;

public sealed record StoredDocument(byte[] Bytes, VerifiedFileType Type);

public interface IUploadStorage
{
    Task<string> UploadAsync(UploadCategory category, byte[] bytes, string extension, CancellationToken cancellationToken = default);
    Task<StoredDocument?> ReadDocumentAsync(RegistrationDocumentKind kind, string? reference, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadPublicLocalAsync(string reference, CancellationToken cancellationToken = default);
    Task DeleteOwnedAsync(UploadCategory category, string reference, CancellationToken cancellationToken = default);
}

public static class UploadBytes
{
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (result.Length + count > UploadStorageOptions.MaxFileBytes) throw new IOException("File exceeds 5 MiB.");
            await result.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return result.ToArray();
    }
}
