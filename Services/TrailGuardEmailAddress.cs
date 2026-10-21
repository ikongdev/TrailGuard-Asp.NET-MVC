using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace TrailGuard.Services;

public static class TrailGuardEmailAddress
{
    public const int MaximumLength = 256;
    public const string RequiredMessage = "Email address is required.";
    public const string InvalidMessage = "Enter a valid email address, for example name@example.com.";

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            error = RequiredMessage;
            return false;
        }

        if (normalized.Length > MaximumLength || normalized.Any(char.IsWhiteSpace) || normalized.Any(char.IsControl))
        {
            error = InvalidMessage;
            return false;
        }

        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1)
        {
            error = InvalidMessage;
            return false;
        }

        var domain = normalized[(at + 1)..];
        if (!domain.Contains('.', StringComparison.Ordinal) || domain.StartsWith(".", StringComparison.Ordinal) ||
            domain.EndsWith(".", StringComparison.Ordinal) || Uri.CheckHostName(domain) != UriHostNameType.Dns ||
            !HasNormalPublicDomain(domain))
        {
            error = InvalidMessage;
            return false;
        }

        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.Ordinal) || !string.IsNullOrEmpty(parsed.DisplayName))
            {
                error = InvalidMessage;
                return false;
            }
        }
        catch (FormatException)
        {
            error = InvalidMessage;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool HasNormalPublicDomain(string domain)
    {
        var labels = domain.Split('.');
        if (labels[^1].Length is < 2 or > 63 || !labels[^1].All(char.IsLetter)) return false;
        return labels.All(label => label.Length is > 0 and <= 63 && label[0] != '-' && label[^1] != '-' &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class TrailGuardEmailAddressAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        TrailGuardEmailAddress.TryNormalize(value as string, out _, out var error)
            ? ValidationResult.Success
            : new ValidationResult(error, [validationContext.MemberName!]);
}
