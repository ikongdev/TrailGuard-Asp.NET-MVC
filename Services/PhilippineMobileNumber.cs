namespace TrailGuard.Services;

/// <summary>
/// Parses the registration form's Philippine mobile-number formats without guessing,
/// truncating, or accepting non-ASCII digits. Registration records use its canonical
/// presentation so the two contact fields are stored consistently.
/// </summary>
public static class PhilippineMobileNumber
{
    public const string LocalExample = "912 345 6789";

    public static bool TryNormalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        var hasPlusPrefix = text.StartsWith('+');
        var start = hasPlusPrefix ? 1 : 0;
        if (start == text.Length) return false;

        var digits = new System.Text.StringBuilder(text.Length);
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= '0' and <= '9')
            {
                digits.Append(character);
            }
            else if (character is ' ' or '-')
            {
                continue;
            }
            else
            {
                return false;
            }
        }

        var normalizedDigits = digits.ToString();
        string localDigits;
        if (hasPlusPrefix)
        {
            if (normalizedDigits.Length != 12 || !normalizedDigits.StartsWith("63", StringComparison.Ordinal)) return false;
            localDigits = normalizedDigits[2..];
        }
        else if (normalizedDigits.Length == 10)
        {
            localDigits = normalizedDigits;
        }
        else if (normalizedDigits.Length == 11 && normalizedDigits[0] == '0')
        {
            localDigits = normalizedDigits[1..];
        }
        else if (normalizedDigits.Length == 12 && normalizedDigits.StartsWith("63", StringComparison.Ordinal))
        {
            localDigits = normalizedDigits[2..];
        }
        else
        {
            return false;
        }

        if (localDigits.Length != 10 || localDigits[0] != '9') return false;

        canonical = $"+63 {localDigits[..3]} {localDigits[3..6]} {localDigits[6..]}";
        return true;
    }

    public static string FormatLocalDigits(string localDigits)
    {
        if (localDigits.Length > 6) return $"{localDigits[..3]} {localDigits[3..6]} {localDigits[6..]}";
        if (localDigits.Length > 3) return $"{localDigits[..3]} {localDigits[3..]}";
        return localDigits;
    }

    public static string ToLocalDisplay(string canonical)
    {
        const string prefix = "+63 ";
        return canonical.StartsWith(prefix, StringComparison.Ordinal)
            ? canonical[prefix.Length..]
            : canonical;
    }
}
