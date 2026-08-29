using Microsoft.AspNetCore.Http;

namespace TrailGuard.Services
{
    // Upload-time counterpart to DocumentStorageResolver's serve-time check -
    // both call into the same DocumentFileSignature allow-list/sniffing logic,
    // so a file can never be accepted at upload and then refused at serve time
    // (or vice versa) because the two used different rules. IFormFile.ContentType
    // is never consulted - it's a client-supplied header, not a property of the
    // actual bytes.
    public static class DocumentUploadValidator
    {
        // Returns the verified type on success so callers can derive a canonical
        // stored extension (see DocumentFileSignature.SafeExtensionFor) directly
        // from the same result that passed validation - never from the client's
        // original filename, and never a second, independent mapping in the
        // controller. Null means "reject": either the declared extension isn't
        // recognized, or the actual bytes don't match what that extension claims.
        public static async Task<VerifiedFileType?> ValidateAsync(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName);
            if (!DocumentFileSignature.TryGetExpectedTypeForExtension(extension, out var expectedType))
                return null;

            VerifiedFileType sniffedType;
            await using (var stream = file.OpenReadStream())
            {
                sniffedType = await DocumentFileSignature.SniffAsync(stream);
            }

            if (sniffedType != expectedType || !DocumentFileSignature.IsAllowedType(sniffedType))
                return null;

            return sniffedType;
        }

        // Server-generated filename only - the client's original filename (available
        // in file.FileName) is never used to build a physical path, and is never
        // otherwise persisted or returned to any client. Guid.NewGuid("N") alone is
        // already collision-safe in practice, but the existence check below is kept
        // anyway so this can never silently overwrite a file it didn't create.
        public static string GenerateStoredFileName(string uploadsFolder, VerifiedFileType type)
        {
            string fileName;
            string filePath;
            do
            {
                fileName = Guid.NewGuid().ToString("N") + DocumentFileSignature.SafeExtensionFor(type);
                filePath = Path.Combine(uploadsFolder, fileName);
            } while (File.Exists(filePath));

            return fileName;
        }
    }
}
