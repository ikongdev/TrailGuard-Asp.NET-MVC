using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    // Aggregate model-validation report — the multi-event counterpart to
    // OrganizerController.EventComparison. Reuses FinalLabelService for every
    // label comparison so the definitions of "accurate" and the ordinal category
    // order can't drift between the per-event and aggregate views.
    //
    // Admin-only: every query in this controller is already system-wide (no
    // OrganizerId scoping anywhere below), so an Organizer previously granted
    // access here saw every other Organizer's data, not just their own — this
    // is an access-control fix, not a data-scope change. A dual-role
    // Admin+Organizer account is still allowed, since it holds the Admin role.
    [Authorize(Roles = "Admin")]
    public class ReportsController : Controller
    {
        // Below this many resolved labels, percentages and kappa are not shown —
        // only raw counts. See DESIGN note in the view for why.
        public const int MinSampleSize = 20;

        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var model = new ReportsViewModel();

            // D. Sampling-bias funnel — computed independently of the label rows below,
            // since it must count assessments/registrations that never produced a label.
            model.TotalAssessments = await _context.Assessments.CountAsync();
            model.TotalRegistrations = await _context.EventRegistrations.CountAsync(r => r.AssessmentId != null);
            model.TotalAccepted = await _context.EventRegistrations.CountAsync(r => r.AssessmentId != null && r.Status == "Accepted");

            var rows = await (
                from label in _context.FinalSuitabilityLabels
                join sr in _context.SuitabilityResults on label.AssessmentId equals sr.AssessmentId into srJoin
                from sr in srJoin.DefaultIfEmpty()
                join ev in _context.Events on label.EventId equals ev.Id into evJoin
                from ev in evJoin.DefaultIfEmpty()
                join trail in _context.Trails on ev!.TrailId equals trail.Id into trailJoin
                from trail in trailJoin.DefaultIfEmpty()
                select new ReportRow
                {
                    PreHikeLabel = label.PreHikeLabel,
                    ModelPreHikeLabel = label.ModelPreHikeLabel,
                    FinalLabel = label.FinalLabel,
                    NpsBand = sr != null ? sr.NpsBand : null,
                    TrailClass = trail != null ? trail.TrailClass : (int?)null
                }
            ).ToListAsync();

            model.TotalResolvedLabels = rows.Count;

            model.Overall = BuildBreakdown(rows.Select(r => (r.PreHikeLabel, r.FinalLabel)));
            model.ModelOnly = BuildBreakdown(rows.Where(r => r.ModelPreHikeLabel != null).Select(r => (r.ModelPreHikeLabel, r.FinalLabel)));

            model.ConfusionMatrix = BuildConfusionMatrix(rows.Select(r => (r.PreHikeLabel, r.FinalLabel)));
            model.ModelConfusionMatrix = BuildConfusionMatrix(rows.Where(r => r.ModelPreHikeLabel != null).Select(r => (r.ModelPreHikeLabel, r.FinalLabel)));

            if (model.TotalResolvedLabels >= MinSampleSize)
            {
                (model.Kappa, model.WeightedKappa) = FinalLabelService.ComputeKappa(model.ConfusionMatrix);
            }
            if (model.ModelOnly.Total >= MinSampleSize)
            {
                (model.ModelKappa, model.ModelWeightedKappa) = FinalLabelService.ComputeKappa(model.ModelConfusionMatrix);
            }

            model.ByNpsBand = rows
                .Where(r => !string.IsNullOrEmpty(r.NpsBand))
                .GroupBy(r => r.NpsBand!)
                .Select(g => new GroupBreakdown
                {
                    GroupName = g.Key,
                    Stats = BuildBreakdown(g.Select(r => (r.PreHikeLabel, r.FinalLabel)))
                })
                .OrderBy(g => Array.IndexOf(DifficultyCalculator.Bands, g.GroupName) is var i && i >= 0 ? i : int.MaxValue)
                .ToList();

            model.ByTrailClass = rows
                .Where(r => r.TrailClass.HasValue)
                .GroupBy(r => r.TrailClass!.Value)
                .OrderBy(g => g.Key) // Walking(1) -> Hiking(2) -> Scrambling(3) -> Simple Climbing(4); alphabetical on the label would scramble that order.
                .Select(g => new GroupBreakdown
                {
                    GroupName = DifficultyCalculator.TrailClassLabel(g.Key),
                    Stats = BuildBreakdown(g.Select(r => (r.PreHikeLabel, r.FinalLabel)))
                })
                .ToList();

            // D8. The "Not Recommended" acknowledgement pathway — the only evidence the
            // system has about whether its negative predictions were correct, since every
            // other Not-Recommended participant never registered or was rejected.
            model.NotRecommendedResolvedCount = rows.Count(r => r.PreHikeLabel == "Not Recommended");
            model.NotRecommendedPathway = BuildBreakdown(
                rows.Where(r => r.PreHikeLabel == "Not Recommended").Select(r => (r.PreHikeLabel, r.FinalLabel)));

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var rows = await (
                from label in _context.FinalSuitabilityLabels
                join reg in _context.EventRegistrations on label.RegistrationId equals reg.Id
                join assessment in _context.Assessments on label.AssessmentId equals assessment.Id
                join sr in _context.SuitabilityResults on label.AssessmentId equals sr.AssessmentId into srJoin
                from sr in srJoin.DefaultIfEmpty()
                join ev in _context.Events on label.EventId equals ev.Id into evJoin
                from ev in evJoin.DefaultIfEmpty()
                join trail in _context.Trails on ev!.TrailId equals trail.Id into trailJoin
                from trail in trailJoin.DefaultIfEmpty()
                orderby label.ResolvedAt descending
                select new
                {
                    label.Id,
                    label.EventId,
                    EventTitle = ev != null ? ev.EventTitle : "",
                    TrailName = trail != null ? trail.Name : "",
                    TrailDistanceKm = trail != null ? trail.DistanceKm : (double?)null,
                    TrailElevationGainMeters = trail != null ? trail.ElevationGainMeters : (int?)null,
                    TrailClass = trail != null ? trail.TrailClass : (int?)null,
                    assessment.Age,
                    assessment.HeightCm,
                    assessment.WeightKg,
                    assessment.MedicalConditions,
                    assessment.ExerciseFrequency,
                    assessment.ExerciseType,
                    assessment.CardioEndurance,
                    assessment.ExerciseConsistency,
                    assessment.MountainsClimbed,
                    assessment.RecencyOfHike,
                    assessment.TrailDifficultyCompleted,
                    assessment.GearItems,
                    NpsScore = sr != null ? sr.NpsScore : (double?)null,
                    NpsBand = sr != null ? sr.NpsBand : null,
                    ConfidenceScore = sr != null ? sr.ConfidenceScore : (double?)null,
                    label.PreHikeLabel,
                    label.ModelPreHikeLabel,
                    label.ParticipantFeedback,
                    label.OrganizerAssessment,
                    label.FinalLabel,
                    label.ResolvedAt
                }
            ).ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("LabelId,EventId,EventTitle,TrailName,TrailDistanceKm,TrailElevationGainM,TrailClass," +
                "Age,HeightCm,WeightKg,MedicalConditions,ExerciseFrequency,ExerciseType,CardioEndurance,ExerciseConsistency," +
                "MountainsClimbed,RecencyOfHike,TrailDifficultyCompleted,GearItems,NpsScore,NpsBand,ConfidenceScore," +
                "PreHikeLabel,ModelPreHikeLabel,ParticipantFeedback,OrganizerAssessment,FinalLabel,ResolvedAt");

            foreach (var r in rows)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    r.Id.ToString(),
                    r.EventId.ToString(),
                    Csv(r.EventTitle),
                    Csv(r.TrailName),
                    r.TrailDistanceKm?.ToString() ?? "",
                    r.TrailElevationGainMeters?.ToString() ?? "",
                    r.TrailClass?.ToString() ?? "",
                    r.Age?.ToString() ?? "",
                    r.HeightCm?.ToString() ?? "",
                    r.WeightKg?.ToString() ?? "",
                    Csv(r.MedicalConditions),
                    Csv(r.ExerciseFrequency),
                    Csv(r.ExerciseType),
                    Csv(r.CardioEndurance),
                    Csv(r.ExerciseConsistency),
                    Csv(r.MountainsClimbed),
                    Csv(r.RecencyOfHike),
                    Csv(r.TrailDifficultyCompleted),
                    Csv(r.GearItems),
                    r.NpsScore?.ToString() ?? "",
                    Csv(r.NpsBand),
                    r.ConfidenceScore?.ToString() ?? "",
                    Csv(r.PreHikeLabel),
                    Csv(r.ModelPreHikeLabel),
                    Csv(r.ParticipantFeedback),
                    Csv(r.OrganizerAssessment),
                    Csv(r.FinalLabel),
                    r.ResolvedAt.ToString("O")
                }));
            }

            var fileName = $"ModelValidation_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", fileName);
        }

        private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";

        private static AccuracyBreakdown BuildBreakdown(IEnumerable<(string? predicted, string? final_)> pairs)
        {
            var breakdown = new AccuracyBreakdown();
            foreach (var (predicted, final_) in pairs)
            {
                var classification = FinalLabelService.ClassifyAccuracy(predicted, final_);
                switch (classification)
                {
                    case "Accurate": breakdown.Accurate++; break;
                    case "Over-cautious": breakdown.OverCautious++; break;
                    case "Missed risk": breakdown.MissedRisk++; break;
                    default: breakdown.Unclassifiable++; break;
                }
                breakdown.Total++;
            }
            return breakdown;
        }

        private static int[,] BuildConfusionMatrix(IEnumerable<(string? predicted, string? final_)> pairs)
        {
            var k = FinalLabelService.LabelCategories.Length;
            var matrix = new int[k, k];
            foreach (var (predicted, final_) in pairs)
            {
                var i = FinalLabelService.LabelOrder(predicted);
                var j = FinalLabelService.LabelOrder(final_);
                if (i.HasValue && j.HasValue) matrix[i.Value, j.Value]++;
            }
            return matrix;
        }

        private class ReportRow
        {
            public string? PreHikeLabel { get; set; }
            public string? ModelPreHikeLabel { get; set; }
            public string? FinalLabel { get; set; }
            public string? NpsBand { get; set; }
            public int? TrailClass { get; set; }
        }
    }

    public class ReportsViewModel
    {
        public int TotalResolvedLabels { get; set; }
        public bool HasEnoughData => TotalResolvedLabels >= ReportsController.MinSampleSize;

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
        public AccuracyBreakdown NotRecommendedPathway { get; set; } = new();
    }

    public class AccuracyBreakdown
    {
        public int Total { get; set; }
        public int Accurate { get; set; }

        // Predicted harder than it turned out. Inefficient, not unsafe.
        public int OverCautious { get; set; }

        // Predicted easier than it turned out — the failure mode the system exists to
        // prevent. A participant was told they were ready and was not.
        public int MissedRisk { get; set; }

        public int Unclassifiable { get; set; }

        public bool HasEnoughData => Total >= ReportsController.MinSampleSize;

        public double AccuratePct => Total > 0 ? (double)Accurate / Total * 100 : 0;
        public double OverCautiousPct => Total > 0 ? (double)OverCautious / Total * 100 : 0;
        public double MissedRiskPct => Total > 0 ? (double)MissedRisk / Total * 100 : 0;
    }

    public class GroupBreakdown
    {
        public string GroupName { get; set; } = "";
        public AccuracyBreakdown Stats { get; set; } = new();
    }
}
