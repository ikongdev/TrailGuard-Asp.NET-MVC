using Microsoft.AspNetCore.Http;

namespace TrailGuard.Services
{






    public static class DocumentUploadValidator
    {






        public static async Task<VerifiedFileType?> ValidateAsync(IFormFile file)
        {
            if (file.Length <= 0 || file.Length > UploadStorageOptions.MaxFileBytes) return null;
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
    }
}
