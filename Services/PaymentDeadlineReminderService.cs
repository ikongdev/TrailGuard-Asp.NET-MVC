using System.Net;
using Microsoft.Extensions.Options;
using TrailGuard.Models;

namespace TrailGuard.Services;

public sealed class PaymentReminderOptions
{
    public bool Enabled { get; set; }
    public int PollingIntervalMinutes { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int LeaseMinutes { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan PollingInterval => TimeSpan.FromMinutes(PollingIntervalMinutes);
    public TimeSpan LeaseDuration => TimeSpan.FromMinutes(LeaseMinutes);

    public void Validate()
    {
        if (PollingIntervalMinutes is < 1 or > 60) throw new InvalidOperationException("PaymentReminders:PollingIntervalMinutes must be between 1 and 60.");
        if (BatchSize is < 1 or > 200) throw new InvalidOperationException("PaymentReminders:BatchSize must be between 1 and 200.");
        if (LeaseMinutes is < 1 or > 60) throw new InvalidOperationException("PaymentReminders:LeaseMinutes must be between 1 and 60.");
        if (MaxAttempts is < 1 or > 5) throw new InvalidOperationException("PaymentReminders:MaxAttempts must be between 1 and 5.");
    }
}

public static class PhilippineWallClock
{
    private static readonly TimeZoneInfo Zone = Resolve();
    public static DateTime Now(TimeProvider clock) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime, DateTimeKind.Unspecified);
    public static DateTimeOffset ToUtc(DateTime wallClock) => new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), Zone.GetUtcOffset(wallClock)).ToUniversalTime();
    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        throw new InvalidOperationException("The Asia/Manila timezone is not available on this host.");
    }
}

