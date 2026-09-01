using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    public class ChangeRoleRequest
    {
        public string Id { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class ToggleAccountStatusRequest
    {
        public string Id { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly RoleAssignmentService _roleAssignmentService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            RoleAssignmentService roleAssignmentService,
            ILogger<AdminController> logger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _roleAssignmentService = roleAssignmentService;
            _logger = logger;
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
            var currentUserId = _userManager.GetUserId(User);
            var model = new AccountManagementViewModel();
            var accountList = new List<AccountItemViewModel>();

            foreach (var user in users)
            {
                // RoleAssignmentService.GetRoleIntegrityAsync is the single
                // source of truth for "does this account hold exactly one
                // operational role" - a plain roles.FirstOrDefault() here would
                // silently present a multi-role or role-less account as though
                // it were a normal single-role one.
                var integrity = await _roleAssignmentService.GetRoleIntegrityAsync(user);

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
                    RoleStatus = integrity.Status,
                    AssignedRoles = integrity.AssignedRoles.ToList(),
                    IsActive = user.IsActive,
                    DateCreated = user.DateCreated.ToString("MMM dd, yyyy"),
                    DateCreatedIso = user.DateCreated.ToString("o"),
                    Initials = initials,
                    ProfilePictureUrl = user.ProfilePictureUrl,
                    IsCurrentUser = user.Id == currentUserId
                });
            }

            model.Accounts = accountList;
            model.TotalAccounts = accountList.Count;
            // Conflict/Missing accounts are excluded from these three exact-role
            // totals (RoleStatus is only ever exactly Admin/Organizer/Participant
            // for a clean single-role account - see OperationalRolePolicy.Evaluate)
            // so a conflicted account is never double-counted under two roles.
            model.TotalAdmins = accountList.Count(u => u.RoleStatus == RoleIntegrityStatus.Admin);
            model.TotalOrganizers = accountList.Count(u => u.RoleStatus == RoleIntegrityStatus.Organizer);
            model.TotalParticipants = accountList.Count(u => u.RoleStatus == RoleIntegrityStatus.Participant);
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
            // The <select> on the view only ever submits one of the three
            // operational role names, but the server never trusts that -
            // an unknown or tampered value is rejected here before a user
            // row is even created.
            if (!OperationalRolePolicy.IsAllowedRole(model.Role))
            {
                ModelState.AddModelError(nameof(model.Role), "Please select a valid role.");
            }

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

                // CreateAccountWithRoleAsync creates the user and assigns
                // model.Role in one transaction - a role-assignment failure
                // rolls the user row back too, so there is never a committed,
                // active, role-less account to compensate for afterward.
                var creation = await _roleAssignmentService.CreateAccountWithRoleAsync(user, model.Password, model.Role);
                if (creation.Succeeded)
                {
                    TempData["Success"] = $"Account for {creation.User!.FirstName} {creation.User!.LastName} created successfully!";
                    return RedirectToAction(nameof(Accounts));
                }

                if (creation.IdentityErrors.Count > 0)
                {
                    foreach (var error in creation.IdentityErrors)
                    {
                        ModelState.AddModelError(string.Empty, error);
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, creation.GenericError ?? "An unexpected error occurred while creating the account. Please try again.");
                }
            }
            return View(model);
        }

        // JSON endpoint backing the account-status confirmation dialog on
        // Views/Admin/Accounts.cshtml (replaces the previous native confirm()
        // + form-post-and-redirect flow, matching the ChangeRole endpoint's
        // shape below). request.Active is the target state decided by the
        // row's Enable/Disable trigger at render time - the server never
        // infers "the opposite of the account's current state" itself,
        // since SetAccountActiveAsync re-reads and authoritatively decides
        // from inside its own transaction regardless of what the client sent.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAccountStatus([FromBody] ToggleAccountStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Id))
            {
                return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
            }

            try
            {
                var callerId = _userManager.GetUserId(User);
                if (callerId == null)
                {
                    return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
                }

                // SetAccountActiveAsync rejects a self-disable attempt, and
                // (when disabling someone else) re-reads the account and
                // re-counts other Admins inside one Serializable transaction
                // with the write itself - the last-Admin check here can't
                // race against a concurrent request disabling/role-changing a
                // different Admin account (see RoleAssignmentService for why).
                var result = await _roleAssignmentService.SetAccountActiveAsync(callerId, request.Id, request.Active);
                if (!result.Succeeded)
                {
                    return Json(new { success = false, message = result.ErrorMessage });
                }

                var message = request.Active ? "Account enabled successfully." : "Account disabled successfully.";
                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error changing account status for {TargetId}.", request.Id);
                return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
            }
        }

        // Exclusive role-replacement endpoint - one shared server-side method
        // (RoleAssignmentService.ReplaceRoleAsync) for both resolving a
        // conflicted/role-less account and changing an already-valid account
        // to a different role. Returns JSON so Views/Admin/Accounts.cshtml's
        // reusable confirmation dialog can show a toast without a full page
        // reload, matching the established ActionConfirm pattern (see
        // Organizer/RegistrationDetails.cshtml).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole([FromBody] ChangeRoleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Role))
            {
                return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
            }

            try
            {
                var callerId = _userManager.GetUserId(User);
                if (callerId == null)
                {
                    return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
                }

                var result = await _roleAssignmentService.ReplaceRoleAsync(callerId, request.Id, request.Role);
                if (!result.Succeeded)
                {
                    return Json(new { success = false, message = result.ErrorMessage });
                }

                return Json(new { success = true, message = "Role updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error changing role for account {TargetId}.", request.Id);
                return Json(new { success = false, message = "An unexpected error occurred. Please try again." });
            }
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