using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

TrailGuardNpgsqlConfiguration.ApplyTimestampCompatibility();

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
var utc = new MutableTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
var now = PhilippineWallClock.Now(utc);

EventRegistration Registration(int id, DateTime? deadline = null, string status = "Awaiting Payment", bool paid = false) => new()
{
    Id = id, UserId = $"participant-{id}", EventId = id, RegisteredAt = now.AddDays(-2), Status = status, IsPaid = paid,
    PaymentDeadline = deadline ?? now.AddHours(24), PaymentReceiptUrl = "receipts/rejected.pdf",
    Event = new Event { Id = id, EventTitle = $"Event {id}", Status = "Upcoming", EventDate = now.AddDays(2) },
    User = new ApplicationUser { Id = $"participant-{id}", Email = $"participant{id}@example.com", EmailConfirmed = true, IsActive = true }
};

Check(PaymentDeadlineReminderEligibility.IsEligible(Registration(1), now), "exactly 24 hours is eligible");
Check(!PaymentDeadlineReminderEligibility.IsEligible(Registration(1, now.AddHours(24).AddTicks(1)), now), "just over 24 hours is excluded");
Check(!PaymentDeadlineReminderEligibility.IsEligible(Registration(1, now), now), "zero deadline is excluded");
var latest = Registration(9); latest.RegisteredAt = now.AddMinutes(-1);
var older = Registration(8); older.UserId = latest.UserId; older.EventId = latest.EventId; older.RegisteredAt = now.AddMinutes(-2);
Check(PaymentDeadlineReminderEligibility.IsLatestAttempt(latest, [older, latest]) && !PaymentDeadlineReminderEligibility.IsLatestAttempt(older, [older, latest]), "latest attempt uses RegisteredAt then Id");
Check(PhilippineWallClock.ToUtc(new DateTime(2026, 10, 22, 0, 30, 0)).UtcDateTime == new DateTime(2026, 10, 21, 16, 30, 0, DateTimeKind.Utc), "Philippine conversion is host-independent");
var defaults = new PaymentReminderOptions(); defaults.Validate();
Check(!defaults.Enabled && defaults.BatchSize == 50, "safe disabled defaults retained");
Check(EmailDeliveryResult.Delivered().Outcome == EmailDeliveryOutcome.Accepted && EmailDeliveryResult.Unavailable().Outcome == EmailDeliveryOutcome.Unavailable && EmailDeliveryResult.TransientFailure().Outcome == EmailDeliveryOutcome.TransientFailure && EmailDeliveryResult.PermanentFailure().Outcome == EmailDeliveryOutcome.PermanentFailure && EmailDeliveryResult.Uncertain().Outcome == EmailDeliveryOutcome.Uncertain, "delivery contract exposes all sanitized outcomes");
using (var translationContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql("Host=127.0.0.1;Port=1;Database=translation_only;Username=unused;Password=unused").Options))
{
    var sql = PaymentDeadlineReminderCandidateQuery.Build(translationContext, defaults, now, utc.GetUtcNow().UtcDateTime).ToQueryString();
    Check(sql.Contains("PaymentDeadlineReminders", StringComparison.Ordinal) && sql.Contains("NextAttemptAtUtc", StringComparison.Ordinal) && sql.Contains("LeaseExpiresAtUtc", StringComparison.Ordinal),
        "production Npgsql provider translates deadline/reminder exclusion query without a database connection");
}

PaymentDeadlineReminderProcessor Processor(FakeReminderStore store, FakeSender sender, PaymentReminderOptions? options = null, string publicBaseUrl = "http://localhost:5000/") => new(
    store, sender, Options.Create(options ?? new PaymentReminderOptions { Enabled = true, BatchSize = 2, LeaseMinutes = 10, MaxAttempts = 3 }),
    Options.Create(new EmailOptions { PublicBaseUrl = publicBaseUrl }), utc, new FakeEnvironment(), NullLogger<PaymentDeadlineReminderProcessor>.Instance);

