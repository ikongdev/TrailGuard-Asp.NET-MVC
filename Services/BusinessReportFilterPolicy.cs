using System.Linq.Expressions;
using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>
/// Reports compare the persisted wall-clock values in PostgreSQL timestamp-without-time-zone columns.
/// EventDate is a scheduled calendar value; RegisteredAt is a stored local timestamp created by the
/// registration writer. PhilippineClock supplies only the default calendar month. No timezone conversion
/// is applied because historical source timezones are not stored and therefore cannot be reconstructed.
/// </summary>
public static class BusinessReportFilterPolicy
{
    public static BusinessReportFilter ApplyDefaults(BusinessReportFilter requested, DateOnly today) => new()
    {
        StartDate = requested.StartDate ?? new DateOnly(today.Year, today.Month, 1),
        EndDate = requested.EndDate ?? new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
        TrailId = requested.TrailId,
        OrganizerId = string.IsNullOrWhiteSpace(requested.OrganizerId) ? null : requested.OrganizerId.Trim()
    };

    public static IReadOnlyList<string> ValidateDates(BusinessReportFilter filter, bool malformedDate)
    {
        var errors = new List<string>();
        if (malformedDate) errors.Add("Enter valid calendar dates.");
        if (filter.StartDate.HasValue && filter.EndDate.HasValue && filter.EndDate < filter.StartDate)
            errors.Add("The end date must not be before the start date.");
        return errors;
    }

    public static (DateTime Start, DateTime EndExclusive) StoredTimestampRange(BusinessReportFilter filter)
    {
        if (!filter.StartDate.HasValue || !filter.EndDate.HasValue)
            throw new ArgumentException("A normalized report filter requires both dates.", nameof(filter));

        return (filter.StartDate.Value.ToDateTime(TimeOnly.MinValue), filter.EndDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }

    public static Expression<Func<Event, bool>> EventCohortPredicate(BusinessReportFilter filter)
    {
        var (start, endExclusive) = StoredTimestampRange(filter);
        return eventItem => eventItem.EventDate >= start && eventItem.EventDate < endExclusive
            && (!filter.TrailId.HasValue || eventItem.TrailId == filter.TrailId.Value)
            && (filter.OrganizerId == null || eventItem.OrganizerId == filter.OrganizerId);
    }

    public static Expression<Func<Event, bool>> SubmissionEventScopePredicate(BusinessReportFilter filter) =>
        eventItem => (!filter.TrailId.HasValue || eventItem.TrailId == filter.TrailId.Value)
            && (filter.OrganizerId == null || eventItem.OrganizerId == filter.OrganizerId);

    public static Expression<Func<EventRegistration, bool>> SubmissionTimestampPredicate(BusinessReportFilter filter)
    {
        var (start, endExclusive) = StoredTimestampRange(filter);
        return registration => registration.RegisteredAt >= start && registration.RegisteredAt < endExclusive;
    }
}
