using Microsoft.AspNetCore.Http;

namespace TrailGuard.Services
{






    public static class DocumentUploadValidator
    {






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
