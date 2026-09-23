namespace TrailGuard.Services
{
    public enum RegistrationDocumentKind
    {
        Receipt,
        Clearance
    }

    public class ResolvedDocument
    {
        public string PhysicalPath { get; set; } = string.Empty;
        public VerifiedFileType Type { get; set; }
    }









    public static class DocumentStorageResolver
    {



        private static readonly Dictionary<RegistrationDocumentKind, string> RelativeFolder = new()
        {
            { RegistrationDocumentKind.Receipt, "uploads/receipts" },
            { RegistrationDocumentKind.Clearance, "uploads/medical-clearances" }
        };

        public static bool TryParseKind(string? kind, out RegistrationDocumentKind parsed)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "receipt":
                    parsed = RegistrationDocumentKind.Receipt;
                    return true;
                case "clearance":
                    parsed = RegistrationDocumentKind.Clearance;
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }





        private static string? ResolvePhysicalPath(string webRootPath, RegistrationDocumentKind kind, string? storedUrl)
        {
            if (string.IsNullOrWhiteSpace(storedUrl)) return null;
            if (storedUrl.Contains('\0')) return null;
            if (storedUrl.Contains("..", StringComparison.Ordinal)) return null;
            if (storedUrl.Contains('\\')) return null;
            if (storedUrl.StartsWith("//", StringComparison.Ordinal)) return null;
            if (!storedUrl.StartsWith('/')) return null;




            if (storedUrl.Contains('%')) return null;

            var expectedPrefix = "/" + RelativeFolder[kind] + "/";
            if (!storedUrl.StartsWith(expectedPrefix, StringComparison.Ordinal)) return null;
            if (storedUrl.Length <= expectedPrefix.Length) return null;

            string candidateFullPath;
            string canonicalDirectory;
            try
            {
                candidateFullPath = Path.GetFullPath(Path.Combine(webRootPath, storedUrl.TrimStart('/')));
                canonicalDirectory = Path.GetFullPath(Path.Combine(webRootPath, RelativeFolder[kind]));
            }
            catch
            {
                return null;
            }

            var canonicalDirectoryWithSeparator = canonicalDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? canonicalDirectory
                : canonicalDirectory + Path.DirectorySeparatorChar;





            if (!candidateFullPath.StartsWith(canonicalDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase))
                return null;

            return candidateFullPath;
        }








        public static async Task<ResolvedDocument?> TryResolveAsync(string webRootPath, RegistrationDocumentKind kind, string? storedUrl)
        {
            var physicalPath = ResolvePhysicalPath(webRootPath, kind, storedUrl);
            if (physicalPath == null) return null;
            if (!File.Exists(physicalPath)) return null;

            var extension = Path.GetExtension(physicalPath);
            if (!DocumentFileSignature.TryGetExpectedTypeForExtension(extension, out var expectedType))
                return null;

            VerifiedFileType sniffedType;
            try
            {
                await using var stream = File.OpenRead(physicalPath);
                sniffedType = await DocumentFileSignature.SniffAsync(stream);
            }
            catch
            {
                return null;
            }

            if (sniffedType != expectedType) return null;
            if (!DocumentFileSignature.IsAllowedType(sniffedType)) return null;

            return new ResolvedDocument { PhysicalPath = physicalPath, Type = sniffedType };
        }
    }
}
