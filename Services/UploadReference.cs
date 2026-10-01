using System.Text.RegularExpressions;

namespace TrailGuard.Services;

public enum UploadCategory { Trails, Profiles, Receipts, MedicalClearances }
public sealed record UploadReference(string Value, string Key, UploadCategory Category, bool Cloud);

public sealed class UploadReferences(UploadStorageOptions options)
{
    public static string Folder(UploadCategory category) => category switch
    {
        UploadCategory.Trails => "trails", UploadCategory.Profiles => "profiles",
        UploadCategory.Receipts => "receipts", UploadCategory.MedicalClearances => "medical-clearances",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
    public static bool IsPublic(UploadCategory category) => category is UploadCategory.Trails or UploadCategory.Profiles;
    public string Bucket(UploadCategory category) => IsPublic(category) ? options.PublicBucket : options.PrivateBucket;
    private string Prefix(UploadCategory category, bool cloud) => cloud
        ? $"{options.Origin}/storage/v1/object/{(IsPublic(category) ? "public" : "authenticated")}/{Bucket(category)}/"
        : IsPublic(category) ? "/media/uploads/" : "local:v1/";

    public UploadReference Create(UploadCategory category, string extension)
    {
        var value = Prefix(category, options.Provider == "Supabase") + options.Namespace + "/" + Folder(category)
            + "/" + Guid.NewGuid().ToString("N") + extension;
        return Parse(value, category) ?? throw new InvalidOperationException("Invalid upload type.");
    }

    public UploadReference? Parse(string? value, UploadCategory category)
    {
        if (value == null || value.Length > (IsPublic(category) ? 300 : 512)) return null;
        foreach (var cloud in new[] { false, true })
        {
            if (cloud && options.Origin == null) continue;
            var prefix = Prefix(category, cloud);
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var key = value[prefix.Length..];
            var keyPrefix = options.Namespace + "/" + Folder(category) + "/";
            if (!key.StartsWith(keyPrefix, StringComparison.Ordinal)) continue;
            var file = key[keyPrefix.Length..];
            var extensions = IsPublic(category) ? "jpg|png" : "jpg|png|webp|pdf";
            if (!Regex.IsMatch(file, "^[a-f0-9]{32}\\.(" + extensions + ")\\z")) continue;
            return new(value, key, category, cloud);
        }
        return null;
    }

    public string LocalPath(UploadReference reference) => ConfinedPath(options.LocalRoot, reference.Key);

    public static string ConfinedPath(string root, string relative)
    {
        if (relative.Contains("..") || relative.Contains('\\') || relative.Contains('%') || relative.Contains(':')
            || relative.StartsWith('/') || relative.Contains('\0')) throw new IOException("Invalid storage path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid storage path.");
        // A confined lexical path must not escape through a local symlink/junction.
        for (var entry = path; entry != null; entry = Path.GetDirectoryName(entry))
            if ((File.Exists(entry) || Directory.Exists(entry)) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked storage paths are not supported.");
        return path;
    }
}
