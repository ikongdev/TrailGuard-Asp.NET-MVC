namespace TrailGuard.Services;

public sealed class PublicImageDisplay(UploadStorageOptions options, UploadReferences references)
{
    // Returning null activates each view's existing initials/no-image markup. Never rewrite stored data.
    public string? Url(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var owned = references.Parse(reference, UploadCategory.Trails) ?? references.Parse(reference, UploadCategory.Profiles);
        try
        {
            if (owned != null) return owned.Cloud || File.Exists(references.LocalPath(owned)) ? reference : null;
            if (!reference.StartsWith("/images/trails/", StringComparison.Ordinal)
                && !reference.StartsWith("/images/profiles/", StringComparison.Ordinal)) return null;
            return File.Exists(UploadReferences.ConfinedPath(options.WebRoot, reference[1..])) ? reference : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