public sealed class PaymentDeadlineReminderProcessor(
    IPaymentDeadlineReminderStore store,
    IEmailSender sender,
    IOptions<PaymentReminderOptions> options,
    IOptions<EmailOptions> emailOptions,
    TimeProvider clock,
    IWebHostEnvironment environment,
    ILogger<PaymentDeadlineReminderProcessor> logger)
{
    private readonly PaymentReminderOptions _options = options.Value;

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        if (!_options.Enabled) return;
        if (!sender.IsAvailable || !TryGetPublicBaseUrl(out _))
            throw new InvalidOperationException("Payment reminders are enabled but Email SMTP or Email:PublicBaseUrl is not safely configured.");

        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var now = PhilippineWallClock.Now(clock);
        var candidates = await store.FindCandidateIdsAsync(now, nowUtc, cancellationToken);

        foreach (var candidate in candidates)
        {
            try { await ProcessCandidateAsync(candidate, now, nowUtc, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogWarning(exception, "Payment reminder processing failed for registration {RegistrationId}.", candidate); }
        }
    }

    private async Task ProcessCandidateAsync(int registrationId, DateTime now, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var registration = await store.FindRegistrationAsync(registrationId, cancellationToken);
        if (registration is null || !PaymentDeadlineReminderEligibility.IsEligible(registration, now) || !await store.HasVerifiedParticipantAsync(registration.User, cancellationToken)) return;
        var deadline = registration.PaymentDeadline!.Value;
        var reminder = await store.EnsureReminderAsync(registrationId, deadline, nowUtc, cancellationToken);
        if (reminder is null) return;
        var lease = Guid.NewGuid();
        if (!await store.TryClaimAsync(reminder.Id, lease, nowUtc, cancellationToken)) return;

        registration = await store.FindRegistrationAsync(registrationId, cancellationToken);
        if (registration is null)
        {
            await store.FinishAsync(reminder.Id, lease, PaymentDeadlineReminder.Superseded, nowUtc, null, cancellationToken);
            return;
        }
        if (registration.PaymentDeadline != deadline || !await store.IsLatestAttemptAsync(registration, cancellationToken) || !PaymentDeadlineReminderEligibility.IsEligible(registration, PhilippineWallClock.Now(clock)) ||
            !await store.HasVerifiedParticipantAsync(registration.User, cancellationToken))
        {
            await store.FinishAsync(reminder.Id, lease, PaymentDeadlineReminder.Superseded, nowUtc, null, cancellationToken);
            return;
        }

        var email = registration.User!.Email!;
        var message = BuildMessage(registration, email);
        EmailDeliveryResult outcome;
        try { outcome = await sender.SendAsync(message, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Payment reminder SMTP attempt failed for registration {RegistrationId}.", registrationId);
            outcome = EmailDeliveryResult.Failed();
        }
        var finalNowUtc = clock.GetUtcNow().UtcDateTime;
        var finalNow = PhilippineWallClock.Now(clock);
        if (outcome.Accepted)
            await store.FinishAsync(reminder.Id, lease, PaymentDeadlineReminder.Sent, finalNowUtc, "accepted", cancellationToken);
        else if (registration.PaymentDeadline is { } finalDeadline && finalDeadline <= finalNow)
            await store.FinishAsync(reminder.Id, lease, PaymentDeadlineReminder.Superseded, finalNowUtc, "expired", cancellationToken);
        else if (outcome.Outcome == EmailDeliveryOutcome.PermanentFailure)
            await store.FinishAsync(reminder.Id, lease, PaymentDeadlineReminder.Failed, finalNowUtc, "permanent", cancellationToken);
        else if (outcome.Outcome == EmailDeliveryOutcome.Unavailable)
            // Preflight normally prevents this. Do not burn a cohort's attempts if configuration changes mid-iteration.
            await store.ReleaseForConfigurationRecoveryAsync(reminder.Id, lease, finalNowUtc, cancellationToken);
        else
            await store.RetryOrFailAsync(reminder.Id, lease, finalNowUtc,
                outcome.Outcome == EmailDeliveryOutcome.Uncertain ? "uncertain" : "transient", cancellationToken);
    }

    private EmailMessage BuildMessage(EventRegistration registration, string recipient)
    {
        var deadline = registration.PaymentDeadline!.Value.ToString("MMMM d, yyyy h:mm tt");
        var title = registration.Event!.EventTitle;
        var url = BuildRegistrationUrl(registration.Id);
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedDeadline = WebUtility.HtmlEncode(deadline);
        var plain = $"TrailGuard payment deadline approaching\n\n{title}\nPayment deadline (Philippine Time): {deadline}\n\nUpload your payment receipt before the deadline: {url}";
        var html = $"<p><strong>TrailGuard payment deadline approaching</strong></p><p>{encodedTitle}</p><p>Payment deadline (Philippine Time): <strong>{encodedDeadline}</strong></p><p>Upload your payment receipt before the deadline.</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">View Registration</a></p>";
        return new EmailMessage(recipient, "TrailGuard payment deadline approaching", plain, html);
    }

    private string BuildRegistrationUrl(int registrationId)
    {
        TryGetPublicBaseUrl(out var baseUrl);
        return $"{baseUrl!.GetLeftPart(UriPartial.Authority)}{baseUrl.AbsolutePath.TrimEnd('/')}/Registration/MyRegistrations#registration-{registrationId}";
    }

    private bool TryGetPublicBaseUrl(out Uri? result)
    {
        result = null;
        var email = emailOptions.Value;
        if (!Uri.TryCreate(email.PublicBaseUrl, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return false;
        result = uri; return true;
    }
}

public static class PaymentDeadlineReminderEligibility
{
    public static bool IsEligible(EventRegistration registration, DateTime now) =>
        registration.Status == "Awaiting Payment" && !registration.IsPaid && registration.PaymentDeadline is { } deadline && deadline > now &&
        deadline <= now.AddHours(24) && registration.Event?.Status == "Upcoming";

    public static bool IsLatestAttempt(EventRegistration registration, IEnumerable<EventRegistration> attempts) =>
        attempts.Where(other => other.UserId == registration.UserId && other.EventId == registration.EventId)
            .OrderByDescending(other => other.RegisteredAt).ThenByDescending(other => other.Id).FirstOrDefault()?.Id == registration.Id;
}

public sealed class PaymentDeadlineReminderWorker(IServiceScopeFactory scopes, IOptions<PaymentReminderOptions> options, ILogger<PaymentDeadlineReminderWorker> logger) : BackgroundService
{
    private readonly PaymentReminderOptions _options = options.Value;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        if (!_options.Enabled) { logger.LogInformation("Payment deadline reminders are disabled."); await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<PaymentDeadlineReminderProcessor>().ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Payment reminder iteration failed; the worker will retry."); }
            await Task.Delay(_options.PollingInterval, stoppingToken);
        }
    }
}
