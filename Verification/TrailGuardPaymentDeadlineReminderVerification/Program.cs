using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
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
async Task ExpectCancellation(Func<Task> action, string message)
{
    try { await action(); }
    catch (OperationCanceledException) { Check(true, message); return; }
    Check(false, message);
}
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
    var cursor = new PaymentDeadlineReminderCandidate(25, now.AddHours(1));
    var nextPageSql = PaymentDeadlineReminderCandidateQuery.Build(translationContext, defaults, now, utc.GetUtcNow().UtcDateTime, cursor)
        .Select(r => new PaymentDeadlineReminderCandidate(r.Id, r.PaymentDeadline!.Value)).ToQueryString();
    Check(nextPageSql.Contains("\"Id\" >", StringComparison.Ordinal) && nextPageSql.Contains("ORDER BY", StringComparison.Ordinal) &&
        nextPageSql.Contains("LIMIT", StringComparison.Ordinal) && !nextPageSql.Contains("OFFSET", StringComparison.Ordinal),
        "production Npgsql provider translates projected keyset page with a limit and no offset");
}

// Exercise the production account checker (including email and role policy) without querying a database.
// Only UserManager.GetRolesAsync is substituted; discovery/tracking and SMTP are controlled below.
using var accountContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql("Host=127.0.0.1;Port=1;Database=translation_only;Username=unused;Password=unused").Options);
using var roleUsers = new ControlledRoleUserManager(accountContext);
var accountStore = new EfPaymentDeadlineReminderStore(accountContext, roleUsers, Options.Create(defaults));

PaymentDeadlineReminderProcessor Processor(FakeReminderStore store, FakeSender sender, PaymentReminderOptions? options = null, string publicBaseUrl = "http://localhost:5000/")
{
    options ??= new PaymentReminderOptions { Enabled = true, BatchSize = 2, LeaseMinutes = 10, MaxAttempts = 3 };
    store.Options = options;
    store.AccountEligibility = accountStore.HasVerifiedParticipantAsync;
    return new(store, sender, Options.Create(options), Options.Create(new EmailOptions { PublicBaseUrl = publicBaseUrl }),
        utc, new FakeEnvironment(), NullLogger<PaymentDeadlineReminderProcessor>.Instance);
}

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
var batchSender = new FakeSender(EmailDeliveryResult.Delivered());
var batchProcessor = Processor(batchStore, batchSender, new PaymentReminderOptions { Enabled = true, BatchSize = 1 });
var duePage = await batchStore.FindCandidatesAsync(PhilippineWallClock.Now(utc), batchNow, null, CancellationToken.None);
Check(duePage.Single().RegistrationId == 24, "future retries and active leases are excluded before the one-row discovery limit");
await batchProcessor.ProcessOnceAsync(CancellationToken.None);
Check(batchSender.Messages.Single().To == "participant24@example.com", "non-due/leased/sent/exhausted records do not starve later due candidate");

var cancellationStore = new FakeReminderStore(utc, Registration(30)); var cancellationSender = new FakeSender(EmailDeliveryResult.Delivered());
using (var cancellation = new CancellationTokenSource()) { cancellationStore.AfterClaim = cancellation.Cancel; await ExpectCancellation(() => Processor(cancellationStore, cancellationSender).ProcessOnceAsync(cancellation.Token), "cancellation after claim propagates from the scan"); }
Check(cancellationSender.Messages.Count == 0 && cancellationStore.Reminders.Single().State == PaymentDeadlineReminder.Sending, "cancellation before send leaves claim for lease recovery without SMTP dispatch");
var blockingStore = new FakeReminderStore(utc, Registration(31)); var blockingSender = new FakeSender(EmailDeliveryResult.Delivered()) { Block = true };
using (var cancellation = new CancellationTokenSource()) { var task = Processor(blockingStore, blockingSender).ProcessOnceAsync(cancellation.Token); await blockingSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel(); await ExpectCancellation(() => task, "cancellation during controlled send propagates from the scan"); }
Check(blockingStore.Reminders.Single().State == PaymentDeadlineReminder.Sending, "cancellation during controlled SMTP operation leaves lease for recovery");

