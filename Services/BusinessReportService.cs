using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services;

public interface IBusinessReportService
{
    Task<(BusinessReportFilter Filter, IReadOnlyList<string> Errors, IReadOnlyList<BusinessReportFilterOption> Trails, IReadOnlyList<BusinessReportFilterOption> Organizers)> NormalizeAsync(BusinessReportFilter requested, bool malformedDate);
    Task<BusinessReportData> BuildAsync(BusinessReportFilter filter, string? trailScope = null, string? organizerScope = null);
}

public sealed class BusinessReportService : IBusinessReportService
{
    private readonly ApplicationDbContext _context;
    private readonly IPhilippineClock _clock;

    public BusinessReportService(ApplicationDbContext context, IPhilippineClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<(BusinessReportFilter Filter, IReadOnlyList<string> Errors, IReadOnlyList<BusinessReportFilterOption> Trails, IReadOnlyList<BusinessReportFilterOption> Organizers)> NormalizeAsync(BusinessReportFilter requested, bool malformedDate)
    {
        var today = _clock.Today;
        var filter = BusinessReportFilterPolicy.ApplyDefaults(requested, today);
        var errors = BusinessReportFilterPolicy.ValidateDates(filter, malformedDate).ToList();

        var events = _context.Events.AsNoTracking();
        var trailRows = await events
            .Select(e => new { e.TrailId, e.TrailNameSnapshot })
            .Distinct()
            .OrderBy(x => x.TrailNameSnapshot).ThenBy(x => x.TrailId)
            .ToListAsync();
        var trails = trailRows.Select(x => new BusinessReportFilterOption { Value = x.TrailId.ToString(), Label = BusinessReportCalculator.TrailLabel(x.TrailId, x.TrailNameSnapshot) }).ToList();
        var organizers = await (from e in events
                                join u in _context.Users.AsNoTracking() on e.OrganizerId equals u.Id into users
                                from u in users.DefaultIfEmpty()
                                where e.OrganizerId != null
                                select new { e.OrganizerId, Name = u == null ? e.OrganizedBy : (u.FirstName + " " + u.LastName) })
            .Distinct()
            .OrderBy(x => x.Name).ThenBy(x => x.OrganizerId)
            .Select(x => new BusinessReportFilterOption { Value = x.OrganizerId!, Label = string.IsNullOrWhiteSpace(x.Name) ? "Organizer unavailable" : x.Name! })
            .ToListAsync();

        if (filter.TrailId.HasValue && !trails.Any(x => x.Value == filter.TrailId.Value.ToString())) errors.Add("Select a valid trail filter.");
        if (filter.OrganizerId is not null && !organizers.Any(x => x.Value == filter.OrganizerId)) errors.Add("Select a valid organizer filter.");
        return (filter, errors, trails, organizers);
    }

    public async Task<BusinessReportData> BuildAsync(BusinessReportFilter filter, string? trailScope = null, string? organizerScope = null)
    {
        var eventQuery = _context.Events.AsNoTracking().Where(BusinessReportFilterPolicy.EventCohortPredicate(filter));

        var events = await (from e in eventQuery
                            join u in _context.Users.AsNoTracking() on e.OrganizerId equals u.Id into users
                            from u in users.DefaultIfEmpty()
                            select new BusinessReportEvent
                            {
                                Id = e.Id, TrailId = e.TrailId, TrailNameSnapshot = e.TrailNameSnapshot,
                                EventTitle = e.EventTitle, EventDate = e.EventDate, Status = e.Status,
                                Capacity = e.Capacity, OrganizerId = e.OrganizerId,
                                OrganizerName = u == null ? e.OrganizedBy : (u.FirstName + " " + u.LastName)
                            }).ToListAsync();
        var eventIds = events.Select(e => e.Id).ToList();
        var cohortRegistrations = eventIds.Count == 0 ? [] : await _context.EventRegistrations.AsNoTracking()
            .Where(r => eventIds.Contains(r.EventId))
            .Select(r => new BusinessReportRegistration { Id = r.Id, EventId = r.EventId, UserId = r.UserId, RegisteredAt = r.RegisteredAt, Status = r.Status, IsPaid = r.IsPaid })
            .ToListAsync();

        // The submissions cohort deliberately uses RegisteredAt, but carries the same trail/organizer scope.
        var submissionEvents = _context.Events.AsNoTracking().Where(BusinessReportFilterPolicy.SubmissionEventScopePredicate(filter));
        var submissionEventIds = submissionEvents.Select(e => e.Id);
        var submissions = await _context.EventRegistrations.AsNoTracking()
            .Where(r => submissionEventIds.Contains(r.EventId))
            .Where(BusinessReportFilterPolicy.SubmissionTimestampPredicate(filter))
            .Select(r => new BusinessReportRegistration { Id = r.Id, EventId = r.EventId, UserId = r.UserId, RegisteredAt = r.RegisteredAt, Status = r.Status, IsPaid = r.IsPaid })
            .ToListAsync();
        return BusinessReportCalculator.Build(filter, events, cohortRegistrations, submissions, _clock.Today, DateTime.Now, trailScope, organizerScope);
    }
}

public static class BusinessReportCalculator
{
    public static readonly string[] PersistedRegistrationStatuses = ["Pending", "Awaiting Payment", "For Payment Verification", "Accepted", "Rejected", "Alternative Recommended", "Cancelled", "Voided"];