var disabledScopes = new CountingScopeFactory();
var disabledWorker = new PaymentDeadlineReminderWorker(disabledScopes, Options.Create(new PaymentReminderOptions { Enabled = false }), NullLogger<PaymentDeadlineReminderWorker>.Instance);
await disabledWorker.StartAsync(CancellationToken.None); await Task.Delay(20); await disabledWorker.StopAsync(CancellationToken.None);
Check(disabledScopes.CreateScopeCalls == 0, "disabled worker performs no scoped database or email work");
var unavailablePreflightStore = new FakeReminderStore(utc, Registration(40)); var unavailablePreflightSender = new FakeSender(EmailDeliveryResult.Delivered()) { Available = false };
try { await Processor(unavailablePreflightStore, unavailablePreflightSender).ProcessOnceAsync(CancellationToken.None); throw new InvalidOperationException("unavailable preflight unexpectedly processed"); }
catch (InvalidOperationException ex) when (ex.Message.StartsWith("Payment reminders are enabled")) { }
Check(unavailablePreflightStore.DiscoveryCalls == 0 && unavailablePreflightSender.Messages.Count == 0, "unavailable SMTP fails before database discovery or email dispatch");
var invalidUrlStore = new FakeReminderStore(utc, Registration(41));
try { await Processor(invalidUrlStore, new FakeSender(EmailDeliveryResult.Delivered()), publicBaseUrl: "http://example.com/").ProcessOnceAsync(CancellationToken.None); throw new InvalidOperationException("unsafe URL preflight unexpectedly processed"); }
catch (InvalidOperationException ex) when (ex.Message.StartsWith("Payment reminders are enabled")) { }
Check(invalidUrlStore.DiscoveryCalls == 0, "unsafe public URL fails before database discovery");

var acceptedStore = new FakeReminderStore(utc, Registration(1)); var acceptedSender = new FakeSender(EmailDeliveryResult.Delivered());
await Processor(acceptedStore, acceptedSender).ProcessOnceAsync(CancellationToken.None);
await Processor(acceptedStore, acceptedSender).ProcessOnceAsync(CancellationToken.None);
Check(acceptedSender.Messages.Count == 1 && acceptedStore.Reminders.Single().State == PaymentDeadlineReminder.Sent, "accepted send is suppressed on another enabled scan/restart");
Check(acceptedStore.Reminders.Single().LastOutcome == "accepted", "accepted outcome is recorded");

var contenderStore = new FakeReminderStore(utc, Registration(2)) { ReturnLeasedCandidates = true };
var firstSender = new FakeSender(EmailDeliveryResult.Delivered()) { Block = true };
var secondSender = new FakeSender(EmailDeliveryResult.Delivered());
var first = Processor(contenderStore, firstSender).ProcessOnceAsync(CancellationToken.None);
await firstSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
await Processor(contenderStore, secondSender).ProcessOnceAsync(CancellationToken.None);
firstSender.Release.SetResult(); await first;
Check(firstSender.Messages.Count + secondSender.Messages.Count == 1, "two contenders produce one SMTP dispatch owner");

var leaseStore = new FakeReminderStore(utc, Registration(3));
var expired = leaseStore.CreateReminder(3); expired.State = PaymentDeadlineReminder.Sending; expired.LeaseId = Guid.NewGuid(); expired.LeaseExpiresAtUtc = utc.GetUtcNow().UtcDateTime.AddMinutes(-1); var staleLease = expired.LeaseId.Value;
await Processor(leaseStore, new FakeSender(EmailDeliveryResult.Delivered())).ProcessOnceAsync(CancellationToken.None);
await leaseStore.FinishAsync(expired.Id, staleLease, PaymentDeadlineReminder.Failed, utc.GetUtcNow().UtcDateTime, "stale", CancellationToken.None);
Check(expired.State == PaymentDeadlineReminder.Sent && expired.LastOutcome == "accepted", "expired lease is reclaimed and stale owner update is rejected");

