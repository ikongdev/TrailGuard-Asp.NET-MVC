using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Admin,Organizer")]
    public class RecordsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RecordsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string searchString, string dateFrom, string dateTo)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["DateFrom"] = dateFrom;
            ViewData["DateTo"] = dateTo;

            var model = new RecordsViewModel();

            // Parse dates
            DateTime? fromDate = null;
            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(dateFrom))
                fromDate = DateTime.Parse(dateFrom);
            if (!string.IsNullOrEmpty(dateTo))
                toDate = DateTime.Parse(dateTo).AddDays(1);

            // Event Records
            var eventsQuery = _context.Events
                .Include(e => e.Trail)
                .AsQueryable();

            if (fromDate.HasValue)
                eventsQuery = eventsQuery.Where(e => e.EventDate >= fromDate.Value);
            if (toDate.HasValue)
                eventsQuery = eventsQuery.Where(e => e.EventDate < toDate.Value);
            if (!string.IsNullOrEmpty(searchString))
                eventsQuery = eventsQuery.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));

            model.Events = await eventsQuery.OrderByDescending(e => e.EventDate).ToListAsync();

            // Participant Registrations
            var registrationsQuery = _context.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.User)
                .AsQueryable();

            if (fromDate.HasValue)
                registrationsQuery = registrationsQuery.Where(r => r.RegisteredAt >= fromDate.Value);
            if (toDate.HasValue)
                registrationsQuery = registrationsQuery.Where(r => r.RegisteredAt < toDate.Value);
            if (!string.IsNullOrEmpty(searchString))
                registrationsQuery = registrationsQuery.Where(r => 
                    r.ParticipantName.Contains(searchString) || 
                    (r.Event != null && r.Event.EventTitle.Contains(searchString)));

            model.Registrations = await registrationsQuery.OrderByDescending(r => r.RegisteredAt).ToListAsync();

            // Trail Usage
            var completedEvents = await _context.Events
                .Where(e => e.Status == "Completed")
                .Include(e => e.Trail)
                .ToListAsync();

            model.TrailUsage = completedEvents
                .GroupBy(e => e.TrailId)
                .Select(g => new TrailUsageViewModel
                {
                    TrailId = g.Key,
                    TrailName = g.First().Trail?.Name ?? "Unknown Trail",
                    TotalEvents = g.Count(),
                    TotalParticipants = _context.EventRegistrations.Count(r => g.Select(e => e.Id).Contains(r.EventId))
                })
                .OrderByDescending(t => t.TotalEvents)
                .ToList();

            // Financial Records
            model.FinancialRecords = await _context.Events
                .Where(e => e.Status == "Completed")
                .Select(e => new FinancialRecordViewModel
                {
                    EventId = e.Id,
                    EventTitle = e.EventTitle,
                    EventDate = e.EventDate,
                    RegistrationFee = ExtractRegistrationFee(e.PaymentDetails ?? ""),
                    TotalParticipants = _context.EventRegistrations.Count(r => r.EventId == e.Id),
                    TotalRevenue = ExtractRegistrationFee(e.PaymentDetails ?? "") * _context.EventRegistrations.Count(r => r.EventId == e.Id)
                })
                .OrderByDescending(f => f.EventDate)
                .ToListAsync();

            // Feedback & Ratings
            model.Feedbacks = await _context.EventFeedbacks
                .Include(f => f.Event)
                .Include(f => f.User)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Export(string searchString, string dateFrom, string dateTo)
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
            if (!string.IsNullOrEmpty(dateFrom))
                fromDate = DateTime.Parse(dateFrom);
            if (!string.IsNullOrEmpty(dateTo))
                toDate = DateTime.Parse(dateTo).AddDays(1);

            var csv = new System.Text.StringBuilder();

            csv.AppendLine("=== EVENT HISTORY ===");
            csv.AppendLine("Event Title,Trail,Date,Status,Capacity,Registered");

            var eventsQuery = _context.Events.Include(e => e.Trail).AsQueryable();
            if (fromDate.HasValue)
                eventsQuery = eventsQuery.Where(e => e.EventDate >= fromDate.Value);
            if (toDate.HasValue)
                eventsQuery = eventsQuery.Where(e => e.EventDate < toDate.Value);
            if (!string.IsNullOrEmpty(searchString))
                eventsQuery = eventsQuery.Where(e => e.EventTitle.Contains(searchString) || e.Location.Contains(searchString));

            var events = await eventsQuery.OrderByDescending(e => e.EventDate).ToListAsync();
            foreach (var e in events)
            {
                var registeredCount = _context.EventRegistrations.Count(r => r.EventId == e.Id);
                csv.AppendLine($"\"{e.EventTitle}\",\"{e.Trail?.Name}\",{e.EventDate:MMM dd, yyyy},{e.Status},{e.Capacity},{registeredCount}");
            }

            csv.AppendLine("");
            csv.AppendLine("");

            csv.AppendLine("=== PARTICIPANT REGISTRATIONS ===");
            csv.AppendLine("Participant Name,Event,Pickup Point,Registered At,Payment Status");

            var regQuery = _context.EventRegistrations.Include(r => r.Event).AsQueryable();
            if (fromDate.HasValue)
                regQuery = regQuery.Where(r => r.RegisteredAt >= fromDate.Value);
            if (toDate.HasValue)
                regQuery = regQuery.Where(r => r.RegisteredAt < toDate.Value);
            if (!string.IsNullOrEmpty(searchString))
                regQuery = regQuery.Where(r => r.ParticipantName.Contains(searchString) || (r.Event != null && r.Event.EventTitle.Contains(searchString)));

            var registrations = await regQuery.OrderByDescending(r => r.RegisteredAt).ToListAsync();
            foreach (var r in registrations)
            {
                csv.AppendLine($"\"{r.ParticipantName}\",\"{r.Event?.EventTitle}\",\"{r.PickupPoint}\",{r.RegisteredAt:MMM dd, yyyy},{ (r.IsPaid ? "Paid" : "Pending") }");
            }

            csv.AppendLine("");
            csv.AppendLine("");

            csv.AppendLine("=== TRAIL USAGE & POPULARITY ===");
            csv.AppendLine("Trail Name,Total Events,Total Participants");

            var completedEvents = await _context.Events
                .Where(e => e.Status == "Completed")
                .Include(e => e.Trail)
                .ToListAsync();

            var trailUsage = completedEvents
                .GroupBy(e => e.TrailId)
                .Select(g => new
                {
                    TrailName = g.First().Trail?.Name ?? "Unknown Trail",
                    TotalEvents = g.Count(),
                    TotalParticipants = _context.EventRegistrations.Count(r => g.Select(e => e.Id).Contains(r.EventId))
                })
                .OrderByDescending(t => t.TotalEvents)
                .ToList();

            foreach (var t in trailUsage)
            {
                csv.AppendLine($"\"{t.TrailName}\",{t.TotalEvents},{t.TotalParticipants}");
            }

            csv.AppendLine("");
            csv.AppendLine("");

            csv.AppendLine("=== FINANCIAL RECORDS ===");
            csv.AppendLine("Event Title,Date,Registration Fee,Total Participants,Total Revenue");

            var financialRecords = await _context.Events
                .Where(e => e.Status == "Completed")
                .Select(e => new
                {
                    e.EventTitle,
                    e.EventDate,
                    RegistrationFee = ExtractRegistrationFee(e.PaymentDetails ?? ""),
                    TotalParticipants = _context.EventRegistrations.Count(r => r.EventId == e.Id)
                })
                .OrderByDescending(f => f.EventDate)
                .ToListAsync();

            foreach (var f in financialRecords)
            {
                var totalRevenue = f.RegistrationFee * f.TotalParticipants;
                csv.AppendLine($"\"{f.EventTitle}\",{f.EventDate:MMM dd, yyyy},{f.RegistrationFee:N0},{f.TotalParticipants},{totalRevenue:N0}");
            }

            csv.AppendLine("");
            csv.AppendLine("");

            csv.AppendLine("=== FEEDBACK & RATINGS ===");
            csv.AppendLine("User,Event,Rating,Comment,Date");

            var feedbacks = await _context.EventFeedbacks
                .Include(f => f.Event)
                .Include(f => f.User)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            foreach (var f in feedbacks)
            {
                csv.AppendLine($"\"{f.User?.FirstName ?? "Anonymous"}\",\"{f.Event?.EventTitle}\",{f.Rating},\"{f.Comment?.Replace("\"", "\"\"") ?? ""}\",{f.CreatedAt:MMM dd, yyyy}");
            }

            var fileName = $"AllRecords_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", fileName);
        }

        private static decimal ExtractRegistrationFee(string paymentDetails)
        {
            if (string.IsNullOrEmpty(paymentDetails)) return 0;
            
            var match = System.Text.RegularExpressions.Regex.Match(paymentDetails, @"₱\s*(\d+(?:,\d+)*(?:\.\d+)?)");
            if (match.Success)
            {
                var amount = match.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(amount, out var fee))
                    return fee;
            }
            return 0;
        }
    }
    
    public class RecordsViewModel
    {
        public List<Event> Events { get; set; } = new List<Event>();
        public List<EventRegistration> Registrations { get; set; } = new List<EventRegistration>();
        public List<TrailUsageViewModel> TrailUsage { get; set; } = new List<TrailUsageViewModel>();
        public List<FinancialRecordViewModel> FinancialRecords { get; set; } = new List<FinancialRecordViewModel>();
        public List<EventFeedback> Feedbacks { get; set; } = new List<EventFeedback>();
    }

    public class TrailUsageViewModel
    {
        public int TrailId { get; set; }
        public string TrailName { get; set; } = string.Empty;
        public int TotalEvents { get; set; }
        public int TotalParticipants { get; set; }
    }

    public class FinancialRecordViewModel
    {
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public decimal RegistrationFee { get; set; }
        public int TotalParticipants { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}