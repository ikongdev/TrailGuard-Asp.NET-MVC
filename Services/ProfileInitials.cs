namespace TrailGuard.Services;

public static class ProfileInitials
{
    public static string FromNames(string? firstName, string? lastName, string? displayedName)
    {
        var firstInitial = Initial(firstName);
        var lastInitial = Initial(lastName);

        if (firstInitial is not null && lastInitial is not null)
        {
            return (firstInitial + lastInitial).ToUpperInvariant();
        }

        var displayedInitials = FromDisplayedName(displayedName);
        if (displayedInitials != "?")
        {
            return displayedInitials;
        }

        return (firstInitial ?? lastInitial ?? "?").ToUpperInvariant();
    }

    private static string FromDisplayedName(string? displayedName)
    {
        var nameParts = (displayedName ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (nameParts.Length == 0)
        {
            return "?";
        }

        var firstInitial = Initial(nameParts[0]);
        var lastInitial = nameParts.Length > 1 ? Initial(nameParts[^1]) : null;
        return (firstInitial + lastInitial).ToUpperInvariant();
    }

    private static string? Initial(string? name)
    {
        var trimmedName = name?.Trim();
        return string.IsNullOrEmpty(trimmedName) ? null : trimmedName[0].ToString();
    }
}
