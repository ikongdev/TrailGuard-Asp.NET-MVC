using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = "TrailGuard.EmailConfirmation.v1";
        TokenLifespan = TimeSpan.FromHours(24);
    }
}

public sealed class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger) where TUser : class;

public enum VerificationSendStatus { Sent, Cooldown, RateLimited, Unavailable, Failed, Ineligible }

public sealed record VerificationSendResult(VerificationSendStatus Status)
{
    public bool Accepted => Status == VerificationSendStatus.Sent;
}

public interface IEmailVerificationService
{
    Task<VerificationSendResult> SendConfirmationAsync(ApplicationUser user, string? remoteIp, CancellationToken cancellationToken = default);
    Task<VerificationSendResult> SendChangeConfirmationAsync(ApplicationUser user, string newEmail, string? remoteIp, CancellationToken cancellationToken = default);
}

public interface IEmailVerificationStateStore
{
    Task<bool> TryReserveAsync(ApplicationUser user, DateTime reservationUtc, TimeSpan cooldown, CancellationToken cancellationToken = default);
    Task ReleaseReservationAsync(ApplicationUser user, DateTime reservationUtc, CancellationToken cancellationToken = default);
}

// Deliberately avoids UserManager.UpdateAsync: that API refreshes Identity's
// security stamp and would invalidate a token generated for the same delivery.
public sealed class EmailVerificationStateStore(ApplicationDbContext context) : IEmailVerificationStateStore
{
    public async Task<bool> TryReserveAsync(ApplicationUser user, DateTime reservationUtc, TimeSpan cooldown, CancellationToken cancellationToken = default)
    {
        var cutoffUtc = reservationUtc - cooldown;
        var updated = await context.Users
            .Where(candidate => candidate.Id == user.Id &&
                (candidate.LastEmailVerificationSentAtUtc == null || candidate.LastEmailVerificationSentAtUtc <= cutoffUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.LastEmailVerificationSentAtUtc, reservationUtc), cancellationToken);

        if (updated != 1) return false;
        user.LastEmailVerificationSentAtUtc = reservationUtc;
        return true;
    }

    public async Task ReleaseReservationAsync(ApplicationUser user, DateTime reservationUtc, CancellationToken cancellationToken = default)
    {
        await context.Users
            .Where(candidate => candidate.Id == user.Id && candidate.LastEmailVerificationSentAtUtc == reservationUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.LastEmailVerificationSentAtUtc, (DateTime?)null), cancellationToken);
        if (user.LastEmailVerificationSentAtUtc == reservationUtc)
            user.LastEmailVerificationSentAtUtc = null;
    }
}

// The database timestamp enforces the cooldown across app instances. The bounded
// in-memory abuse counters reset on restart and are per-instance; deploy a shared
// rate limiter before scaling this app beyond one instance.
public sealed class EmailVerificationThrottle
{
    private const int MaxEntries = 2_000;
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    public EmailVerificationThrottle(TimeProvider clock) => _clock = clock;

    public bool TryConsume(string scope, string key, int limit, TimeSpan window)
    {
        var now = _clock.GetUtcNow();
        var combined = scope + ":" + key;
        if (_windows.Count > MaxEntries) Trim(now);
        var state = _windows.GetOrAdd(combined, _ => new Window(now, 0));
        lock (state)
        {
            if (now - state.Start >= window) { state.Start = now; state.Count = 0; }
            if (state.Count >= limit) return false;
            state.Count++;
            return true;
        }
    }

    private void Trim(DateTimeOffset now)
    {
        foreach (var item in _windows.Where(item => now - item.Value.Start > TimeSpan.FromHours(2)).Take(250))
            _windows.TryRemove(item.Key, out _);
    }

    private sealed class Window(DateTimeOffset start, int count)
    {
        public DateTimeOffset Start = start;
        public int Count = count;
    }
}