var retryStore = new FakeReminderStore(utc, Registration(4)); var retrySender = new FakeSender(EmailDeliveryResult.TransientFailure(), EmailDeliveryResult.TransientFailure(), EmailDeliveryResult.TransientFailure());
var retryOptions = new PaymentReminderOptions { Enabled = true, BatchSize = 2, LeaseMinutes = 10, MaxAttempts = 3 };
await Processor(retryStore, retrySender, retryOptions).ProcessOnceAsync(CancellationToken.None);
var retry = retryStore.Reminders.Single(); Check(retry.AttemptCount == 1 && retry.NextAttemptAtUtc == utc.GetUtcNow().UtcDateTime.AddMinutes(5) && retry.LastOutcome == "transient", "first transient failure schedules five-minute retry");
utc.Advance(TimeSpan.FromMinutes(5)); await Processor(retryStore, retrySender, retryOptions).ProcessOnceAsync(CancellationToken.None);
Check(retry.AttemptCount == 2 && retry.NextAttemptAtUtc == utc.GetUtcNow().UtcDateTime.AddMinutes(10), "second transient failure schedules ten-minute retry");
utc.Advance(TimeSpan.FromMinutes(10)); await Processor(retryStore, retrySender, retryOptions).ProcessOnceAsync(CancellationToken.None);
Check(retry.State == PaymentDeadlineReminder.Failed && retry.AttemptCount == 3, "attempt limit becomes terminal failure");

var permanentStore = new FakeReminderStore(utc, Registration(5)); await Processor(permanentStore, new FakeSender(EmailDeliveryResult.PermanentFailure())).ProcessOnceAsync(CancellationToken.None);
Check(permanentStore.Reminders.Single().State == PaymentDeadlineReminder.Failed && permanentStore.Reminders.Single().LastOutcome == "permanent", "permanent rejection does not retry");
var uncertainStore = new FakeReminderStore(utc, Registration(6)); await Processor(uncertainStore, new FakeSender(EmailDeliveryResult.Uncertain())).ProcessOnceAsync(CancellationToken.None);
Check(uncertainStore.Reminders.Single().State == PaymentDeadlineReminder.Pending && uncertainStore.Reminders.Single().LastOutcome == "uncertain", "uncertain outcome is bounded-retry, not claimed exactly once");

var unavailableStore = new FakeReminderStore(utc, Registration(7)); await Processor(unavailableStore, new FakeSender(EmailDeliveryResult.Unavailable())).ProcessOnceAsync(CancellationToken.None);
Check(unavailableStore.Reminders.Single().AttemptCount == 0 && unavailableStore.Reminders.Single().LastOutcome == "unavailable", "mid-iteration unavailable result preserves retry budget");

var lostStore = new FakeReminderStore(utc, Registration(8)); lostStore.AfterClaim = () => lostStore.Registrations.Single().IsPaid = true; var lostSender = new FakeSender(EmailDeliveryResult.Delivered());
await Processor(lostStore, lostSender).ProcessOnceAsync(CancellationToken.None);
Check(lostSender.Messages.Count == 0 && lostStore.Reminders.Single().State == PaymentDeadlineReminder.Superseded, "lost payment eligibility immediately before dispatch is superseded");
var expiryStore = new FakeReminderStore(utc, Registration(9, PhilippineWallClock.Now(utc).AddMinutes(1))); var expirySender = new FakeSender(EmailDeliveryResult.TransientFailure()) { AfterSend = () => utc.Advance(TimeSpan.FromMinutes(2)) };
await Processor(expiryStore, expirySender).ProcessOnceAsync(CancellationToken.None);
Check(expiryStore.Reminders.Single().State == PaymentDeadlineReminder.Superseded && expiryStore.Reminders.Single().LastOutcome == "expired", "deadline expiry after dispatch attempt is terminal");

var laterStore = new FakeReminderStore(utc, Registration(10), Registration(11)); var laterSender = new FakeSender(EmailDeliveryResult.TransientFailure(), EmailDeliveryResult.Delivered());
await Processor(laterStore, laterSender).ProcessOnceAsync(CancellationToken.None);
Check(laterSender.Messages.Count == 2 && laterSender.Messages[1].To == "participant11@example.com", "one failed item does not block later verified recipient");

