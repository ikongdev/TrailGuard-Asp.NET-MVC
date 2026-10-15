using System.Text.Json.Serialization;

namespace TrailGuard.Models;

public sealed class BusinessReportFilter
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? TrailId { get; set; }
    public string? OrganizerId { get; set; }
}

public sealed class BusinessReportViewModel
{
    public BusinessReportFilter Filter { get; init; } = new();
    public IReadOnlyList<BusinessReportFilterOption> Trails { get; init; } = [];
    public IReadOnlyList<BusinessReportFilterOption> Organizers { get; init; } = [];
    public BusinessReportData? Report { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class BusinessReportFilterOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class BusinessReportData
{
    public required BusinessReportFilter Filter { get; init; }
    public string TrailScope { get; init; } = "All trails";
    public string OrganizerScope { get; init; } = "All organizers";
    public required DateTime GeneratedAt { get; init; }
    public int TotalEvents { get; init; }
    public int CompletedEvents { get; init; }
    public int AcceptedRegistrations { get; init; }
    public int UniqueRegisteredParticipants { get; init; }
    public int ClosedEvents { get; init; }
    public int CancelledEvents { get; init; }
    public int UpcomingEvents { get; init; }
    public int ManualClosureEvents { get; init; }
    public int? ClosedEventCompletionRate { get; init; }
    public int VerifiedPaid { get; init; }
    public int AwaitingPayment { get; init; }
    public int AwaitingPaymentVerification { get; init; }
    public int PaymentDataExceptions { get; init; }
    public IReadOnlyList<BusinessReportTrailRow> TrailPerformance { get; init; } = [];
    public IReadOnlyList<BusinessReportEventRow> EventPerformance { get; init; } = [];
    public IReadOnlyList<BusinessReportStatusRow> StatusBreakdown { get; init; } = [];
    public IReadOnlyList<BusinessReportStatusRow> EventStatusDistribution { get; init; } = [];
    public IReadOnlyList<BusinessReportTrendPoint> SubmissionTrend { get; init; } = [];
    public BusinessReportTrailChart TrailChart { get; init; } = new();
}

public sealed class BusinessReportTrailChart
{
    public IReadOnlyList<BusinessReportTrailChartEntry> Entries { get; init; } = [];
    public int FullScopeAcceptedRegistrations { get; init; }
    public int OmittedTrailCount { get; init; }
    public bool IsAggregated => OmittedTrailCount > 0;
}

public sealed class BusinessReportTrailChartEntry
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool IsOther { get; init; }
}

public sealed class BusinessReportTrailRow
{
    public int TrailId { get; init; }
    public string Trail { get; init; } = string.Empty;
    public int TotalEvents { get; init; }
    public int CompletedEvents { get; init; }
    public int AcceptedRegistrations { get; init; }
    public decimal? AverageAcceptedPerCompletedEvent { get; init; }
}

public sealed class BusinessReportEventRow
{
    public int EventId { get; init; }
    public string EventTitle { get; init; } = string.Empty;
    public string Trail { get; init; } = string.Empty;
    public string Organizer { get; init; } = string.Empty;
    public DateTime EventDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public int AcceptedRegistrations { get; init; }
    public int ActiveOccupiedSlots { get; init; }
    public decimal? OccupancyPercent { get; init; }
}

public sealed class BusinessReportStatusRow
{
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool IsUnexpected { get; init; }
}

public sealed class BusinessReportTrendPoint
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Deliberate JSON boundary between the report calculation and the browser charts.
/// Property names are fixed here so Razor and JavaScript do not depend on serializer defaults.
/// </summary>
public sealed class BusinessReportChartPayload
{
    [JsonPropertyName("submissions")]
    public IReadOnlyList<BusinessReportChartPoint> Submissions { get; init; } = [];

    [JsonPropertyName("trails")]
    public IReadOnlyList<BusinessReportTrailChartPoint> Trails { get; init; } = [];

    [JsonPropertyName("trailChartTotal")]
    public int TrailChartTotal { get; init; }

    [JsonPropertyName("trailChartIsAggregated")]
    public bool TrailChartIsAggregated { get; init; }

    [JsonPropertyName("statuses")]
    public IReadOnlyList<BusinessReportStatusChartPoint> Statuses { get; init; } = [];

    public static BusinessReportChartPayload From(BusinessReportData report) => new()
    {
        Submissions = report.SubmissionTrend.Select(point => new BusinessReportChartPoint { Label = point.Label, Count = point.Count }).ToList(),
        Trails = report.TrailChart.Entries.Select(entry => new BusinessReportTrailChartPoint { Label = entry.Label, Count = entry.Count, IsOther = entry.IsOther }).ToList(),
        TrailChartTotal = report.TrailChart.FullScopeAcceptedRegistrations,
        TrailChartIsAggregated = report.TrailChart.IsAggregated,
        Statuses = report.EventStatusDistribution.Select(entry => new BusinessReportStatusChartPoint { Status = entry.Status, Count = entry.Count }).ToList()
    };
}

public sealed class BusinessReportChartPoint
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed class BusinessReportTrailChartPoint
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("isOther")]
    public bool IsOther { get; init; }
}

public sealed class BusinessReportStatusChartPoint
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed class BusinessReportEvent
{
    public int Id { get; init; }
    public int TrailId { get; init; }
    public string TrailNameSnapshot { get; init; } = string.Empty;
    public string EventTitle { get; init; } = string.Empty;
    public DateTime EventDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Capacity { get; init; }
    public string? OrganizerId { get; init; }
    public string? OrganizerName { get; init; }
}

public sealed class BusinessReportRegistration
{
    public int Id { get; init; }
    public int EventId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public DateTime RegisteredAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsPaid { get; init; }
}