var pageOptions = new PaymentReminderOptions { Enabled = true, BatchSize = 50 };
var rejectedPrefix = Enumerable.Range(1000, 51).Select(id => Registration(id)).ToArray();
for (var index = 0; index < 50; index++)
{
    var user = rejectedPrefix[index].User!;
    switch (index % 5)
    {
        case 0: user.IsActive = false; break;
        case 1: user.EmailConfirmed = false; break;
        case 2: user.Email = "invalid-current-email"; break;
        case 3: roleUsers.Roles[user.Id] = []; break;
        case 4: roleUsers.Roles[user.Id] = ["Participant", "Organizer"]; break;
    }
}
foreach (var index in Enumerable.Range(0, 5))
    Check(!await accountStore.HasVerifiedParticipantAsync(rejectedPrefix[index].User, CancellationToken.None),
        $"production account checker rejects variant {index}: inactive, unconfirmed, invalid email, missing role, conflicting roles");
var prefixStore = new FakeReminderStore(utc, rejectedPrefix.Reverse().ToArray());
var prefixSender = new FakeSender(); var prefixProcessor = Processor(prefixStore, prefixSender, pageOptions);
await prefixProcessor.ProcessOnceAsync(CancellationToken.None);
Check(prefixStore.DiscoveredPages.Count == 2 && prefixStore.DiscoveredPages[0].Length == 50 && prefixStore.DiscoveredPages[1].Single() == 1050,
    "a full rejected page advances by discovered deadline/Id keys to the eligible 51st registration");
Check(prefixSender.Messages.Single().To == "participant1050@example.com" && prefixStore.Reminders.Single().EventRegistrationId == 1050,
    "all five rejection types leave tracking untouched while the later eligible recipient is sent");
await prefixProcessor.ProcessOnceAsync(CancellationToken.None);
Check(prefixSender.Messages.Count == 1 && prefixStore.DiscoveredPages[^1].Length == 0,
    "another poll traverses the rejected prefix to end-of-results without resending the successful reminder");
for (var index = 0; index < 5; index++)
{
    var user = rejectedPrefix[index].User!;
    user.IsActive = true; user.EmailConfirmed = true; user.Email = $"participant{rejectedPrefix[index].Id}@example.com";
    roleUsers.Roles[user.Id] = ["Participant"];
}
await prefixProcessor.ProcessOnceAsync(CancellationToken.None);
Check(prefixSender.Messages.Count == 6 && prefixStore.Reminders.Count == 6 &&
    Enumerable.Range(1000, 5).All(id => prefixStore.Reminders.Any(r => r.EventRegistrationId == id && r.State == PaymentDeadlineReminder.Sent)),
    "all five repaired accounts are reconsidered on a fresh poll before their unchanged deadlines");
Check(prefixSender.Messages.Count(m => m.To == "participant1050@example.com") == 1,
    "account recovery and changing discovery results do not resend the original successful reminder");

// Several full rejected pages, followed by mutable sent rows and increasing deadlines with lower IDs.
var multiplePages = Enumerable.Range(2100, 5).Select(id => Registration(id)).ToList();
foreach (var registration in multiplePages.Take(4)) registration.User!.IsActive = false;
multiplePages.Add(Registration(2000, now.AddHours(24).AddMinutes(-1)));
multiplePages.Add(Registration(1999, now.AddHours(24)));
// Keep the first five at one deadline; the last two have later deadlines and smaller IDs.
foreach (var registration in multiplePages.Take(5)) registration.PaymentDeadline = now.AddHours(23);
var multiStore = new FakeReminderStore(utc, multiplePages.ToArray()); var multiSender = new FakeSender();
await Processor(multiStore, multiSender).ProcessOnceAsync(CancellationToken.None);
Check(multiStore.DiscoveredPages.SelectMany(page => page).SequenceEqual(new[] { 2100, 2101, 2102, 2103, 2104, 2000, 1999 }),
    "multiple rejected pages advance once per key, including later deadlines whose IDs are smaller");
Check(multiSender.Messages.Select(message => message.To).SequenceEqual(new[] { "participant2104@example.com", "participant2000@example.com", "participant1999@example.com" }),
    "successful sends removing earlier rows do not skip later recipients as offset pagination would");
Check(multiStore.DiscoveredPages.All(page => page.Length <= 2) && multiStore.DiscoveredPages.Count == 4,
    "configured two-row pages keep discovery bounded and a short final page ends the scan");

var longScanStore = new FakeReminderStore(utc, Registration(2200), Registration(2201)); var longScanSender = new FakeSender();
longScanSender.AfterSend = () => { if (longScanSender.Messages.Count == 1) utc.Advance(TimeSpan.FromMinutes(11)); };
await Processor(longScanStore, longScanSender, new PaymentReminderOptions { Enabled = true, BatchSize = 1 }).ProcessOnceAsync(CancellationToken.None);
Check(longScanSender.Messages.Count == 2 && longScanStore.Reminders.Single(r => r.EventRegistrationId == 2201).LastAttemptAtUtc == utc.GetUtcNow().UtcDateTime,
    "later pages claim with current time rather than an expired poll-start lease timestamp");