    public static BusinessReportData Build(BusinessReportFilter filter, IEnumerable<BusinessReportEvent> eventSource, IEnumerable<BusinessReportRegistration> registrationSource, IEnumerable<BusinessReportRegistration> submissions, DateOnly today, DateTime generatedAt, string? trailScope = null, string? organizerScope = null)
    {
        var events = eventSource.OrderBy(e => e.EventDate).ThenBy(e => e.EventTitle, StringComparer.Ordinal).ThenBy(e => e.Id).ToList();
        var eventById = events.ToDictionary(e => e.Id);
        var latest = registrationSource.Where(r => eventById.ContainsKey(r.EventId))
            .GroupBy(r => new { r.EventId, r.UserId })
            .Select(g => g.OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id).First())
            .ToList();
        var latestByEvent = latest.GroupBy(r => r.EventId).ToDictionary(g => g.Key, g => g.ToList());
        var accepted = latest.Where(r => r.Status == "Accepted").ToList();
        var active = latest.Where(r => RegistrationStatusHelper.ActiveStatuses.Contains(r.Status)).ToList();
        var completed = events.Count(e => e.Status == "Completed");
        var cancelled = events.Count(e => e.Status == RegistrationStatusHelper.CancelledEventStatus);
        var closed = completed + cancelled;

        var trailRows = events.GroupBy(e => new { e.TrailId, Trail = TrailLabel(e.TrailId, e.TrailNameSnapshot) })
            .OrderBy(g => g.Key.Trail, StringComparer.Ordinal).ThenBy(g => g.Key.TrailId)
            .Select(g =>
            {
                var completeIds = g.Where(e => e.Status == "Completed").Select(e => e.Id).ToHashSet();
                var completedEvents = completeIds.Count;
                var completedAccepted = accepted.Count(r => completeIds.Contains(r.EventId));
                return new BusinessReportTrailRow
                {
                    TrailId = g.Key.TrailId, Trail = g.Key.Trail, TotalEvents = g.Count(), CompletedEvents = completedEvents,
                    AcceptedRegistrations = accepted.Count(r => g.Any(e => e.Id == r.EventId)),
                    AverageAcceptedPerCompletedEvent = completedEvents == 0 ? null : Math.Round((decimal)completedAccepted / completedEvents, 2, MidpointRounding.AwayFromZero)
                };
            }).ToList();

        var eventRows = events.Select(e =>
        {
            var eventRegistrations = latestByEvent.GetValueOrDefault(e.Id) ?? [];
            var occupied = e.Status == RegistrationStatusHelper.CancelledEventStatus ? 0 : eventRegistrations.Count(r => RegistrationStatusHelper.ActiveStatuses.Contains(r.Status));
            return new BusinessReportEventRow
            {
                EventId = e.Id, EventTitle = e.EventTitle, Trail = TrailLabel(e.TrailId, e.TrailNameSnapshot),
                Organizer = OrganizerLabel(e.OrganizerName), EventDate = e.EventDate, Status = StatusLabel(e.Status), Capacity = e.Capacity,
                AcceptedRegistrations = eventRegistrations.Count(r => r.Status == "Accepted"), ActiveOccupiedSlots = occupied,
                OccupancyPercent = e.Capacity > 0 ? Math.Round((decimal)occupied / e.Capacity * 100, 1, MidpointRounding.AwayFromZero) : null
            };
        }).ToList();

