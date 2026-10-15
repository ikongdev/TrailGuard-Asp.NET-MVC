using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace TrailGuard.Services;

public sealed record ValidatedTrailImage(byte[] Bytes, string Extension);

public static class TrailImageUploadValidator
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    // A byte-size limit alone cannot bound the memory needed for a highly
    // compressed image. This cap is a decoding-resource limit, not a trail rule.
    public const long MaxImagePixels = 40_000_000;

    public static async Task<(ValidatedTrailImage? Image, string? Error)> ValidateAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return (null, "Select an image file.");
        }

        if (file.Length > MaxImageBytes)
        {
            return (null, "Image files must be 5MB or smaller.");
        }

        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            return (null, "Image files cannot be empty.");
        }

        if (bytes.Length > MaxImageBytes)
        {
            return (null, "Image files must be 5MB or smaller.");
        }

        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec == null)
            {
                return (null, "Image files must be valid JPG or PNG images.");
            }

            var extension = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => ".jpg",
                SKEncodedImageFormat.Png => ".png",
                _ => null
            };

            if (extension == null)
            {
                return (null, "Image files must be valid JPG or PNG images.");
            }

            var imageInfo = codec.Info;
            if (imageInfo.Width <= 0 || imageInfo.Height <= 0
                || (long)imageInfo.Width * imageInfo.Height > MaxImagePixels)
            {
                return (null, "Image dimensions are too large.");
            }

            using var bitmap = new SKBitmap();
            if (!bitmap.TryAllocPixels(imageInfo)
                || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                return (null, "Image files must be valid JPG or PNG images.");
            }

            return (new ValidatedTrailImage(bytes, extension), null);
        }
        catch (ArgumentException)
        {
            return (null, "Image files must be valid JPG or PNG images.");
        }
    }
}