var emptyStore = new FakeReminderStore(utc); var emptySender = new FakeSender();
await Processor(emptyStore, emptySender).ProcessOnceAsync(CancellationToken.None);
Check(emptyStore.DiscoveryCalls == 1 && emptySender.Messages.Count == 0 && emptyStore.Reminders.Count == 0,
    "empty discovery terminates without processing or tracking writes");
var allRejected = new[] { Registration(3000), Registration(3001) };
foreach (var registration in allRejected) registration.User!.IsActive = false;
var allRejectedStore = new FakeReminderStore(utc, allRejected);
await Processor(allRejectedStore, new FakeSender()).ProcessOnceAsync(CancellationToken.None);
Check(allRejectedStore.DiscoveryCalls == 2 && allRejectedStore.DiscoveredPages[^1].Length == 0 && allRejectedStore.Reminders.Count == 0,
    "an exactly full rejected last page advances to an empty page and terminates without suppression");

using (var cancellation = new CancellationTokenSource())
{
    var beforeDiscovery = new FakeReminderStore(utc, Registration(3100)); cancellation.Cancel();
    await ExpectCancellation(() => Processor(beforeDiscovery, new FakeSender()).ProcessOnceAsync(cancellation.Token), "pre-cancelled scan propagates cancellation");
    Check(beforeDiscovery.DiscoveryCalls == 0, "pre-cancelled scan performs no discovery");
}
using (var cancellation = new CancellationTokenSource())
{
    var duringRejection = new FakeReminderStore(utc, allRejected.Append(Registration(3002)).ToArray()) { AfterAccountCheck = cancellation.Cancel };
    var cancelledSender = new FakeSender();
    await ExpectCancellation(() => Processor(duringRejection, cancelledSender, new PaymentReminderOptions { Enabled = true, BatchSize = 1 }).ProcessOnceAsync(cancellation.Token),
        "cancellation while rejecting a page stops before fetching the next page");
    Check(duringRejection.DiscoveryCalls == 1 && cancelledSender.Messages.Count == 0 && duringRejection.Reminders.Count == 0,
        "cancelled rejected-page scan sends nothing and creates no tracking");
}