var batchStore = new FakeReminderStore(utc, Registration(20), Registration(21), Registration(22), Registration(23), Registration(24));
var batchNow = utc.GetUtcNow().UtcDateTime;
var sent = batchStore.CreateReminder(20); sent.SentAtUtc = batchNow;
var leased = batchStore.CreateReminder(21); leased.LeaseExpiresAtUtc = batchNow.AddMinutes(1);
var delayed = batchStore.CreateReminder(22); delayed.NextAttemptAtUtc = batchNow.AddMinutes(1);
var exhausted = batchStore.CreateReminder(23); exhausted.AttemptCount = 3;
var batchSender = new FakeSender(EmailDeliveryResult.Delivered()); await Processor(batchStore, batchSender, retryOptions).ProcessOnceAsync(CancellationToken.None);
Check(batchSender.Messages.Single().To == "participant24@example.com", "non-due/leased/sent/exhausted records do not starve later due candidate");

var cancellationStore = new FakeReminderStore(utc, Registration(30)); var cancellationSender = new FakeSender(EmailDeliveryResult.Delivered());
using (var cancellation = new CancellationTokenSource()) { cancellationStore.AfterClaim = cancellation.Cancel; await Processor(cancellationStore, cancellationSender).ProcessOnceAsync(cancellation.Token); }
Check(cancellationSender.Messages.Count == 0 && cancellationStore.Reminders.Single().State == PaymentDeadlineReminder.Sending, "cancellation before send leaves claim for lease recovery without SMTP dispatch");
var blockingStore = new FakeReminderStore(utc, Registration(31)); var blockingSender = new FakeSender(EmailDeliveryResult.Delivered()) { Block = true };
using (var cancellation = new CancellationTokenSource()) { var task = Processor(blockingStore, blockingSender).ProcessOnceAsync(cancellation.Token); await blockingSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel(); await task; }
Check(blockingStore.Reminders.Single().State == PaymentDeadlineReminder.Sending, "cancellation during controlled SMTP operation leaves lease for recovery");

Console.WriteLine($"Payment deadline reminder orchestration verification passed ({checks} assertions). It uses the production processor with controlled persistence and sender seams; it does not establish PostgreSQL transaction/isolation or real SMTP/inbox behavior.");

sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
{
    private DateTimeOffset _value = value;
    public override DateTimeOffset GetUtcNow() => _value;
    public void Advance(TimeSpan amount) => _value = _value.Add(amount);
}

sealed class FakeEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Verification";
    public string WebRootPath { get; set; } = ".";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class FakeSender(params EmailDeliveryResult[] results) : IEmailSender
{
    private readonly Queue<EmailDeliveryResult> _results = new(results);
    public List<EmailMessage> Messages { get; } = [];
    public bool Available { get; set; } = true;
    public bool IsAvailable => Available;
    public bool Block { get; set; }
    public Action? AfterSend { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(message); Entered.TrySetResult();
        if (Block) await Release.Task.WaitAsync(cancellationToken);
        AfterSend?.Invoke();
        return _results.Count == 0 ? EmailDeliveryResult.Delivered() : _results.Dequeue();
    }
}

sealed class CountingScopeFactory : IServiceScopeFactory
{
    public int CreateScopeCalls { get; private set; }
    public IServiceScope CreateScope() { CreateScopeCalls++; throw new InvalidOperationException("Disabled worker must not create a scope."); }
}

