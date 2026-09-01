using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    public enum RoleIntegrityStatus
    {
        Admin,
        Organizer,
        Participant,
        Conflict,
        Missing
    }

    // The operational roles an account is actually holding, classified
    // against the exclusivity policy below. AssignedRoles only ever contains
    // the three operational names - an infrastructure/non-operational role
    // (none exist in this app today) would never appear here and never
    // affects the classification.
    public class RoleIntegrityResult
    {
        public RoleIntegrityStatus Status { get; init; }
        public IReadOnlyList<string> AssignedRoles { get; init; } = Array.Empty<string>();

        public string? SingleRole => Status is RoleIntegrityStatus.Admin or RoleIntegrityStatus.Organizer or RoleIntegrityStatus.Participant
            ? AssignedRoles.FirstOrDefault()
            : null;
    }

    public class RoleAssignmentResult
    {
        public bool Succeeded { get; init; }

        // Always a generic, user-facing message - the underlying IdentityResult
        // errors/exceptions are logged by the caller, never returned here.
        public string? ErrorMessage { get; init; }

        public static RoleAssignmentResult Ok() => new() { Succeeded = true };
        public static RoleAssignmentResult Fail(string message) => new() { Succeeded = false, ErrorMessage = message };
    }

    public class AccountCreationResult
    {
        public bool Succeeded { get; init; }
        public ApplicationUser? User { get; init; }

        // Populated only for UserManager.CreateAsync's own validation
        // failures (weak password, duplicate email, etc.) - these are
        // already safe, user-facing IdentityResult descriptions and are
        // shown as-is, same as before this change.
        public IReadOnlyList<string> IdentityErrors { get; init; } = Array.Empty<string>();

        // Generic, user-facing fallback for everything else (invalid role,
        // role-assignment failure, final-state verification failure,
        // unexpected exception). Never the raw exception/IdentityResult detail.
        public string? GenericError { get; init; }

        public static AccountCreationResult Ok(ApplicationUser user) => new() { Succeeded = true, User = user };
        public static AccountCreationResult IdentityFailure(IEnumerable<IdentityError> errors) =>
            new() { Succeeded = false, IdentityErrors = errors.Select(e => e.Description).ToList() };
        public static AccountCreationResult Fail(string generic) => new() { Succeeded = false, GenericError = generic };
    }

    // Single source of truth for "what are TrailGuard's operational roles"
    // and "is this set of roles valid" - every account-creation and role-edit
    // path (AccountController.Register, AdminController.AddAccount,
    // AdminController's role-change endpoint, DbSeeder, the startup audit)
    // reads this instead of holding its own copy of the role-name array or
    // its own conflict-detection logic.
    public static class OperationalRolePolicy
    {
        public static readonly string[] AllowedRoles = { "Admin", "Organizer", "Participant" };

        public static bool IsAllowedRole(string? role) =>
            role != null && AllowedRoles.Contains(role, StringComparer.Ordinal);

        // Given the full role list Identity returns for an account (which,
        // in principle, could include a non-operational role from elsewhere),
        // this looks only at the three operational names and classifies the
        // result - exclusivity is defined purely in terms of those three.
        public static RoleIntegrityResult Evaluate(IEnumerable<string> allRoles)
        {
            var operational = allRoles.Where(IsAllowedRole).Distinct(StringComparer.Ordinal).ToList();

            var status = operational.Count switch
            {
                0 => RoleIntegrityStatus.Missing,
                1 => StatusFor(operational[0]),
                _ => RoleIntegrityStatus.Conflict
            };

            return new RoleIntegrityResult { Status = status, AssignedRoles = operational };
        }

        public static RoleIntegrityStatus StatusFor(string role) => role switch
        {
            "Admin" => RoleIntegrityStatus.Admin,
            "Organizer" => RoleIntegrityStatus.Organizer,
            "Participant" => RoleIntegrityStatus.Participant,
            _ => throw new ArgumentException($"'{role}' is not an operational role.", nameof(role))
        };
    }

    // The one place every account-creation and role-edit path goes through to
    // read or mutate an account's operational role. Never call
    // UserManager.AddToRoleAsync/RemoveFromRoleAsync for an operational role
    // directly from a controller - route it through here so exclusivity,
    // last-Admin protection, self-change protection, the Organizer active-
    // Event dependency check, and the security-stamp refresh can never drift
    // between call sites.
    public class RoleAssignmentService
    {
        private const string GenericFailureMessage = "An unexpected error occurred. Please try again.";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<RoleAssignmentService> _logger;

        public RoleAssignmentService(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RoleAssignmentService> logger)
        {
            _userManager = userManager;
            _context = context;
            _signInManager = signInManager;
            _logger = logger;
        }

        public async Task<RoleIntegrityResult> GetRoleIntegrityAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return OperationalRolePolicy.Evaluate(roles);
        }

        // Atomic account creation for both public registration and
        // Admin-created accounts: UserManager.CreateAsync and the initial
        // role assignment share one transaction on this same DI-scoped
        // ApplicationDbContext (confirmed: AddEntityFrameworkStores<ApplicationDbContext>
        // in Program.cs means UserManager's store uses this exact context
        // instance, so its internal SaveChangesAsync calls participate in the
        // transaction started here). A failure at any step - weak password,
        // an invalid role, the role assignment itself, or the account not
        // converging to exactly one role - rolls back the whole thing, so
        // there is never a committed user row with zero operational roles.
        // A compensating DeleteAsync is deliberately NOT the correctness
        // mechanism here; the transaction is.
        public async Task<AccountCreationResult> CreateAccountWithRoleAsync(ApplicationUser user, string password, string role)
        {
            if (!OperationalRolePolicy.IsAllowedRole(role))
            {
                _logger.LogError("Refused to create account with unrecognized role '{Role}'.", role);
                return AccountCreationResult.Fail(GenericFailureMessage);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var createResult = await _userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return AccountCreationResult.IdentityFailure(createResult.Errors);
                }

                var roleResult = await _userManager.AddToRoleAsync(user, role);
                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    LogIdentityErrors("assign initial role", user.Id, roleResult);
                    return AccountCreationResult.Fail(GenericFailureMessage);
                }

                var finalRoles = await _userManager.GetRolesAsync(user);
                var finalIntegrity = OperationalRolePolicy.Evaluate(finalRoles);
                if (finalIntegrity.Status != OperationalRolePolicy.StatusFor(role))
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("New account did not converge to exactly '{Role}' (ended with: {FinalRoles}) - rolled back.",
                        role, string.Join(",", finalIntegrity.AssignedRoles));
                    return AccountCreationResult.Fail(GenericFailureMessage);
                }

                await transaction.CommitAsync();
                return AccountCreationResult.Ok(user);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Unexpected failure creating account with role '{Role}'.", role);
                return AccountCreationResult.Fail(GenericFailureMessage);
            }
        }

        // The exclusive role-replacement flow used by every Admin-initiated
        // role change - both "resolve this conflicted/role-less account" and
        // "change this already-valid account to a different role" are the same
        // operation. callerUserId is the authenticated Admin performing the
        // change (already verified as Admin by the caller's [Authorize(Roles =
        // "Admin")] - this method trusts that boundary and only uses
        // callerUserId to detect a self-change attempt).
        //
        // Isolation is chosen from desiredRole alone, before any target/role
        // read - desiredRole != "Admin" is the only case that can ever take
        // Admin access away from someone, so it's the only case that needs
        // Serializable protection against a concurrent last-Admin race. This
        // choice must not depend on a pre-transaction snapshot of the
        // target's current roles: another request could add/remove Admin
        // between that snapshot and the point this transaction starts.
        public async Task<RoleAssignmentResult> ReplaceRoleAsync(string callerUserId, string targetUserId, string desiredRole)
        {
            if (!OperationalRolePolicy.IsAllowedRole(desiredRole))
            {
                return RoleAssignmentResult.Fail(GenericFailureMessage);
            }

            var isSelf = string.Equals(callerUserId, targetUserId, StringComparison.Ordinal);
            var isolationLevel = desiredRole != "Admin" ? IsolationLevel.Serializable : IsolationLevel.Unspecified;

            await using var transaction = await _context.Database.BeginTransactionAsync(isolationLevel);
            try
            {
                // Every read below happens inside this transaction - no
                // pre-transaction snapshot is used to authorize the mutation.
                var target = await _userManager.FindByIdAsync(targetUserId);
                if (target == null)
                {
                    // Generic, identical to an invalid role - never confirms
                    // or denies whether an id exists.
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                var currentRoles = await _userManager.GetRolesAsync(target);
                var integrity = OperationalRolePolicy.Evaluate(currentRoles);

                // --- Self-change protection --------------------------------------
                // A normally configured single-role Admin can never change their
                // own role through this endpoint - the current authentication
                // cookie can retain old role claims until the next security-stamp
                // validation, and losing Admin this way risks self-lockout. The one
                // carve-out is a conflicted account that still holds Admin: it may
                // repair itself to Admin-only (never to Organizer or Participant),
                // since that is the only safe self-service path back to a clean
                // Admin-only state if it happens to be the only Admin account.
                if (isSelf)
                {
                    if (integrity.Status == RoleIntegrityStatus.Conflict && integrity.AssignedRoles.Contains("Admin"))
                    {
                        if (desiredRole != "Admin")
                        {
                            await transaction.RollbackAsync();
                            return RoleAssignmentResult.Fail("A conflicted Admin account may only resolve itself to Admin. Ask another Administrator to assign a different role.");
                        }
                        // else: allowed - self-repair to Admin-only.
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return RoleAssignmentResult.Fail("You cannot change your own role. Ask another Administrator to make this change.");
                    }
                }

                // --- Idempotent no-op ----------------------------------------------
                // A conflicted/role-less account resolving to one role is always an
                // actual mutation (integrity.Status is Conflict/Missing, never equal
                // to StatusFor(desiredRole) with a single assigned role), so this
                // only short-circuits a genuine already-clean, already-correct
                // account. No stamp update, no last-Admin/Organizer checks needed -
                // nothing is changing.
                var isNoOp = integrity.Status == OperationalRolePolicy.StatusFor(desiredRole) && integrity.AssignedRoles.Count == 1;
                if (isNoOp)
                {
                    await transaction.CommitAsync();
                    return RoleAssignmentResult.Ok();
                }

                // --- Last-Admin protection (demotion) ------------------------------
                // A conflicted Admin (e.g. Admin+Organizer) still counts as
                // "currently holding Admin" for this check - removing its Admin
                // membership can still eliminate the only usable Admin access, even
                // though the account itself was never a valid fallback Admin for
                // anyone else (see HasAnotherActiveValidAdminAsync). Re-read inside
                // this Serializable transaction, not reused from any earlier
                // snapshot - the whole point is that Postgres tracks this read as
                // part of the transaction's conflict set.
                var removingAdminRisk = integrity.AssignedRoles.Contains("Admin") && desiredRole != "Admin";
                if (removingAdminRisk && !await HasAnotherActiveValidAdminAsync(target.Id))
                {
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail("At least one Administrator account must remain.");
                }

                // --- Organizer active-Event dependency protection -----------------
                // Only Upcoming is treated as actively manageable (see CLAUDE.md,
                // Event Lifecycle) - Completed/Cancelled are historical and keep
                // their stable OrganizerId regardless of the account's current role.
                if (integrity.AssignedRoles.Contains("Organizer") && desiredRole != "Organizer")
                {
                    var hasActiveEvents = await _context.Events
                        .AsNoTracking()
                        .AnyAsync(e => e.OrganizerId == target.Id && e.Status == "Upcoming");
                    if (hasActiveEvents)
                    {
                        await transaction.RollbackAsync();
                        return RoleAssignmentResult.Fail("This account still owns Upcoming events as Organizer. Resolve or transfer those events before changing their role.");
                    }
                }

                // --- Mutation ------------------------------------------------------
                // UserManager<ApplicationUser> and this ApplicationDbContext are the
                // same DI-scoped instance (AddEntityFrameworkStores<ApplicationDbContext>
                // in Program.cs) - every UserManager call below shares this
                // transaction's connection, so a failure partway through rolls back
                // every mutation already made, never leaving the account with zero
                // or with more than one operational role.
                if (!currentRoles.Contains(desiredRole))
                {
                    var addResult = await _userManager.AddToRoleAsync(target, desiredRole);
                    if (!addResult.Succeeded)
                    {
                        await transaction.RollbackAsync();
                        LogIdentityErrors("add role", target.Id, addResult);
                        return RoleAssignmentResult.Fail(GenericFailureMessage);
                    }
                }

                var rolesToRemove = currentRoles
                    .Where(r => OperationalRolePolicy.IsAllowedRole(r) && r != desiredRole)
                    .ToList();
                if (rolesToRemove.Count > 0)
                {
                    var removeResult = await _userManager.RemoveFromRolesAsync(target, rolesToRemove);
                    if (!removeResult.Succeeded)
                    {
                        await transaction.RollbackAsync();
                        LogIdentityErrors("remove roles", target.Id, removeResult);
                        return RoleAssignmentResult.Fail(GenericFailureMessage);
                    }
                }

                var finalRoles = await _userManager.GetRolesAsync(target);
                var finalIntegrity = OperationalRolePolicy.Evaluate(finalRoles);
                if (finalIntegrity.Status != OperationalRolePolicy.StatusFor(desiredRole))
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("Role replacement for user {UserId} did not converge to exactly '{Role}' (ended with: {FinalRoles}) - rolled back.",
                        target.Id, desiredRole, string.Join(",", finalIntegrity.AssignedRoles));
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                // --- Final invariant verification ----------------------------------
                // Re-checked after the mutation, not assumed from the pre-mutation
                // branch above - a second, independent guard against a bug in the
                // branching logic leaving the system with zero usable Admins. No
                // exclusion here: the target's own new role has already been
                // written, so if it's no longer a valid Admin it simply won't count.
                if (removingAdminRisk && !await HasAnotherActiveValidAdminAsync(null))
                {
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail("At least one Administrator account must remain.");
                }

                var stampResult = await _userManager.UpdateSecurityStampAsync(target);
                if (!stampResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    LogIdentityErrors("security stamp update", target.Id, stampResult);
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                await transaction.CommitAsync();

                // Only the self-repair path (a conflicted Admin resolving
                // itself to Admin-only) refreshes the *current* request's
                // sign-in cookie - the acting Admin and the target are the
                // same person, so this is the one case where "the current
                // request" has a session to refresh at all. This is the same
                // established call SettingsController already uses after a
                // profile/password change; it is not a new session/token
                // mechanism. Every other role change updates the target's
                // security stamp above, which invalidates their *existing*
                // session only at the next security-stamp validation - see
                // CLAUDE.md, Account Roles.
                if (isSelf)
                {
                    await _signInManager.RefreshSignInAsync(target);
                }

                return RoleAssignmentResult.Ok();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Role change for user {UserId} aborted by a concurrent conflict.", targetUserId);
                return RoleAssignmentResult.Fail("This account changed concurrently. Please try again.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Unexpected failure replacing role for user {UserId}.", targetUserId);
                return RoleAssignmentResult.Fail(GenericFailureMessage);
            }
        }

        // The one place ApplicationUser.IsActive changes. Re-reads the target
        // and, for a disable, counts other Admins inside the same transaction
        // as the write - the identical Serializable-isolation last-Admin
        // protection ReplaceRoleAsync uses above, so this can't race with a
        // concurrent request disabling (or role-changing away from Admin) a
        // different account. Enabling never reduces the Admin count, so it
        // keeps the provider's default isolation.
        //
        // Isolation is chosen from the active parameter alone, before any
        // target/status read - active == false is the only case that can
        // ever take Admin access away from someone, so it's the only case
        // that needs Serializable protection.
        //
        // callerUserId is the authenticated Admin performing the change
        // (already verified as Admin by the caller's [Authorize(Roles =
        // "Admin")] - this method trusts that boundary and only uses
        // callerUserId for the self-disable guard immediately below).
        public async Task<RoleAssignmentResult> SetAccountActiveAsync(string callerUserId, string targetUserId, bool active)
        {
            // Self-disable guard: a logged-in Admin (or any account) must
            // never be able to disable their own account through this flow.
            // Checked first, entirely from the two stable ids and the
            // requested boolean - no database read is needed to decide this,
            // so it runs before an isolation level is even chosen or a
            // transaction opened, and no mutation of any kind happens before
            // this check can reject the request. This is enforced here, not
            // only by AdminController hiding the control, so a request built
            // by hand (or a future call site) is rejected the same way.
            // Never disabling one's own account also means self-disable can
            // never be the thing that removes the last active valid Admin -
            // the last-Admin checks below exist for every *other* case.
            if (!active && string.Equals(callerUserId, targetUserId, StringComparison.Ordinal))
            {
                return RoleAssignmentResult.Fail("You cannot disable your own account. Ask another Administrator to do this.");
            }

            var isolationLevel = !active ? IsolationLevel.Serializable : IsolationLevel.Unspecified;

            await using var transaction = await _context.Database.BeginTransactionAsync(isolationLevel);
            try
            {
                // Every read below happens inside this transaction - no
                // pre-transaction snapshot is used to authorize the mutation.
                var target = await _userManager.FindByIdAsync(targetUserId);
                if (target == null)
                {
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                // --- Idempotent no-op ----------------------------------------------
                if (target.IsActive == active)
                {
                    await transaction.CommitAsync();
                    return RoleAssignmentResult.Ok();
                }

                // --- Last-Admin protection (disable) -------------------------------
                // A conflicted Admin still counts as "currently holding Admin" for
                // this check - see HasAnotherActiveValidAdminAsync and the same
                // reasoning in ReplaceRoleAsync.
                if (!active)
                {
                    var roles = await _userManager.GetRolesAsync(target);
                    var integrity = OperationalRolePolicy.Evaluate(roles);
                    if (integrity.AssignedRoles.Contains("Admin") && !await HasAnotherActiveValidAdminAsync(target.Id))
                    {
                        await transaction.RollbackAsync();
                        return RoleAssignmentResult.Fail("At least one Administrator account must remain.");
                    }
                }

                target.IsActive = active;
                var updateResult = await _userManager.UpdateAsync(target);
                if (!updateResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    LogIdentityErrors("status update", target.Id, updateResult);
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                // --- Final invariant verification ----------------------------------
                // Re-checked after the write, not assumed from the pre-write branch
                // above - a second, independent guard against a bug in the earlier
                // branching logic leaving the system with zero usable Admins. No
                // exclusion: target.IsActive is already false at this point, so it
                // won't count towards its own fallback even without one.
                if (!active && !await HasAnotherActiveValidAdminAsync(null))
                {
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail("At least one Administrator account must remain.");
                }

                // Both directions update the stamp - re-enabling an account
                // should not let a stale pre-disable session keep asserting
                // claims that predate the change either.
                var stampResult = await _userManager.UpdateSecurityStampAsync(target);
                if (!stampResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    LogIdentityErrors("security stamp update", target.Id, stampResult);
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }

                await transaction.CommitAsync();
                return RoleAssignmentResult.Ok();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Account status change for {UserId} aborted by a concurrent conflict.", targetUserId);
                return RoleAssignmentResult.Fail("This account changed concurrently. Please try again.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Unexpected failure changing active status for user {UserId}.", targetUserId);
                return RoleAssignmentResult.Fail(GenericFailureMessage);
            }
        }

        // Read-only aggregate counts for the startup log and for a manual
        // operational audit - never mutates a record. Used by Program.cs after
        // seeding so an existing conflict/missing-role account is reported for
        // manual Admin resolution instead of silently left undetected.
        public async Task<RoleIntegrityAudit> AuditRoleIntegrityAsync()
        {
            // Two bounded, constant-count queries instead of one
            // GetRolesAsync call per account (Admin Dashboard now calls this
            // on every page load, so that per-account cost no longer scales
            // acceptably): (1) every account id, (2) every UserRole row
            // joined to its Role name, in one query - not one per account.
            // Grouped in memory afterward; an account with zero rows in (2)
            // simply has no entry in the dictionary and falls through to an
            // empty role list below, which OperationalRolePolicy.Evaluate
            // already classifies as Missing - exactly what GetRolesAsync(user)
            // returning an empty IList<string> produced for that same
            // account before. The classification rule itself
            // (OperationalRolePolicy.Evaluate) is untouched.
            var userIds = await _context.Users.AsNoTracking().Select(u => u.Id).ToListAsync();

            var userRoleNames = await (
                from ur in _context.UserRoles.AsNoTracking()
                join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync();

            var rolesByUserId = userRoleNames
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).ToList());

            var audit = new RoleIntegrityAudit { Total = userIds.Count };

            foreach (var userId in userIds)
            {
                var roles = rolesByUserId.TryGetValue(userId, out var found) ? found : new List<string>();
                switch (OperationalRolePolicy.Evaluate(roles).Status)
                {
                    case RoleIntegrityStatus.Admin: audit.Admin++; break;
                    case RoleIntegrityStatus.Organizer: audit.Organizer++; break;
                    case RoleIntegrityStatus.Participant: audit.Participant++; break;
                    case RoleIntegrityStatus.Conflict: audit.Conflict++; break;
                    case RoleIntegrityStatus.Missing: audit.Missing++; break;
                }
            }

            return audit;
        }

        // Single definition of "a fallback Admin that can genuinely act as
        // one right now", shared by both ReplaceRoleAsync and
        // SetAccountActiveAsync (each call site is what excludeUserId is for
        // - null means "no exclusion, check system-wide"). Identity's role
        // table can say "Admin" for an account that cannot actually use
        // Admin access at all: disabled, role-less, or holding Admin
        // alongside a second operational role (Conflict). Only an active
        // account whose OperationalRolePolicy.Evaluate result is exactly
        // RoleIntegrityStatus.Admin (single role, no conflict) counts.
        private async Task<bool> HasAnotherActiveValidAdminAsync(string? excludeUserId)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                if (excludeUserId != null && admin.Id == excludeUserId)
                {
                    continue;
                }
                if (!admin.IsActive)
                {
                    continue;
                }
                var roles = await _userManager.GetRolesAsync(admin);
                if (OperationalRolePolicy.Evaluate(roles).Status == RoleIntegrityStatus.Admin)
                {
                    return true;
                }
            }
            return false;
        }

        private void LogIdentityErrors(string operation, string userId, IdentityResult result)
        {
            _logger.LogError("Role {Operation} failed for user {UserId}: {Errors}",
                operation, userId, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // A PostgreSQL serialization failure (SQLSTATE 40001) can surface two
        // ways depending on which statement inside the transaction triggered
        // it: a plain read (GetUsersInRoleAsync, FindByIdAsync) throws the
        // PostgresException directly, while a write that goes through
        // UserManager's SaveChangesAsync gets it wrapped in a DbUpdateException.
        // Checking both the exception itself and its InnerException covers
        // either case without needing two separate catch clauses.
        private static bool IsSerializationFailure(Exception ex)
        {
            var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
            return pgEx?.SqlState == PostgresErrorCodes.SerializationFailure;
        }
    }

    public class RoleIntegrityAudit
    {
        public int Total { get; set; }
        public int Admin { get; set; }
        public int Organizer { get; set; }
        public int Participant { get; set; }
        public int Conflict { get; set; }
        public int Missing { get; set; }
    }
}