Console.WriteLine($"Payment deadline reminder orchestration verification passed ({checks} assertions). It uses the production processor and account checker with controlled discovery, persistence, role lookup, and sender seams; Npgsql SQL generation does not establish PostgreSQL execution or real SMTP/inbox behavior.");

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
    public Action? AfterAccountCheck { get; set; }
    public PaymentReminderOptions Options { get; set; } = new();
    public Func<ApplicationUser?, CancellationToken, Task<bool>> AccountEligibility { get; set; } = null!;
    public List<int[]> DiscoveredPages { get; } = [];
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<IReadOnlyList<PaymentDeadlineReminderCandidate>> FindCandidatesAsync(DateTime now, DateTime nowUtc, PaymentDeadlineReminderCandidate? after, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiscoveryCalls++;
        var ids = Registrations.Where(r => PaymentDeadlineReminderEligibility.IsEligible(r, now) && IsLatest(r))
            .Where(r =>
            {
                var reminder = Reminders.SingleOrDefault(x => x.EventRegistrationId == r.Id && x.ReminderType == PaymentDeadlineReminder.PaymentDeadlineApproaching && x.PaymentDeadline == r.PaymentDeadline);
                return reminder is null || (reminder.SentAtUtc is null && reminder.SupersededAtUtc is null && reminder.AttemptCount < Options.MaxAttempts &&
                    (reminder.NextAttemptAtUtc is null || reminder.NextAttemptAtUtc <= nowUtc) &&
                    (ReturnLeasedCandidates || reminder.LeaseExpiresAtUtc is null || reminder.LeaseExpiresAtUtc <= nowUtc));
            }).Where(r => after is null || r.PaymentDeadline > after.PaymentDeadline || (r.PaymentDeadline == after.PaymentDeadline && r.Id > after.RegistrationId))
            .OrderBy(r => r.PaymentDeadline).ThenBy(r => r.Id).Take(Options.BatchSize)
            .Select(r => new PaymentDeadlineReminderCandidate(r.Id, r.PaymentDeadline!.Value)).ToArray();
        DiscoveredPages.Add(ids.Select(candidate => candidate.RegistrationId).ToArray());
        return Task.FromResult<IReadOnlyList<PaymentDeadlineReminderCandidate>>(ids);
    }
    public Task<EventRegistration?> FindRegistrationAsync(int id, CancellationToken cancellationToken) => Task.FromResult(Registrations.SingleOrDefault(r => r.Id == id));
    public Task<bool> IsLatestAttemptAsync(EventRegistration registration, CancellationToken cancellationToken) => Task.FromResult(IsLatest(registration));
    public async Task<bool> HasVerifiedParticipantAsync(ApplicationUser? user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eligible = await AccountEligibility(user, cancellationToken);
        AfterAccountCheck?.Invoke();
        return eligible;
    }
    public Task<PaymentDeadlineReminder?> EnsureReminderAsync(int registrationId, DateTime deadline, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<PaymentDeadlineReminder?>(Reminders.SingleOrDefault(r => r.EventRegistrationId == registrationId && r.PaymentDeadline == deadline) ?? CreateReminder(registrationId));
    public PaymentDeadlineReminder CreateReminder(int registrationId) { var registration = Registrations.Single(r => r.Id == registrationId); var result = new PaymentDeadlineReminder { Id = _nextId++, EventRegistrationId = registrationId, PaymentDeadline = registration.PaymentDeadline!.Value, CreatedAtUtc = UtcNow, UpdatedAtUtc = UtcNow }; Reminders.Add(result); return result; }
    public Task<bool> TryClaimAsync(long id, Guid lease, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var r = Reminders.Single(x => x.Id == id);
        if (r.SentAtUtc is not null || r.SupersededAtUtc is not null || r.AttemptCount >= Options.MaxAttempts || r.NextAttemptAtUtc > nowUtc || r.LeaseExpiresAtUtc > nowUtc) return Task.FromResult(false);
        r.State = PaymentDeadlineReminder.Sending; r.LeaseId = lease; r.LeaseExpiresAtUtc = nowUtc.Add(Options.LeaseDuration); r.AttemptCount++; r.LastAttemptAtUtc = nowUtc; r.UpdatedAtUtc = nowUtc; AfterClaim?.Invoke(); return Task.FromResult(true);
    }
    public Task FinishAsync(long id, Guid lease, string state, DateTime nowUtc, string? outcome, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = state; r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.LastOutcome = outcome; r.UpdatedAtUtc = nowUtc; if (state == PaymentDeadlineReminder.Sent) r.SentAtUtc = nowUtc; if (state == PaymentDeadlineReminder.Superseded) r.SupersededAtUtc = nowUtc; return Task.CompletedTask; }
    public Task ReleaseForConfigurationRecoveryAsync(long id, Guid lease, DateTime nowUtc, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = PaymentDeadlineReminder.Pending; r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.NextAttemptAtUtc = nowUtc.AddMinutes(5); r.AttemptCount = Math.Max(0, r.AttemptCount - 1); r.LastOutcome = "unavailable"; return Task.CompletedTask; }
    public Task RetryOrFailAsync(long id, Guid lease, DateTime nowUtc, string outcome, CancellationToken cancellationToken) { var r = Reminders.Single(x => x.Id == id); if (r.LeaseId != lease) return Task.CompletedTask; r.State = r.AttemptCount >= Options.MaxAttempts ? PaymentDeadlineReminder.Failed : PaymentDeadlineReminder.Pending; r.NextAttemptAtUtc = r.State == PaymentDeadlineReminder.Failed ? null : nowUtc.AddMinutes(5 * r.AttemptCount); r.LeaseId = null; r.LeaseExpiresAtUtc = null; r.LastOutcome = outcome; return Task.CompletedTask; }
    private bool IsLatest(EventRegistration registration) => Registrations.Where(r => r.UserId == registration.UserId && r.EventId == registration.EventId).OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id).First().Id == registration.Id;
}

sealed class ControlledRoleUserManager(ApplicationDbContext context) : UserManager<ApplicationUser>(
    new UserStore<ApplicationUser>(context), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
    [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), new ServiceCollection().BuildServiceProvider(),
    NullLogger<UserManager<ApplicationUser>>.Instance)
{
    public Dictionary<string, IList<string>> Roles { get; } = [];
    public override Task<IList<string>> GetRolesAsync(ApplicationUser user) =>
        Task.FromResult(Roles.TryGetValue(user.Id, out var roles) ? roles : (IList<string>)["Participant"]);
}
