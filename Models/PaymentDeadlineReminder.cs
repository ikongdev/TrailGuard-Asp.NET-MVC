using System.ComponentModel.DataAnnotations;

namespace TrailGuard.Models;

/// <summary>Durable outbox state for one reminder about one saved payment deadline.</summary>
public sealed class PaymentDeadlineReminder
{
    public const string PaymentDeadlineApproaching = "PaymentDeadlineApproaching";
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Superseded = "Superseded";
    public const string Failed = "Failed";

    public long Id { get; set; }
    public int EventRegistrationId { get; set; }
    public EventRegistration? EventRegistration { get; set; }
    [MaxLength(64)] public string ReminderType { get; set; } = PaymentDeadlineApproaching;
    // PaymentDeadline is deliberately a Philippine wall-clock timestamp, matching EventRegistration.
    public DateTime PaymentDeadline { get; set; }
    [MaxLength(24)] public string State { get; set; } = Pending;
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    [MaxLength(160)] public string? LastOutcome { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
