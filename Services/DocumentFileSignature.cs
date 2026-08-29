namespace TrailGuard.Services
{
    public enum VerifiedFileType
    {
        Unknown,
        Jpeg,
        Png,
        Webp,
        Pdf
    }

    // Single shared "what kind of file is this, really" check - used both when a
    // receipt/medical-clearance upload is accepted (RegistrationController) and
    // when a previously-stored document is served back (DocumentsController).
    // Extension alone is never trusted: a stored/declared extension is only ever
    // accepted after the actual bytes are sniffed and found to match. This is
    // deliberately the same check on both paths so a file that was somehow saved
    // with a mismatched extension before this validation existed can never be
    // served just because its stored name looks safe.
    public static class DocumentFileSignature
    {
        // Both payment receipts and medical clearances are uploaded through forms
        // that declare accept="image/*,.pdf" (Views/Registration/MyRegistrations.cshtml
        // and Views/Registration/Register.cshtml) - so both document kinds share the
        // same allowed set. SVG is deliberately excluded (it can carry script) even
        // though it's technically an image format.
        public static bool IsAllowedType(VerifiedFileType type) =>
            type is VerifiedFileType.Jpeg or VerifiedFileType.Png or VerifiedFileType.Webp or VerifiedFileType.Pdf;

        public static bool IsImageType(VerifiedFileType type) =>
            type is VerifiedFileType.Jpeg or VerifiedFileType.Png or VerifiedFileType.Webp;

        public static string ContentTypeFor(VerifiedFileType type) => type switch
        {
            VerifiedFileType.Jpeg => "image/jpeg",
            VerifiedFileType.Png => "image/png",
            VerifiedFileType.Webp => "image/webp",
            VerifiedFileType.Pdf => "application/pdf",
            _ => "application/octet-stream"
        };

        public static string SafeExtensionFor(VerifiedFileType type) => type switch
        {
            VerifiedFileType.Jpeg => ".jpg",
            VerifiedFileType.Png => ".png",
            VerifiedFileType.Webp => ".webp",
            VerifiedFileType.Pdf => ".pdf",
            _ => ""
        };

        // The declared extension is checked first (cheap, rejects .svg/.html/.js/etc.
        // before any file content is even read) - this only says which family of
        // magic bytes the actual content must then match.
        public static bool TryGetExpectedTypeForExtension(string? extension, out VerifiedFileType expected)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".jfif":
                    // .jfif is a JPEG variant extension (same FF D8 FF signature) -
                    // some browsers/cameras produce it when saving a JPEG. Still
                    // requires the real JPEG magic bytes below; a .jfif file with
                    // non-JPEG content is rejected exactly like a mislabeled .jpg.
                    expected = VerifiedFileType.Jpeg;
                    return true;
                case ".png":
                    expected = VerifiedFileType.Png;
                    return true;
                case ".webp":
                    expected = VerifiedFileType.Webp;
                    return true;
                case ".pdf":
                    expected = VerifiedFileType.Pdf;
                    return true;
                default:
                    expected = VerifiedFileType.Unknown;
                    return false;
            }
        }

        // Reads only the leading bytes actually needed to identify these four
        // formats (WEBP's RIFF/WEBP markers are the longest, at 12 bytes) - never
        // the whole file, so this is cheap to run on every document-availability
        // check as well as on every upload and every serve.
        public static async Task<VerifiedFileType> SniffAsync(Stream stream)
        {
            var header = new byte[12];
            var totalRead = 0;
            while (totalRead < header.Length)
            {
                var read = await stream.ReadAsync(header.AsMemory(totalRead, header.Length - totalRead));
                if (read == 0) break;
                totalRead += read;
            }

            return Sniff(header, totalRead);
        }

        public static VerifiedFileType Sniff(byte[] header, int length)
        {
            if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return VerifiedFileType.Jpeg;

            if (length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return VerifiedFileType.Png;

            if (length >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
                return VerifiedFileType.Webp;

            if (length >= 5 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F' && header[4] == (byte)'-')
                return VerifiedFileType.Pdf;

            return VerifiedFileType.Unknown;
        }
    }
}
