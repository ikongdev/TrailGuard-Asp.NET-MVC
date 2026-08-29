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

    // Server-authoritative resolver for the two kinds of registration document
    // this app stores (payment receipts, medical clearances). This is the ONLY
    // place that turns a stored EventRegistration.PaymentReceiptUrl /
    // MedicalClearanceUrl string into an actual file on disk - Razor views never
    // do this resolution themselves, they only display the boolean/kind results
    // a controller already obtained from here. Supersedes the earlier
    // DocumentPathValidator, which only checked the string shape and left actual
    // file-content verification and physical-path containment undone.
    public static class DocumentStorageResolver
    {
        // The exact two folders RegistrationController's uploads write to
        // (Register's medical-clearance upload, UpdatePaymentReceipt) - nothing
        // else in the app writes under wwwroot/uploads.
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

        // Stage 1: string-shape and path-containment checks only - no file I/O.
        // Every failure returns null identically; callers must never distinguish
        // *why* resolution failed (see DocumentsController - "missing", "invalid
        // path", and "wrong folder" are all the same generic outcome to a client).
        private static string? ResolvePhysicalPath(string webRootPath, RegistrationDocumentKind kind, string? storedUrl)
        {
            if (string.IsNullOrWhiteSpace(storedUrl)) return null;
            if (storedUrl.Contains('\0')) return null;
            if (storedUrl.Contains("..", StringComparison.Ordinal)) return null;
            if (storedUrl.Contains('\\')) return null;
            if (storedUrl.StartsWith("//", StringComparison.Ordinal)) return null; // protocol-relative
            if (!storedUrl.StartsWith('/')) return null; // must be app-relative, never scheme-qualified
            // This app never writes a percent-encoded character into these URLs (see
            // RegistrationController's upload actions) - a literal '%' is therefore
            // either an encoded-traversal attempt or a value nothing here produced,
            // and is rejected rather than decoded and re-checked.
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

            // Trailing-separator-safe containment check - a naive StartsWith(canonicalDirectory)
            // without the separator would also match a sibling folder like
            // "uploads/receipts-evil/...", which shares the same string prefix but is
            // not actually inside the approved directory.
            if (!candidateFullPath.StartsWith(canonicalDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase))
                return null;

            return candidateFullPath;
        }

        // Stage 2: full resolution including file-content verification. Returns
        // null for anything that fails ANY check - missing file, wrong folder,
        // traversal, unreadable, unrecognized signature, or a signature that
        // doesn't match what the stored extension claims. A file that predates
        // this validation (e.g. an old upload with a since-disallowed format)
        // fails here exactly like a newly-tampered one - it is never served
        // merely because its stored extension looks safe.
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