sealed class FakeReminderStore(MutableTimeProvider clock, params EventRegistration[] registrations) : IPaymentDeadlineReminderStore
{
    private long _nextId = 1;
    public List<EventRegistration> Registrations { get; } = [.. registrations];
    public List<PaymentDeadlineReminder> Reminders { get; } = [];
    public bool ReturnLeasedCandidates { get; set; }
    public int DiscoveryCalls { get; private set; }
    public Action? AfterClaim { get; set; }
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<IReadOnlyList<int>> FindCandidateIdsAsync(DateTime now, DateTime nowUtc, CancellationToken cancellationToken)
    {
        DiscoveryCalls++;
        var ids = Registrations.Where(r => PaymentDeadlineReminderEligibility.IsEligible(r, now) && IsLatest(r))
            .Where(r =>
            {
                var reminder = Reminders.SingleOrDefault(x => x.EventRegistrationId == r.Id && x.PaymentDeadline == r.PaymentDeadline);
                return reminder is null || (reminder.SentAtUtc is null && reminder.SupersededAtUtc is null && reminder.AttemptCount < 3 &&
                    (reminder.NextAttemptAtUtc is null || reminder.NextAttemptAtUtc <= nowUtc) &&
                    (ReturnLeasedCandidates || reminder.LeaseExpiresAtUtc is null || reminder.LeaseExpiresAtUtc <= nowUtc));
            }).OrderBy(r => r.PaymentDeadline).ThenBy(r => r.Id).Select(r => r.Id).Take(2).ToArray();
        return Task.FromResult<IReadOnlyList<int>>(ids);
    }
    public Task<EventRegistration?> FindRegistrationAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Registrations.SingleOrDefault(r => r.Id == id));
    public Task<bool> IsLatestAttemptAsync(EventRegistration registration, CancellationToken cancellationToken) => Task.FromResult(IsLatest(registration));
    public Task<bool> HasVerifiedParticipantAsync(ApplicationUser? user, CancellationToken cancellationToken) => Task.FromResult(user is { IsActive: true, EmailConfirmed: true } && TrailGuardEmailAddress.TryNormalize(user.Email, out _, out _));
    public Task<PaymentDeadlineReminder?> EnsureReminderAsync(int registrationId, DateTime deadline, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<PaymentDeadlineReminder?>(Reminders.SingleOrDefault(r => r.EventRegistrationId == registrationId && r.PaymentDeadline == deadline) ?? CreateReminder(registrationId));
    public PaymentDeadlineReminder CreateReminder(int registrationId) { var registration = Registrations.Single(r => r.Id == registrationId); var result = new PaymentDeadlineReminder { Id = _nextId++, EventRegistrationId = registrationId, PaymentDeadline = registration.PaymentDeadline!.Value, CreatedAtUtc = UtcNow, UpdatedAtUtc = UtcNow }; Reminders.Add(result); return result; }
    public Task<bool> TryClaimAsync(long id, Guid lease, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var r = Reminders.Single(x => x.Id == id);
        if (r.SentAtUtc is not null || r.SupersededAtUtc is not null || r.AttemptCount >= 3 || r.NextAttemptAtUtc > nowUtc || r.LeaseExpiresAtUtc > nowUtc) return Task.FromResult(false);
        r.State = PaymentDeadlineReminder.Sending; r.LeaseId = lease; r.LeaseExpiresAtUtc = nowUtc.AddMinutes(10); r.AttemptCount++; r.LastAttemptAtUtc = nowUtc; r.UpdatedAtUtc = nowUtc; AfterClaim?.Invoke(); return Task.FromResult(true);
    }
    public Task FinishAsync(long id, Guid lease, string state, DateTime nowUtc, string? outcome, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = state; r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.LastOutcome = outcome; r.UpdatedAtUtc = nowUtc; if (state == PaymentDeadlineReminder.Sent) r.SentAtUtc = nowUtc; if (state == PaymentDeadlineReminder.Superseded) r.SupersededAtUtc = nowUtc; return Task.CompletedTask; }
    public Task ReleaseForConfigurationRecoveryAsync(long id, Guid lease, DateTime nowUtc, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = PaymentDeadlineReminder.Pending; r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.NextAttemptAtUtc = nowUtc.AddMinutes(5); r.AttemptCount = Math.Max(0, r.AttemptCount - 1); r.LastOutcome = "unavailable"; return Task.CompletedTask; }
    public Task RetryOrFailAsync(long id, Guid lease, DateTime nowUtc, string outcome, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = r.AttemptCount >= 3 ? PaymentDeadlineReminder.Failed : PaymentDeadlineReminder.Pending; r.NextAttemptAtUtc = r.State == PaymentDeadlineReminder.Failed ? null : nowUtc.AddMinutes(5 * r.AttemptCount); r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.LastOutcome = outcome; return Task.CompletedTask; }
    private bool IsLatest(EventRegistration registration) => Registrations.Where(r => r.UserId == registration.UserId && r.EventId == registration.EventId).OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id).First().Id == registration.Id;
}
