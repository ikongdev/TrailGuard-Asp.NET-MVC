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









    public static class DocumentFileSignature
    {





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




        public static bool TryGetExpectedTypeForExtension(string? extension, out VerifiedFileType expected)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".jfif":




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
