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

        // A bounded "recent" window for the dashboard - not the full
        // registration history Records already exposes in full elsewhere.
        // Comfortably larger than the 5 rows shown before scrolling, so the
        // fixed viewport and its cue have real content to demonstrate.
        private const int RecentRegistrationsWindow = 20;

        public async Task<IActionResult> Index()
        {
            // Registration status is a lazy check everywhere else it's read
            // (Records, Reports, Organizer, Participant, Event) - Recent
            // Registrations below reads and displays canonical Status, so it
            // needs the same freshness guarantee before it's shown.
            await RegistrationStatusHelper.ExpireOverdueRegistrations(_context);

            var model = new AdminDashboardViewModel();
            var now = DateTime.Now;

            model.ActiveAccountsCount = await _userManager.Users.AsNoTracking().CountAsync(u => u.IsActive);
            model.TotalTrails = await _context.Trails.AsNoTracking().CountAsync();

            // Upcoming Events - Status == "Upcoming" is a bounded subset (not
            // every Event ever created; Completed/Cancelled events are never
            // in this set at all). Three views of it, kept explicitly
            // separate rather than reusing one for another purpose:
            //
            //   joinableEvents    - EventJoinabilityHelper.IsJoinable
            //                       (Status == "Upcoming" && date in the
            //                       future). Feeds ONLY the Upcoming Events
            //                       summary count and displayed list - the
            //                       same predicate Organizer Dashboard uses,
            //                       unbroadened.
            //   staleEvents       - EventJoinabilityHelper.RequiresManualClosure
            //                       (Status == "Upcoming" but the date has
            //                       passed). Feeds the "stale Upcoming"
            //                       Needs Attention category.
            //   operationalUpcomingEvents - every stored-Upcoming event that
            //                       is NOT stale, i.e. NOT RequiresManualClosure.
            //                       Feeds the Organizer-integrity Needs
            //                       Attention categories below. Deliberately
            //                       NOT joinableEvents: a future event that's
            //                       full or has a closed registration window
            //                       still needs a valid, active Organizer,
            //                       and must not be silently skipped just
            //                       because it isn't currently accepting new
            //                       participants. (As EventJoinabilityHelper
            //                       is implemented today - Status+date only,
            //                       no Capacity/registration-window check,
            //                       which RegistrationController applies
            //                       separately at join time - this set is
            //                       numerically identical to joinableEvents;
            //                       it's kept as its own explicit definition
            //                       so the Organizer-integrity checks stay
            //                       correct independent of whatever
            //                       "joinable" comes to mean later, without
            //                       needing a second fix here.)
            //
            // staleEvents and operationalUpcomingEvents partition
            // upcomingStatusEvents exactly (every stored-Upcoming event is in
            // exactly one of the two), which is what guarantees no event is
            // ever flagged under both the stale category and an
            // Organizer-integrity category below.
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

            // Recent Registrations - bounded window, newest first with a
            // stable id tiebreaker.
            var recentRegistrations = await _context.EventRegistrations
                .AsNoTracking()
                .Include(r => r.Event)
                .OrderByDescending(r => r.RegisteredAt).ThenByDescending(r => r.Id)
                .Take(RecentRegistrationsWindow)
                .ToListAsync();

            // Bulk Organizer identity resolution - one query covering the
            // union of every Organizer id referenced anywhere below
            // (joinable Upcoming Events display, every operational Upcoming
            // event Needs Attention checks against, and Recent
            // Registrations) - never one lookup per row/event/category.
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
                    // Event's own Trail Snapshot, never a live Event.Trail read -
                    // see CLAUDE.md, "Event Trail Snapshot".
                    TrailName = string.IsNullOrEmpty(e.TrailNameSnapshot) ? "Unknown Trail" : e.TrailNameSnapshot,
                    EventDate = e.EventDate,
                    EventTime = e.EventTime,
                    Difficulty = e.Difficulty,
                    // Unassigned only when there's no owner id at all;
                    // "Organizer unavailable" when one exists but couldn't be
                    // resolved - never a raw id or email as a fallback.
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

            // Monthly Registration Activity - 6 calendar months including the
            // current one, oldest to newest, zero-filled, system-wide.
            // Registrations This Month is this exact same series' last
            // element, not a second query - mirrors OrganizerController.
            // Index's identical (Organizer-scoped) pattern.
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

            // Needs Attention - concatenated in deterministic severity order:
            // (1) system-wide account role integrity, as one aggregate item
            //     (never N+1 per-account lookups to render every account);
            // (2) stale Upcoming events - already overdue, the most urgent
            //     per-event category;
            // (3) operational (non-stale) Upcoming events with a null or
            //     unresolvable OrganizerId;
            // (4) operational (non-stale) Upcoming events whose Organizer
            //     resolves but is inactive.
            // (3)/(4) are drawn only from operationalUpcomingEvents, which by
            // construction excludes every event in staleEvents (the two sets
            // partition upcomingStatusEvents) - so an event already flagged
            // as stale in (2) is never also evaluated for an Organizer issue
            // in (3)/(4). Within (3)/(4) themselves: an event with a null
            // OrganizerId can only ever match (3); an event with a non-null
            // OrganizerId resolves to exactly one of unresolved (3) /
            // inactive (4) / active-and-fine (no item at all) - so no event
            // is ever flagged under both (3) and (4) either. Every event in
            // upcomingStatusEvents therefore produces at most one Needs
            // Attention item.
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

    }
}