public sealed class EmailVerificationService : IEmailVerificationService
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _options;
    private readonly EmailVerificationThrottle _throttle;
    private readonly TimeProvider _clock;
    private readonly IEmailVerificationStateStore _state;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(UserManager<ApplicationUser> users, IEmailSender emailSender,
        IOptions<EmailOptions> options, EmailVerificationThrottle throttle, TimeProvider clock, IEmailVerificationStateStore state,
        IWebHostEnvironment environment, ILogger<EmailVerificationService> logger)
    {
        _users = users; _emailSender = emailSender; _options = options.Value; _throttle = throttle;
        _clock = clock; _state = state; _environment = environment; _logger = logger;
    }

    public Task<VerificationSendResult> SendConfirmationAsync(ApplicationUser user, string? remoteIp, CancellationToken cancellationToken = default) =>
        SendAsync(user, user.Email, remoteIp, false, cancellationToken);

    public Task<VerificationSendResult> SendChangeConfirmationAsync(ApplicationUser user, string newEmail, string? remoteIp, CancellationToken cancellationToken = default) =>
        SendAsync(user, newEmail, remoteIp, true, cancellationToken);

    private async Task<VerificationSendResult> SendAsync(ApplicationUser user, string? destination, string? remoteIp,
        bool changeEmail, CancellationToken cancellationToken)
    {
        if (!TrailGuardEmailAddress.TryNormalize(destination, out var normalizedDestination, out _) ||
            !_emailSender.IsAvailable || !TryGetPublicBaseUrl(out var baseUrl))
            return new(VerificationSendStatus.Unavailable);

        destination = normalizedDestination;

        var userKey = user.Id;
        var ipKey = string.IsNullOrWhiteSpace(remoteIp) ? "unknown" : remoteIp;
        if (!_throttle.TryConsume("account", userKey, 5, TimeSpan.FromMinutes(15)) ||
            !_throttle.TryConsume("ip", ipKey, 20, TimeSpan.FromHours(1)))
            return new(VerificationSendStatus.RateLimited);

        var now = _clock.GetUtcNow();
        try
        {
            if (!await _state.TryReserveAsync(user, now.UtcDateTime, ResendCooldown, cancellationToken))
                return new(VerificationSendStatus.Cooldown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email verification resend state could not be persisted.");
            return new(VerificationSendStatus.Failed);
        }

        var code = changeEmail
            ? await _users.GenerateChangeEmailTokenAsync(user, destination)
            : await _users.GenerateEmailConfirmationTokenAsync(user);
        // Identity tokens are opaque binary-safe strings. Base64url avoids any
        // ambiguity from query-string intermediaries while retaining URL encoding.
        var encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var url = changeEmail
            ? BuildUrl(baseUrl!, "/Account/ConfirmEmailChange", new Dictionary<string, string?>
            {
                ["userId"] = user.Id, ["email"] = destination, ["code"] = encodedCode
            })
            : BuildUrl(baseUrl!, "/Account/ConfirmEmail", new Dictionary<string, string?>
            {
                ["userId"] = user.Id, ["email"] = destination, ["code"] = encodedCode
            });

        var subject = changeEmail ? "Confirm your new TrailGuard email" : "Verify your TrailGuard email";
        var text = $"Use this link to { (changeEmail ? "confirm your new email address" : "verify your email address") }: {url}\n\nThis link expires in 24 hours.";
        var html = $"<p>Use the link below to {(changeEmail ? "confirm your new email address" : "verify your email address")}.</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">Continue to TrailGuard</a></p><p>This link expires in 24 hours.</p>";
        var delivery = await _emailSender.SendAsync(new EmailMessage(destination, subject, text, html), cancellationToken);
        if (!delivery.Accepted)
        {
            try { await _state.ReleaseReservationAsync(user, now.UtcDateTime, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed email verification cooldown could not be cleared."); }
            _logger.LogWarning("Email verification delivery was not accepted ({Status}).", delivery.SafeFailure ?? "unknown");
            return new(VerificationSendStatus.Failed);
        }

        return new(VerificationSendStatus.Sent);
    }

    private bool TryGetPublicBaseUrl(out Uri? result)
    {
        result = null;
        if (!Uri.TryCreate(_options.PublicBaseUrl, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(_environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            return false;
        result = uri;
        return true;
    }

    private static string BuildUrl(Uri baseUrl, string path, Dictionary<string, string?> values)
    {
        var prefix = baseUrl.GetLeftPart(UriPartial.Authority) + baseUrl.AbsolutePath.TrimEnd('/');
        return QueryHelpers.AddQueryString(prefix + path, values);
    }
}
