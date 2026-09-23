using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Services;

namespace TrailGuard.Controllers;



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


        var rows = await (
            from label in _context.FinalSuitabilityLabels
            join assessment in _context.Assessments on label.AssessmentId equals assessment.Id
            join result in _context.SuitabilityResults on assessment.Id equals result.AssessmentId
            join ev in _context.Events on assessment.EventId equals ev.Id
            select new OutcomeReportRow
            {
                Completed = label.Completed,
                TrailClass = ev.TrailClassSnapshot,
                Difficulty = ev.Difficulty,
                PreHikeLabel = assessment.Result,
                PredictedLabel = result.PredictedLabel,
                CompletionProbability = result.CompletionProbability,
                NonCompletionReason = label.NonCompletionReason
            }).ToListAsync();

        model.TotalRecordedOutcomes = rows.Count;
        model.TotalResolvedLabels = rows.Count;
        model.CompletedCount = rows.Count(r => r.Completed);
        model.NotCompletedCount = rows.Count - model.CompletedCount;
        model.ByTrailClass = BuildGroups(rows.GroupBy(r => r.TrailClass),
            g => DifficultyCalculator.TrailClassLabel(g.Key));




        var notRecommended = rows.Where(r => r.PreHikeLabel == "Not Recommended").ToList();
        model.NotRecommendedResolvedCount = notRecommended.Count;
        model.NotRecommendedCompletedCount = notRecommended.Count(r => r.Completed);
        model.NotRecommendedNotCompletedCount = notRecommended.Count - model.NotRecommendedCompletedCount;
        model.ConfusionMatrix = new int[3, 2];
        foreach (var row in rows) model.ConfusionMatrix[LabelIndex(row.PredictedLabel), row.Completed ? 0 : 1]++;
        model.CalibrationBands = Enumerable.Range(0, 10).Select(i => BuildRate(rows.Where(r => r.CompletionProbability >= i / 10d && (i == 9 ? r.CompletionProbability <= 1 : r.CompletionProbability < (i + 1) / 10d)), $"{i * 10}-{(i + 1) * 10}%")).ToList();
        model.GoodMatchSafety = BuildRate(rows.Where(r => LabelIndex(r.PredictedLabel) == 0), "Good Match");
        model.ThresholdBands = new List<OutcomeRate> { BuildRate(rows.Where(r => r.CompletionProbability < .30), "Below 30%"), BuildRate(rows.Where(r => r.CompletionProbability >= .30 && r.CompletionProbability < .80), "30-80%"), BuildRate(rows.Where(r => r.CompletionProbability >= .80), "80%+") };
        model.NonCompletionReasons = rows.Where(r => !r.Completed).GroupBy(r => r.NonCompletionReason).Select(g => new ReasonCount { Reason = g.Key, Count = g.Count() }).ToList();
        var exportSummary = BuildExportRows(await ExportSource().ToListAsync());
        model.ExportIncludedCount = exportSummary.Included.Count;
        model.ExportNonReadinessExcludedCount = exportSummary.NonReadinessExcludedCount;
        model.ExportIncompleteCaseKeys = exportSummary.IncompleteCaseKeys;

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export()
    {
        var data = BuildExportRows(await ExportSource().ToListAsync());
        var csv = new System.Text.StringBuilder("case_id,participant_profile_id,trail_id,bmi,exercise_frequency,cardio_duration,exercise_consistency,hiking_experience,last_hike_recency,hardest_trail_completed,gear_score,has_asthma,has_cvd,has_joint_knee_injury,has_signs_symptoms,distance_km,elevation_gain_m,trail_class,typical_duration_hours,completed\n");
        foreach (var r in data.Included) csv.AppendLine(string.Join(",", r.Select(Csv)));
        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv",
            $"TrailGuard_Retraining_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    private IQueryable<ExportSourceRow> ExportSource() => from l in _context.FinalSuitabilityLabels join a in _context.Assessments on l.AssessmentId equals a.Id join e in _context.Events on a.EventId equals e.Id join u in _context.Users on a.UserId equals u.Id select new ExportSourceRow { Id=l.Id, Profile=u.PublicProfileId, Trail=e.TrailId, Completed=l.Completed, Reason=l.NonCompletionReason, Height=a.HeightCm, Weight=a.WeightKg, Medical=a.MedicalConditions, Frequency=a.ExerciseFrequency, Cardio=a.CardioEndurance, Consistency=a.ExerciseConsistency, Experience=a.MountainsClimbed, Recency=a.RecencyOfHike, Hardest=a.TrailDifficultyCompleted, Gear=a.GearItems, Distance=e.TrailDistanceKmSnapshot, Elevation=e.TrailElevationGainMetersSnapshot, TrailClass=e.TrailClassSnapshot, Duration=e.TrailDurationHoursSnapshot };
    private static ExportData BuildExportRows(IEnumerable<ExportSourceRow> rows) { var d=new ExportData(); foreach(var r in rows) { var key=$"TG-{r.Id}"; if(!r.Completed && (r.Reason=="External"||r.Reason=="Withdrawal")){d.NonReadinessExcludedCount++;continue;} if(!Valid(r)){d.IncompleteCaseKeys.Add(key);continue;} var m=r.Medical??""; var bmi=Math.Round(r.Weight!.Value/Math.Pow(r.Height!.Value/100,2),2); d.Included.Add(new[]{key,r.Profile.ToString("N"),r.Trail.ToString(),bmi.ToString(System.Globalization.CultureInfo.InvariantCulture),r.Frequency!,r.Cardio!,r.Consistency!,r.Experience!,r.Recency!,r.Hardest!,Gear(r.Gear).ToString(),Flag(m,"Asthma").ToString(),Flag(m,"Hypertension").ToString(),Flag(m,"Joint or knee").ToString(),((Flag(m,"Vertigo")+Flag(m,"Chest pain")+Flag(m,"Shortness of breath"))>0?1:0).ToString(),r.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture),r.Elevation.ToString(),r.TrailClass.ToString(),((double)r.Duration).ToString(System.Globalization.CultureInfo.InvariantCulture),r.Completed?"Yes":"No"}); } return d; }
    private static bool Valid(ExportSourceRow r) => r.Height is > 0 && r.Weight is > 0 && r.Distance>0 && r.Elevation>=0 && r.Duration>0 && r.TrailClass is >=1 and <=4 && new[]{"Sedentary","1-2x","3-4x","5+ times per week"}.Contains(r.Frequency) && new[]{"<15min","15-29min","30-60min",">60min"}.Contains(r.Cardio) && new[]{"<1 month","1-2 months","3+ months"}.Contains(r.Consistency) && new[]{"First-timer","1-3","4-10","10+ mountains"}.Contains(r.Experience) && new[]{"Never",">1yr ago","4-12 months ago","1-3 months ago"}.Contains(r.Recency) && new[]{"None","Minor day hikes","Major w/ steep sections","Multi-day"}.Contains(r.Hardest);
    private static int Gear(string? s) => string.IsNullOrWhiteSpace(s)?0:s.Split(',').Count(x=>!string.IsNullOrWhiteSpace(x)&&!x.Trim().Equals("None of the above",StringComparison.OrdinalIgnoreCase));
    private static int Flag(string s,string v)=>s.Contains(v,StringComparison.OrdinalIgnoreCase)?1:0;

    private static List<GroupBreakdown> BuildGroups<TKey>(IEnumerable<IGrouping<TKey, OutcomeReportRow>> groups, Func<IGrouping<TKey, OutcomeReportRow>, string> name)
        where TKey : notnull => groups.Select(g => new GroupBreakdown
        {
            GroupName = name(g),
            Stats = new AccuracyBreakdown { Total = g.Count() },
            CompletedCount = g.Count(r => r.Completed),
            NotCompletedCount = g.Count(r => !r.Completed)
        }).ToList();

    private static string Csv(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    private static int LabelIndex(string? label) => label switch { "Good Match" or "Good-Match" => 0, "Borderline" => 1, _ => 2 };
    private static OutcomeRate BuildRate(IEnumerable<OutcomeReportRow> source, string label) { var rows = source.ToList(); return new OutcomeRate { Label = label, Total = rows.Count, Completed = rows.Count(r => r.Completed) }; }

    private sealed class OutcomeReportRow
    {
        public bool Completed { get; set; }
        public int TrailClass { get; set; }
        public string? Difficulty { get; set; }
        public string? PreHikeLabel { get; set; }
        public string? PredictedLabel { get; set; }
        public double CompletionProbability { get; set; }
        public string NonCompletionReason { get; set; } = string.Empty;
    }
    private sealed class ExportData { public List<string[]> Included { get; }=[]; public int NonReadinessExcludedCount { get; set; } public List<string> IncompleteCaseKeys { get; }=[]; }
    private sealed class ExportSourceRow { public int Id { get; set; } public Guid Profile { get; set; } public int Trail { get; set; } public bool Completed { get; set; } public string Reason { get; set; }=""; public double? Height { get; set; } public double? Weight { get; set; } public string? Medical { get; set; } public string? Frequency { get; set; } public string? Cardio { get; set; } public string? Consistency { get; set; } public string? Experience { get; set; } public string? Recency { get; set; } public string? Hardest { get; set; } public string? Gear { get; set; } public double Distance { get; set; } public int Elevation { get; set; } public int TrailClass { get; set; } public decimal Duration { get; set; } }
}

