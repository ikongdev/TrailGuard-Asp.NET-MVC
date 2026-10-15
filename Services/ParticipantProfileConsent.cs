using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TrailGuard.Services;

/// <summary>
/// The complete server-owned participant profile consent notice. Razor renders this
/// object directly, and its fingerprint is the value persisted with an acceptance.
/// </summary>
public sealed record ParticipantProfileConsentNotice(
    string Title,
    string Description,
    IReadOnlyList<string> Statements,
    string PrivacyPolicyPrompt,
    string PrivacyPolicyController,
    string PrivacyPolicyAction,
    string PrivacyPolicyLinkText,
    string PrivacyPolicyLinkTarget,
    string PrivacyPolicyLinkRel,
    string PrivacyPolicyTrailingText,
    string DeclineMessage,
    string AffirmationLabel,
    string AcceptedMessage)
{
    /// <summary>Stable identity of the exact notice content and notice-link target.</summary>
    public string Version => ParticipantProfileConsent.CreateVersion(this);
}

/// <summary>
/// Server-owned profile notice contract. Any material wording or notice-link change
/// creates a new acceptance identity, so prior acceptances cannot be silently reused.
/// </summary>
public static class ParticipantProfileConsent
{
    private const string VersionPrefix = "profile-privacy-v1-sha256-";

    public static ParticipantProfileConsentNotice CurrentNotice { get; } = new(
        Title: "Profile Privacy Notice",
        Description: "Please review this notice before saving participant profile information.",
        Statements: Array.AsReadOnly(
        [
            "TrailGuard stores the profile information you provide, such as your name, email address, phone number, profile photo, birthday, gender, Facebook link, and bio.",
            "It is used to manage your account, support participant eligibility and contact workflows, and operate and secure the system.",
            "Authorized organizers and system roles can access information only through the event workflows available to them. Your profile information is not an active suitability-model input."
        ]),
        PrivacyPolicyPrompt: "Read the ",
        PrivacyPolicyController: "Home",
        PrivacyPolicyAction: "Privacy",
        PrivacyPolicyLinkText: "full Privacy Policy and privacy-rights information",
        PrivacyPolicyLinkTarget: "_blank",
        PrivacyPolicyLinkRel: "noopener noreferrer",
        PrivacyPolicyTrailingText: ".",
        DeclineMessage: "If you decline or close this notice, profile changes will not be saved. Account security and password actions remain available.",
        AffirmationLabel: "I have read this notice and explicitly agree to this version for my participant profile.",
        AcceptedMessage: "This version is already recorded as accepted.");

    /// <summary>Version persisted with an affirmative acceptance of <see cref="CurrentNotice"/>.</summary>
    public static string CurrentVersion => CurrentNotice.Version;

    public static bool IsCurrent(string? version) =>
        string.Equals(version, CurrentVersion, StringComparison.Ordinal);

    /// <summary>
    /// Produces an invariant, line-ending-normalized identity for a complete notice.
    /// This is public so focused regression checks can prove a changed notice is stale.
    /// </summary>
    public static string CreateVersion(ParticipantProfileConsentNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        ArgumentNullException.ThrowIfNull(notice.Statements);
        var canonicalFields = new List<string>
        {
            CanonicalField("title", notice.Title),
            CanonicalField("description", notice.Description),
            CanonicalField("statement-count", notice.Statements.Count.ToString(CultureInfo.InvariantCulture))
        };
        canonicalFields.AddRange(notice.Statements.Select((statement, index) => CanonicalField($"statement-{index}", statement)));
        canonicalFields.AddRange(
        [
            CanonicalField("privacy-prompt", notice.PrivacyPolicyPrompt),
            CanonicalField("privacy-controller", notice.PrivacyPolicyController),
            CanonicalField("privacy-action", notice.PrivacyPolicyAction),
            CanonicalField("privacy-link-text", notice.PrivacyPolicyLinkText),
            CanonicalField("privacy-link-target", notice.PrivacyPolicyLinkTarget),
            CanonicalField("privacy-link-rel", notice.PrivacyPolicyLinkRel),
            CanonicalField("privacy-trailing-text", notice.PrivacyPolicyTrailingText),
            CanonicalField("decline-message", notice.DeclineMessage),
            CanonicalField("affirmation-label", notice.AffirmationLabel),
            CanonicalField("accepted-message", notice.AcceptedMessage)
        ]);
        var canonical = string.Join("\n", canonicalFields);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return VersionPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CanonicalField(string name, string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return string.Concat(name, "=", normalized.Length.ToString(CultureInfo.InvariantCulture), ":", normalized);
    }
}
