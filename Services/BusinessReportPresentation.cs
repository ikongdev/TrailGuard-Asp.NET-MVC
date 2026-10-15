using TrailGuard.Models;

namespace TrailGuard.Services;

/// <summary>Creates the same bounded trail-chart presentation for HTML and PDF reports.</summary>
public static class BusinessReportPresentation
{
    private const int TrailChartLimit = 8;

    public static BusinessReportTrailChart BuildTrailChart(IEnumerable<BusinessReportTrailRow> trails)
    {
        var ordered = trails
            .OrderByDescending(trail => trail.AcceptedRegistrations)
            .ThenBy(trail => trail.Trail, StringComparer.Ordinal)
            .ThenBy(trail => trail.TrailId)
            .ToList();
        var total = ordered.Sum(trail => trail.AcceptedRegistrations);
        var entries = ordered.Take(TrailChartLimit)
            .Select(trail => new BusinessReportTrailChartEntry { Label = trail.Trail, Count = trail.AcceptedRegistrations })
            .ToList();

        if (ordered.Count > TrailChartLimit)
        {
            var omitted = ordered.Skip(TrailChartLimit).ToList();
            entries.Add(new BusinessReportTrailChartEntry
            {
                Label = $"Other ({omitted.Count} trails)",
                Count = omitted.Sum(trail => trail.AcceptedRegistrations),
                IsOther = true
            });
        }

        return new BusinessReportTrailChart
        {
            Entries = entries,
            FullScopeAcceptedRegistrations = total,
            OmittedTrailCount = Math.Max(0, ordered.Count - TrailChartLimit)
        };
    }
}
