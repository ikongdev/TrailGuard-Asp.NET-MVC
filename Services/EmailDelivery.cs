using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TrailGuard.Services;

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public SmtpOptions Smtp { get; set; } = new();
    public string? PublicBaseUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed record EmailMessage(string To, string Subject, string PlainTextBody, string HtmlBody);

public enum EmailDeliveryOutcome
{
    Accepted,
    Unavailable,
    TransientFailure,
    PermanentFailure,
    Uncertain
}

public sealed record EmailDeliveryResult(bool Accepted, string? SafeFailure, EmailDeliveryOutcome Outcome)
{
    public static EmailDeliveryResult Delivered() => new(true, null, EmailDeliveryOutcome.Accepted);
    public static EmailDeliveryResult Unavailable() => new(false, "Email delivery is not configured.", EmailDeliveryOutcome.Unavailable);
    public static EmailDeliveryResult TransientFailure() => new(false, "Email delivery could not be completed. Please try again.", EmailDeliveryOutcome.TransientFailure);
    public static EmailDeliveryResult PermanentFailure() => new(false, "Email delivery could not be completed. Please try again.", EmailDeliveryOutcome.PermanentFailure);
    // SMTP may have accepted a message before a timeout/connection loss. Retrying can duplicate it.
    public static EmailDeliveryResult Uncertain() => new(false, "Email delivery could not be confirmed. Please try again.", EmailDeliveryOutcome.Uncertain);
    public static EmailDeliveryResult Failed() => TransientFailure();
}

public interface IEmailSender
{
    bool IsAvailable { get; }
    Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

// Tests can inject this sender explicitly. Production only registers SmtpEmailSender.
public sealed class InMemoryEmailSender : IEmailSender
{
    public List<EmailMessage> Messages { get; } = [];
    public bool IsAvailable => true;
    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(message);
        return Task.FromResult(EmailDeliveryResult.Delivered());
    }
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => TryValidate(out _);

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!TryValidate(out _)) return EmailDeliveryResult.Unavailable();
        if (!IsSafeMailbox(message.To)) return EmailDeliveryResult.Failed();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 60)));

        try
        {
            var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress(_options.FromName!.Trim(), _options.FromAddress!.Trim()));
            mail.To.Add(MailboxAddress.Parse(message.To));
            mail.Subject = message.Subject;
            mail.Body = new BodyBuilder { TextBody = message.PlainTextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            // MailKit's default certificate validation is intentionally retained.
            await client.ConnectAsync(_options.Smtp.Host!, _options.Smtp.Port, SecureSocketOptions.StartTls, timeout.Token);
            await client.AuthenticateAsync(_options.Smtp.Username!, _options.Smtp.Password!, timeout.Token);
            await client.SendAsync(mail, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
            return EmailDeliveryResult.Delivered(); // SMTP acceptance, not inbox delivery.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Email delivery timed out.");
            return EmailDeliveryResult.Uncertain();
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Email delivery was rejected ({FailureType}).", ex.GetType().Name);
            return EmailDeliveryResult.PermanentFailure();
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogWarning("Email delivery command failed ({FailureType}, {StatusCode}).", ex.GetType().Name, (int)ex.StatusCode);
            return (int)ex.StatusCode >= 500 ? EmailDeliveryResult.PermanentFailure() : EmailDeliveryResult.TransientFailure();
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("Email delivery failed ({FailureType}).", ex.GetType().Name);
            return EmailDeliveryResult.TransientFailure();
        }
        // A protocol/stream failure can happen after SMTP accepted DATA but before the
        // client receives that acknowledgement. Preserve that ambiguity for the outbox.
        catch (Exception ex) when (ex is SmtpProtocolException or IOException)
        {
            _logger.LogWarning("Email delivery confirmation was lost ({FailureType}).", ex.GetType().Name);
            return EmailDeliveryResult.Uncertain();
        }
    }

    private bool TryValidate(out string? reason)
    {
        reason = null;
        if (!_options.Enabled) { reason = "disabled"; return false; }
        if (_options.Smtp.Port != 587 || string.IsNullOrWhiteSpace(_options.Smtp.Host) ||
            !string.Equals(_options.Smtp.Host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_options.Smtp.Username) || string.IsNullOrWhiteSpace(_options.Smtp.Password) ||
            !IsSafeMailbox(_options.FromAddress) || !string.Equals(_options.FromAddress, _options.Smtp.Username, StringComparison.OrdinalIgnoreCase))
        {
            reason = "incomplete";
            return false;
        }
        return true;
    }

    private static bool IsSafeMailbox(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(['\r', '\n']) < 0 && MailAddress.TryCreate(value, out _);
}
