using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Services;

namespace TrailGuard.Controllers;

// Aggregate outcome-report shell. Stage 3 preserves the validation presentation so
// Stage 5 can replace its data source without rebuilding the report.
[Authorize(Roles = "Admin")]
public class ReportsController : Controller
{
    public const int MinSampleSize = 20;
    private readonly ApplicationDbContext _context;

    public ReportsController(ApplicationDbContext context) => _context = context;

    public async Task<IActionResult> Index()
    {
        await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);
        var model = new ReportsViewModel
        {
            TotalAssessments = await _context.Assessments.CountAsync(),
            TotalRegistrations = await _context.EventRegistrations.CountAsync(r => r.AssessmentId != null),
            TotalAccepted = await _context.EventRegistrations.CountAsync(r => r.AssessmentId != null && r.Status == "Accepted")
        };

        // Trail fields come from the Event snapshot, never from a live Trail join.
        var rows = await (
            from label in _context.FinalSuitabilityLabels
            join assessment in _context.Assessments on label.AssessmentId equals assessment.Id
            join ev in _context.Events on assessment.EventId equals ev.Id
            select new OutcomeReportRow
            {
                Completed = label.Completed,
                TrailClass = ev.TrailClassSnapshot,
                Difficulty = ev.Difficulty,
                PreHikeLabel = assessment.Result
            }).ToListAsync();

        model.TotalRecordedOutcomes = rows.Count;
        model.TotalResolvedLabels = rows.Count; // Retained report terminology for the Stage 5 replacement.
        model.CompletedCount = rows.Count(r => r.Completed);
        model.NotCompletedCount = rows.Count - model.CompletedCount;
        model.ByNpsBand = BuildGroups(rows.Where(r => !string.IsNullOrEmpty(r.Difficulty))
            .GroupBy(r => r.Difficulty!), g => g.Key);
        model.ByTrailClass = BuildGroups(rows.GroupBy(r => r.TrailClass),
            g => DifficultyCalculator.TrailClassLabel(g.Key));