public class ReportsViewModel
{
    public int TotalResolvedLabels { get; set; }
    public int TotalRecordedOutcomes { get; set; }
    public int CompletedCount { get; set; }
    public int NotCompletedCount { get; set; }
    public bool HasEnoughData => TotalRecordedOutcomes >= ReportsController.MinSampleSize;
    public bool HasStage5Metrics => false;


    public AccuracyBreakdown Overall { get; set; } = new();
    public AccuracyBreakdown ModelOnly { get; set; } = new();
    public int[,] ConfusionMatrix { get; set; } = new int[3, 3];
    public int[,] ModelConfusionMatrix { get; set; } = new int[3, 3];
    public double? Kappa { get; set; }
    public double? WeightedKappa { get; set; }
    public double? ModelKappa { get; set; }
    public double? ModelWeightedKappa { get; set; }

    public List<GroupBreakdown> ByTrailClass { get; set; } = new();
    public int TotalAssessments { get; set; }
    public int TotalRegistrations { get; set; }
    public int TotalAccepted { get; set; }
    public int NotRecommendedResolvedCount { get; set; }
    public int NotRecommendedCompletedCount { get; set; }
    public int NotRecommendedNotCompletedCount { get; set; }
    public AccuracyBreakdown NotRecommendedPathway { get; set; } = new();
    public int ExportIncludedCount { get; set; }
    public int ExportNonReadinessExcludedCount { get; set; }
    public List<string> ExportIncompleteCaseKeys { get; set; } = new();
    public List<OutcomeRate> CalibrationBands { get; set; } = new();
    public OutcomeRate GoodMatchSafety { get; set; } = new();
    public List<OutcomeRate> ThresholdBands { get; set; } = new();
    public List<ReasonCount> NonCompletionReasons { get; set; } = new();
}
public class OutcomeRate { public string Label { get; set; } = string.Empty; public int Total { get; set; } public int Completed { get; set; } public int NotCompleted => Total - Completed; public bool HasEnoughData => Total >= ReportsController.MinSampleSize; public double CompletionRate => Total == 0 ? 0 : (double)Completed / Total * 100; }
public class ReasonCount { public string Reason { get; set; } = string.Empty; public int Count { get; set; } }

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