        var statusCounts = latest.GroupBy(r => r.Status).ToDictionary(g => g.Key ?? string.Empty, g => g.Count(), StringComparer.Ordinal);
        var statusRows = PersistedRegistrationStatuses.Select(status => new BusinessReportStatusRow { Status = status, Count = statusCounts.GetValueOrDefault(status) }).ToList();
        statusRows.AddRange(statusCounts.Where(x => !PersistedRegistrationStatuses.Contains(x.Key, StringComparer.Ordinal))
            .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new BusinessReportStatusRow { Status = string.IsNullOrWhiteSpace(x.Key) ? "Unexpected blank status" : $"Unexpected: {x.Key}", Count = x.Value, IsUnexpected = true }));
        var eventStatusRows = events.GroupBy(e => e.Status).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new BusinessReportStatusRow { Status = StatusLabel(g.Key), Count = g.Count(), IsUnexpected = g.Key is not "Upcoming" and not "Completed" and not "Cancelled" }).ToList();
        var trend = BuildTrend(filter, submissions);
        var paymentExceptions = latest.Count(r => r.IsPaid && r.Status != "Accepted");

        return new BusinessReportData
        {
            Filter = filter, TrailScope = trailScope ?? "All trails", OrganizerScope = organizerScope ?? "All organizers", GeneratedAt = generatedAt, TotalEvents = events.Count, CompletedEvents = completed,
            AcceptedRegistrations = accepted.Count, UniqueRegisteredParticipants = active.Select(r => r.UserId).Distinct(StringComparer.Ordinal).Count(),
            ClosedEvents = closed, CancelledEvents = cancelled, UpcomingEvents = events.Count(e => e.Status == "Upcoming"),
            ManualClosureEvents = events.Count(e => EventJoinabilityHelper.RequiresManualClosure(new Event { Status = e.Status, EventDate = e.EventDate })),
            ClosedEventCompletionRate = closed == 0 ? null : (int)Math.Round((decimal)completed / closed * 100, MidpointRounding.AwayFromZero),
            VerifiedPaid = latest.Count(r => r.IsPaid && r.Status == "Accepted"),
            AwaitingPayment = latest.Count(r => !r.IsPaid && r.Status == "Awaiting Payment"),
            AwaitingPaymentVerification = latest.Count(r => !r.IsPaid && r.Status == "For Payment Verification"), PaymentDataExceptions = paymentExceptions,
            TrailPerformance = trailRows, EventPerformance = eventRows, StatusBreakdown = statusRows,
            EventStatusDistribution = eventStatusRows, SubmissionTrend = trend,
            TrailChart = BusinessReportPresentation.BuildTrailChart(trailRows)
        };
    }

    public static string TrailLabel(int trailId, string? snapshot) => string.IsNullOrWhiteSpace(snapshot) ? $"Trail #{trailId} (snapshot unavailable)" : snapshot.Trim();
    public static string OrganizerLabel(string? organizer) => string.IsNullOrWhiteSpace(organizer) ? "Organizer unavailable" : organizer.Trim();
    public static string StatusLabel(string? status) => string.IsNullOrWhiteSpace(status) ? "Unexpected blank status" : status.Trim();

    private static IReadOnlyList<BusinessReportTrendPoint> BuildTrend(BusinessReportFilter filter, IEnumerable<BusinessReportRegistration> submissions)
    {
        var start = filter.StartDate!.Value;
        var end = filter.EndDate!.Value;
        var daily = start.Year == end.Year && start.Month == end.Month;
        var counts = submissions.GroupBy(r => daily ? DateOnly.FromDateTime(r.RegisteredAt).ToString("yyyy-MM-dd") : $"{r.RegisteredAt:yyyy-MM}")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (daily)
            return Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i =>
            {
                var day = start.AddDays(i); var key = day.ToString("yyyy-MM-dd");
                return new BusinessReportTrendPoint { Label = day.ToString("MMM d"), Count = counts.GetValueOrDefault(key) };
            }).ToList();
        var month = new DateOnly(start.Year, start.Month, 1);
        var last = new DateOnly(end.Year, end.Month, 1);
        var points = new List<BusinessReportTrendPoint>();
        while (month <= last)
        {
            var key = month.ToString("yyyy-MM");
            points.Add(new BusinessReportTrendPoint { Label = month.ToString("MMM yyyy"), Count = counts.GetValueOrDefault(key) });
            month = month.AddMonths(1);
        }
        return points;
    }
}
