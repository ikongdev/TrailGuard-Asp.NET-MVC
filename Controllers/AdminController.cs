using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index()
        {
            var model = new AdminDashboardViewModel();

            model.TotalTrails = await _context.Trails.CountAsync();
            model.TotalEvents = await _context.Events.CountAsync();
            model.TotalParticipants = await _context.EventRegistrations.CountAsync();

            var completedEvents = await _context.Events.Where(e => e.Status == "Completed").ToListAsync();
            decimal totalRevenue = 0;
            foreach (var e in completedEvents)
            {
                var fee = ExtractRegistrationFee(e.PaymentDetails ?? "");
                var participantCount = await _context.EventRegistrations.CountAsync(r => r.EventId == e.Id);
                totalRevenue += fee * participantCount;
            }
            model.TotalRevenue = totalRevenue;

            var last12Months = new List<DateTime>();
            for (int i = 11; i >= 0; i--)
            {
                last12Months.Add(DateTime.Now.AddMonths(-i).Date);
            }

            model.EventsPerMonth = new List<MonthlyData>();
            foreach (var month in last12Months)
            {
                var count = await _context.Events.CountAsync(e => e.EventDate.Year == month.Year && e.EventDate.Month == month.Month);
                model.EventsPerMonth.Add(new MonthlyData
                {
                    Month = month.ToString("MMM yyyy"),
                    Count = count
                });
            }

            model.PopularTrails = new List<PopularTrailData>();
            var trailGroups = await _context.Events
                .GroupBy(e => e.TrailId)
                .Select(g => new { TrailId = g.Key, EventCount = g.Count() })
                .OrderByDescending(t => t.EventCount)
                .Take(5)
                .ToListAsync();

            foreach (var g in trailGroups)
            {
                var trail = await _context.Trails.FirstOrDefaultAsync(t => t.Id == g.TrailId);
                model.PopularTrails.Add(new PopularTrailData
                {
                    TrailId = g.TrailId,
                    TrailName = trail?.Name ?? "Unknown Trail",
                    EventCount = g.EventCount
                });
            }

            model.EventStatusDistribution = new List<StatusData>();
            var statuses = new[] { "Upcoming", "Completed", "Cancelled", "Postponed" };
            foreach (var status in statuses)
            {
                var count = await _context.Events.CountAsync(e => e.Status == status);
                model.EventStatusDistribution.Add(new StatusData
                {
                    Status = status,
                    Count = count
                });
            }

            model.UpcomingEvents = await _context.Events
                .Include(e => e.Trail)
                .Where(e => e.EventDate >= DateTime.Today)
                .OrderBy(e => e.EventDate)
                .Take(5)
                .ToListAsync() ?? new List<Event>();

            model.RecentRegistrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .OrderByDescending(r => r.RegisteredAt)
                .Take(5)
                .ToListAsync() ?? new List<EventRegistration>();

            return View(model);
        }

        public async Task<IActionResult> Accounts()
        {
            var users = await _userManager.Users.ToListAsync();
            var model = new AccountManagementViewModel();
            var accountList = new List<AccountItemViewModel>();
            
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var role = roles.FirstOrDefault() ?? "Participant";

                string initials = "";
                if (!string.IsNullOrEmpty(user.FirstName))
                    initials += user.FirstName[0];
                if (!string.IsNullOrEmpty(user.LastName))
                    initials += user.LastName[0];
                initials = initials.ToUpper();
                
                accountList.Add(new AccountItemViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email ?? "",
                    Role = role,
                    IsActive = user.IsActive,
                    DateCreated = user.DateCreated.ToString("MMM dd, yyyy"),
                    Initials = initials,
                    ProfilePictureUrl = user.ProfilePictureUrl // <-- Idagdag ito
                });
            }
            
            model.Accounts = accountList;
            model.TotalAccounts = accountList.Count;
            model.TotalOrganizers = accountList.Count(u => u.Role == "Organizer");
            model.TotalParticipants = accountList.Count(u => u.Role == "Participant");
            model.ActiveAccounts = accountList.Count(u => u.IsActive);
            
            return View(model);
        }

        [HttpGet]
        public IActionResult AddAccount()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAccount(AddAccountViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    FirstName = model.FirstName,
                    MiddleName = model.MiddleName,
                    LastName = model.LastName,
                    Email = model.Email,
                    UserName = model.Email,
                    IsActive = true,
                    DateCreated = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, model.Role);
                    TempData["Success"] = $"Account for {user.FirstName} {user.LastName} created successfully!";
                    return RedirectToAction(nameof(Accounts));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAccountStatus(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["Error"] = "Invalid account ID.";
                return RedirectToAction(nameof(Accounts));
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Account not found.";
                return RedirectToAction(nameof(Accounts));
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            var status = user.IsActive ? "enabled" : "disabled";
            TempData["Success"] = $"Account for {user.FirstName} {user.LastName} has been {status}.";
            return RedirectToAction(nameof(Accounts));
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
}