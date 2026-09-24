using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
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





        private const int RecentRegistrationsWindow = 20;

        public async Task<IActionResult> Index()
        {




            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var model = new AdminDashboardViewModel();
            var now = DateTime.Now;

            model.ActiveAccountsCount = await _userManager.Users.AsNoTracking().CountAsync(u => u.IsActive);
            model.TotalTrails = await _context.Trails.AsNoTracking().CountAsync();










































            var upcomingStatusEvents = await _context.Events
                .AsNoTracking()
                .Where(e => e.Status == "Upcoming")
                .ToListAsync();

            var joinableEvents = upcomingStatusEvents
                .Where(EventJoinabilityHelper.IsJoinable)
                .OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id)
                .ToList();
            var staleEvents = upcomingStatusEvents
                .Where(EventJoinabilityHelper.RequiresManualClosure)
                .OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id)
                .ToList();
            var operationalUpcomingEvents = upcomingStatusEvents
                .Where(e => !EventJoinabilityHelper.RequiresManualClosure(e))
                .OrderBy(e => e.EventDate).ThenBy(e => e.EventTime).ThenBy(e => e.Id)
                .ToList();

            model.UpcomingEventsCount = joinableEvents.Count;



            var recentRegistrations = await _context.EventRegistrations
                .AsNoTracking()
                .Include(r => r.Event)
                .OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id)
                .Take(RecentRegistrationsWindow)
                .ToListAsync();






            var organizerIds = joinableEvents
                .Select(e => e.OrganizerId)
                .Concat(operationalUpcomingEvents.Select(e => e.OrganizerId))
                .Concat(recentRegistrations.Select(r => r.Event?.OrganizerId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct()
                .ToList();

            var organizersById = organizerIds.Count > 0
                ? (await _userManager.Users.AsNoTracking().Where(u => organizerIds.Contains(u.Id)).ToListAsync())
                    .ToDictionary(u => u.Id, u => u)
                : new Dictionary<string, ApplicationUser>();

            model.UpcomingEvents = joinableEvents.Select(e =>
            {
                var organizer = e.OrganizerId != null && organizersById.TryGetValue(e.OrganizerId, out var found) ? found : null;
                return new AdminUpcomingEventData
                {
                    EventId = e.Id,
                    EventTitle = e.EventTitle,


                    TrailName = string.IsNullOrEmpty(e.TrailNameSnapshot) ? "Unknown Trail" : e.TrailNameSnapshot,
                    EventDate = e.EventDate,
                    EventTime = e.EventTime,
                    Difficulty = e.Difficulty,



                    OrganizerName = organizer != null
                        ? $"{organizer.FirstName} {organizer.LastName}"
                        : (e.OrganizerId == null ? null : "Organizer unavailable"),
                    OrganizerProfilePictureUrl = organizer?.ProfilePictureUrl,
                    OrganizerInitials = BuildInitials(organizer?.FirstName, organizer?.LastName)
                };
            }).ToList();

            model.RecentRegistrations = recentRegistrations.Select(r =>
            {
                var organizerId = r.Event?.OrganizerId;
                var organizerName = "Unassigned";
                if (!string.IsNullOrEmpty(organizerId))
                {
                    organizerName = organizersById.TryGetValue(organizerId, out var org)
                        ? $"{org.FirstName} {org.LastName}"
                        : "Organizer unavailable";
                }
                return new AdminRecentRegistrationData
                {
                    RegistrationId = r.Id,
                    ParticipantName = r.ParticipantName,
                    EventTitle = r.Event?.EventTitle ?? "Unknown Event",
                    OrganizerName = organizerName,
                    Status = r.Status,
                    RegisteredAt = r.RegisteredAt
                };
            }).ToList();






            var currentMonthStart = new DateTime(now.Year, now.Month, 1);
            var chartStart = currentMonthStart.AddMonths(-5);
            var nextMonthStart = currentMonthStart.AddMonths(1);
            var registrationsByMonth = await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.RegisteredAt >= chartStart && r.RegisteredAt < nextMonthStart)
                .GroupBy(r => new { r.RegisteredAt.Year, r.RegisteredAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();
            var monthCounts = registrationsByMonth.ToDictionary(x => (x.Year, x.Month), x => x.Count);
            model.MonthlyRegistrations = Enumerable.Range(0, 6).Select(offset =>
            {
                var month = chartStart.AddMonths(offset);
                return new MonthlyTrendData { Month = month.ToString("MMM"), Count = monthCounts.GetValueOrDefault((month.Year, month.Month)) };
            }).ToList();
            model.RegistrationsThisMonthCount = monthCounts.GetValueOrDefault((now.Year, now.Month));





















            var attentionItems = new List<OrganizerAttentionItem>();

            var roleAudit = await _roleAssignmentService.AuditRoleIntegrityAsync();
            var roleIssueCount = roleAudit.Conflict + roleAudit.Missing;
            if (roleIssueCount > 0)
            {
                attentionItems.Add(new OrganizerAttentionItem
                {
                    Title = "Account role issues",
                    Detail = $"{roleAudit.Conflict} conflicted, {roleAudit.Missing} missing a role.",
                    ActionLabel = "Review accounts",
                    Controller = "Admin",
                    Action = "Accounts"
                });
            }

            attentionItems.AddRange(staleEvents.Select(e => new OrganizerAttentionItem
            {
                Title = e.EventTitle,
                Detail = $"Event date passed on {e.EventDate:MMM dd}; still marked Upcoming.",
                ActionLabel = "Manage event",
                Controller = "Event",
                Action = "Details",
                Id = e.Id
            }));

            attentionItems.AddRange(operationalUpcomingEvents
                .Where(e => string.IsNullOrEmpty(e.OrganizerId) || !organizersById.ContainsKey(e.OrganizerId))
                .Select(e => new OrganizerAttentionItem
                {
                    Title = e.EventTitle,
                    Detail = string.IsNullOrEmpty(e.OrganizerId)
                        ? "No Organizer is assigned to this event."
                        : "This event's Organizer no longer resolves to an account.",
                    ActionLabel = "Assign organizer",
                    Controller = "Event",
                    Action = "Details",
                    Id = e.Id
                }));

            attentionItems.AddRange(operationalUpcomingEvents
                .Where(e => !string.IsNullOrEmpty(e.OrganizerId) && organizersById.TryGetValue(e.OrganizerId, out var org) && !org.IsActive)
                .Select(e => new OrganizerAttentionItem
                {
                    Title = e.EventTitle,
                    Detail = "This event's Organizer account is disabled.",
                    ActionLabel = "Manage event",
                    Controller = "Event",
                    Action = "Details",
                    Id = e.Id
                }));

            model.AttentionItems = attentionItems;

            return View(model);
        }

        private static string BuildInitials(string? firstName, string? lastName)
        {
            var initials = "";
            if (!string.IsNullOrEmpty(firstName)) initials += firstName[0];
            if (!string.IsNullOrEmpty(lastName)) initials += lastName[0];
            return initials.ToUpper();
        }

        public async Task<IActionResult> Accounts()
        {
            var users = await _userManager.Users.ToListAsync();
            var currentUserId = _userManager.GetUserId(User);
            var model = new AccountManagementViewModel();
            var accountList = new List<AccountItemViewModel>();

            foreach (var user in users)
            {





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




            if (!OperationalRolePolicy.IsAllowedRole(model.Role))
            {
                ModelState.AddModelError(nameof(model.Role), "Please select a valid role.");
            }

            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    FirstName = model.FirstName,
                    MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName,
                    LastName = model.LastName,
                    Email = model.Email,
                    UserName = model.Email,
                    IsActive = true,
                    DateCreated = DateTime.UtcNow
                };





                var creation = await _roleAssignmentService.CreateAccountWithRoleAsync(user, model.Password, model.Role);
                if (creation.Succeeded)
                {
                    TempData["Success"] = $"Account for {creation.User!.FirstName} {creation.User!.LastName} created successfully!";
                    return RedirectToAction(nameof(Accounts));
                }

                if (creation.IdentityErrors.Count > 0)
                {
                    foreach (var error in creation.IdentityErrorDetails)
                    {
                        ModelState.AddModelError(FieldForIdentityError(error), error.Description);
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, creation.GenericError ?? "An unexpected error occurred while creating the account. Please try again.");
                }
            }
            return View(model);
        }

        private static string FieldForIdentityError(IdentityError error)
        {
            if (error.Code.StartsWith("Password", StringComparison.Ordinal))
            {
                return nameof(AddAccountViewModel.Password);
            }

            return error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName" => nameof(AddAccountViewModel.Email),
                _ => string.Empty
            };
        }









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








    }
}
