using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers;

[Authorize(Roles = "Admin")]
public sealed class ReportsController : Controller
{
    private readonly IBusinessReportService _reports;
    private readonly IBusinessReportPdfRenderer _pdf;

    public ReportsController(IBusinessReportService reports, IBusinessReportPdfRenderer pdf)
    {
        _reports = reports;
        _pdf = pdf;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] BusinessReportFilter filter)
    {
        var normalized = await _reports.NormalizeAsync(filter, !ModelState.IsValid);
        var model = new BusinessReportViewModel
        {
            Filter = normalized.Filter, Trails = normalized.Trails, Organizers = normalized.Organizers, Errors = normalized.Errors
        };
        if (normalized.Errors.Count == 0)
        {
            var trailScope = normalized.Filter.TrailId.HasValue
                ? normalized.Trails.First(x => x.Value == normalized.Filter.TrailId.Value.ToString()).Label : null;
            var organizerScope = normalized.Filter.OrganizerId is not null
                ? normalized.Organizers.First(x => x.Value == normalized.Filter.OrganizerId).Label : null;
            model = new BusinessReportViewModel
            {
                Filter = normalized.Filter, Trails = normalized.Trails, Organizers = normalized.Organizers,
                Report = await _reports.BuildAsync(normalized.Filter, trailScope, organizerScope)
            };
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] BusinessReportFilter filter)
    {
        var normalized = await _reports.NormalizeAsync(filter, !ModelState.IsValid);
        if (normalized.Errors.Count > 0) return BadRequest(string.Join(" ", normalized.Errors));
        var trailScope = normalized.Filter.TrailId.HasValue ? normalized.Trails.First(x => x.Value == normalized.Filter.TrailId.Value.ToString()).Label : null;
        var organizerScope = normalized.Filter.OrganizerId is not null ? normalized.Organizers.First(x => x.Value == normalized.Filter.OrganizerId).Label : null;
        var report = await _reports.BuildAsync(normalized.Filter, trailScope, organizerScope);
        var filename = $"TrailGuard_Business_Report_{normalized.Filter.StartDate:yyyyMMdd}_{normalized.Filter.EndDate:yyyyMMdd}.pdf";
        return File(_pdf.Render(report), "application/pdf", filename);
    }
}