        // This pathway is a registration behavior, not a label-agreement statistic.
        // Its outcome counts remain useful while the three-category comparisons wait
        // for the Stage 5 metric definition.
        var notRecommended = rows.Where(r => r.PreHikeLabel == "Not Recommended").ToList();
        model.NotRecommendedResolvedCount = notRecommended.Count;
        model.NotRecommendedCompletedCount = notRecommended.Count(r => r.Completed);
        model.NotRecommendedNotCompletedCount = notRecommended.Count - model.NotRecommendedCompletedCount;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export()
    {
        var rows = await (
            from label in _context.FinalSuitabilityLabels
            join assessment in _context.Assessments on label.AssessmentId equals assessment.Id
            join ev in _context.Events on assessment.EventId equals ev.Id
            orderby label.RecordedAt descending
            select new
            {
                label.Id, label.AssessmentId, label.Completed, label.NonCompletionReason,
                label.ParticipantFeedback, label.OrganizerAssessment, label.DifficultyExperience, label.RecordedAt,
                EventId = ev.Id, ev.EventTitle, TrailName = ev.TrailNameSnapshot,
                ev.TrailDistanceKmSnapshot, ev.TrailElevationGainMetersSnapshot, ev.TrailClassSnapshot,
                assessment.Age, assessment.HeightCm, assessment.WeightKg, assessment.MedicalConditions,
                assessment.ExerciseFrequency, assessment.ExerciseType, assessment.CardioEndurance,
                assessment.ExerciseConsistency, assessment.MountainsClimbed, assessment.RecencyOfHike,
                assessment.TrailDifficultyCompleted, assessment.GearItems
            }).ToListAsync();

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("OutcomeId,AssessmentId,EventId,EventTitle,TrailName,TrailDistanceKm,TrailElevationGainM,TrailClass,Completed,NonCompletionReason,ParticipantDifficultyExperience,OrganizerDifficultyExperience,ConservativeDifficultyExperience,RecordedAt,Age,HeightCm,WeightKg,MedicalConditions,ExerciseFrequency,ExerciseType,CardioEndurance,ExerciseConsistency,MountainsClimbed,RecencyOfHike,TrailDifficultyCompleted,GearItems");
        foreach (var r in rows)
            csv.AppendLine(string.Join(",", new[]
            {
                r.Id.ToString(), r.AssessmentId.ToString(), r.EventId.ToString(), Csv(r.EventTitle), Csv(r.TrailName),
                r.TrailDistanceKmSnapshot.ToString(), r.TrailElevationGainMetersSnapshot.ToString(), r.TrailClassSnapshot.ToString(),
                r.Completed.ToString(), Csv(r.NonCompletionReason), Csv(r.ParticipantFeedback), Csv(r.OrganizerAssessment), Csv(r.DifficultyExperience), r.RecordedAt.ToString("O"),
                r.Age?.ToString() ?? "", r.HeightCm?.ToString() ?? "", r.WeightKg?.ToString() ?? "", Csv(r.MedicalConditions),
                Csv(r.ExerciseFrequency), Csv(r.ExerciseType), Csv(r.CardioEndurance), Csv(r.ExerciseConsistency),
                Csv(r.MountainsClimbed), Csv(r.RecencyOfHike), Csv(r.TrailDifficultyCompleted), Csv(r.GearItems)
            }));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv",
            $"OutcomeData_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    private static List<GroupBreakdown> BuildGroups<TKey>(IEnumerable<IGrouping<TKey, OutcomeReportRow>> groups, Func<IGrouping<TKey, OutcomeReportRow>, string> name)
        where TKey : notnull => groups.Select(g => new GroupBreakdown
        {
            GroupName = name(g),
            Stats = new AccuracyBreakdown { Total = g.Count() },
            CompletedCount = g.Count(r => r.Completed),
            NotCompletedCount = g.Count(r => !r.Completed)
        }).ToList();

    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";

    private sealed class OutcomeReportRow
    {
        public bool Completed { get; set; }
        public int TrailClass { get; set; }
        public string? Difficulty { get; set; }
        public string? PreHikeLabel { get; set; }
    }
}

public class ReportsViewModel
{
    public int TotalResolvedLabels { get; set; }
    public int TotalRecordedOutcomes { get; set; }
    public int CompletedCount { get; set; }
    public int NotCompletedCount { get; set; }
    public bool HasEnoughData => TotalRecordedOutcomes >= ReportsController.MinSampleSize;
    public bool HasStage5Metrics => false;

    // Retained data-shape for the Stage 5 validation-metric replacement.
    public AccuracyBreakdown Overall { get; set; } = new();
    public AccuracyBreakdown ModelOnly { get; set; } = new();
    public int[,] ConfusionMatrix { get; set; } = new int[3, 3];
    public int[,] ModelConfusionMatrix { get; set; } = new int[3, 3];
    public double? Kappa { get; set; }
    public double? WeightedKappa { get; set; }
    public double? ModelKappa { get; set; }
    public double? ModelWeightedKappa { get; set; }

    public List<GroupBreakdown> ByNpsBand { get; set; } = new();
    public List<GroupBreakdown> ByTrailClass { get; set; } = new();
    public int TotalAssessments { get; set; }
    public int TotalRegistrations { get; set; }
    public int TotalAccepted { get; set; }
    public int NotRecommendedResolvedCount { get; set; }
    public int NotRecommendedCompletedCount { get; set; }
    public int NotRecommendedNotCompletedCount { get; set; }
    public AccuracyBreakdown NotRecommendedPathway { get; set; } = new();
}

public class AccuracyBreakdown
{
    public int Total { get; set; }
    public int Accurate { get; set; }
    public int OverCautious { get; set; }
    public int MissedRisk { get; set; }
    public int Unclassifiable { get; set; }
    public bool HasEnoughData => Total >= ReportsController.MinSampleSize;
    public double AccuratePct => Total > 0 ? (double)Accurate / Total * 100 : 0;
    public double OverCautiousPct => Total > 0 ? (double)OverCautious / Total * 100 : 0;
    public double MissedRiskPct => Total > 0 ? (double)MissedRisk / Total * 100 : 0;
}

public class GroupBreakdown
{
    public string GroupName { get; set; } = string.Empty;
    public AccuracyBreakdown Stats { get; set; } = new();
    public int CompletedCount { get; set; }
    public int NotCompletedCount { get; set; }
}
