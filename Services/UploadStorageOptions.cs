using System.Text.RegularExpressions;

namespace TrailGuard.Services;

public sealed class UploadStorageOptions
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const long MaxRequestBytes = 50 * 1024 * 1024; // Nine 5 MiB images plus multipart overhead.
    public string Provider { get; init; } = "Local";
    public required string Namespace { get; init; }
    public required string LocalRoot { get; init; }
    public required string WebRoot { get; init; }
    public string? Origin { get; init; }
    public string? SecretKey { get; init; }
    public string PublicBucket { get; init; } = "trailguard-public-images";
    public string PrivateBucket { get; init; } = "trailguard-private-documents";
    public int TimeoutSeconds { get; init; } = 20;

    public static UploadStorageOptions Resolve(IConfiguration config, IWebHostEnvironment environment)
    {
        var provider = config["Storage:Provider"] ?? "Local";
        if (provider != "Local" && provider != "Supabase") Fail("Storage:Provider");
        var ns = config["Storage:Namespace"];
        if (ns == null || !Regex.IsMatch(ns, "^[a-z0-9][a-z0-9-]{0,31}\\z")) Fail("Storage:Namespace");
        var origin = config["Storage:Supabase:Url"];
        if (origin != null)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != ""
                || uri.AbsolutePath != "/" || uri.HostNameType != UriHostNameType.Dns)
                Fail("Storage:Supabase:Url");
            origin = uri!.GetLeftPart(UriPartial.Authority);
        }
        var key = config["Storage:Supabase:SecretKey"];
        if (provider == "Supabase" && (origin == null || key == null
            || !Regex.IsMatch(key, "^sb_secret_[A-Za-z0-9_-]+\\z")))
            Fail("Storage:Supabase:Url / Storage:Supabase:SecretKey");
        var publicBucket = config["Storage:Supabase:PublicImagesBucket"] ?? "trailguard-public-images";
        var privateBucket = config["Storage:Supabase:PrivateDocumentsBucket"] ?? "trailguard-private-documents";
        if (publicBucket != "trailguard-public-images" || privateBucket != "trailguard-private-documents")
            Fail("Storage:Supabase:PublicImagesBucket / Storage:Supabase:PrivateDocumentsBucket");
        var timeout = 20;
        if (config["Storage:Supabase:TimeoutSeconds"] is string raw
            && (!int.TryParse(raw, out timeout) || timeout < 1 || timeout > 60)) Fail("Storage:Supabase:TimeoutSeconds");
        string root;
        try { root = Path.GetFullPath(config["Storage:Local:RootPath"] ?? "App_Data/uploads", environment.ContentRootPath); }
        catch { throw new InvalidOperationException("Invalid Storage:Local:RootPath."); }
        var webRoot = Path.GetFullPath(environment.WebRootPath);
        root = root.TrimEnd(Path.DirectorySeparatorChar);
        webRoot = webRoot.TrimEnd(Path.DirectorySeparatorChar);
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || webRoot.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Fail("Storage:Local:RootPath (must be outside wwwroot)");
        // Public references are copied verbatim into Event.TrailThumbnailUrlSnapshot (varchar(300)).
        if (origin != null && $"{origin}/storage/v1/object/public/{publicBucket}/{ns}/profiles/{new string('a', 32)}.png".Length > 300)
            Fail("Storage:Supabase:Url (generated image reference exceeds 300 characters)");
        return new() { Provider = provider, Namespace = ns!, LocalRoot = root, WebRoot = webRoot,
            Origin = origin, SecretKey = key, PublicBucket = publicBucket, PrivateBucket = privateBucket,
            TimeoutSeconds = timeout };
    }

    private static void Fail(string name) => throw new InvalidOperationException($"Invalid or missing {name} configuration.");
}
