using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;

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
            await using var imageStream = new MemoryStream(bytes);
            var imageInfo = await Image.IdentifyAsync(imageStream);
            if ((long)imageInfo.Width * imageInfo.Height > MaxImagePixels)
            {
                return (null, "Image dimensions are too large.");
            }

            imageStream.Position = 0;
            using var image = await Image.LoadAsync(imageStream);
            var extension = image.Metadata.DecodedImageFormat?.Name switch
            {
                "JPEG" => ".jpg",
                "PNG" => ".png",
                _ => null
            };

            return extension == null
                ? (null, "Image files must be valid JPG or PNG images.")
                : (new ValidatedTrailImage(bytes, extension), null);
        }
        catch (ImageFormatException)
        {
            return (null, "Image files must be valid JPG or PNG images.");
        }
    }
}
