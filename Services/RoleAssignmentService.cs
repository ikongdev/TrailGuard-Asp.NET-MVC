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



        public string? ErrorMessage { get; init; }

        public static RoleAssignmentResult Ok() => new() { Succeeded = true };
        public static RoleAssignmentResult Fail(string message) => new() { Succeeded = false, ErrorMessage = message };
    }

    public class AccountCreationResult
    {
        public bool Succeeded { get; init; }
        public ApplicationUser? User { get; init; }





        public IReadOnlyList<string> IdentityErrors { get; init; } = Array.Empty<string>();




        public string? GenericError { get; init; }

        public static AccountCreationResult Ok(ApplicationUser user) => new() { Succeeded = true, User = user };
        public static AccountCreationResult IdentityFailure(IEnumerable<IdentityError> errors) =>
            new() { Succeeded = false, IdentityErrors = errors.Select(e => e.Description).ToList() };
        public static AccountCreationResult Fail(string generic) => new() { Succeeded = false, GenericError = generic };
    }







    public static class OperationalRolePolicy
    {
        public static readonly string[] AllowedRoles = { "Admin", "Organizer", "Participant" };

        public static bool IsAllowedRole(string? role) =>
            role != null && AllowedRoles.Contains(role, StringComparer.Ordinal);





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








    public class RoleAssignmentService
    {
        private const string GenericFailureMessage = "An unexpected error occurred. Please try again.";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RoleAssignmentService> _logger;

        public RoleAssignmentService(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<RoleAssignmentService> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        public async Task<RoleIntegrityResult> GetRoleIntegrityAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return OperationalRolePolicy.Evaluate(roles);
        }













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









































































































        public async Task<RoleAssignmentResult> SetAccountActiveAsync(string callerUserId, string targetUserId, bool active)
        {












            if (!active && string.Equals(callerUserId, targetUserId, StringComparison.Ordinal))
            {
                return RoleAssignmentResult.Fail("You cannot disable your own account. Ask another Administrator to do this.");
            }

            var isolationLevel = !active ? IsolationLevel.Serializable : IsolationLevel.Unspecified;

            await using var transaction = await _context.Database.BeginTransactionAsync(isolationLevel);
            try
            {


                var target = await _userManager.FindByIdAsync(targetUserId);
                if (target == null)
                {
                    await transaction.RollbackAsync();
                    return RoleAssignmentResult.Fail(GenericFailureMessage);
                }


                if (target.IsActive == active)
                {
                    await transaction.CommitAsync();
                    return RoleAssignmentResult.Ok();
                }





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







                if (!active && !await HasAnotherActiveValidAdminAsync(null))
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





        public async Task<RoleIntegrityAudit> AuditRoleIntegrityAsync()
        {












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










        public async Task<IReadOnlyList<string>> GetActiveUserIdsInSingleRoleAsync(string role)
        {
            if (!OperationalRolePolicy.IsAllowedRole(role))
            {
                throw new ArgumentException($"'{role}' is not an operational role.", nameof(role));
            }

            var activeUserIds = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            var userRoleNames = await (
                from ur in _context.UserRoles.AsNoTracking()
                join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync();

            var rolesByUserId = userRoleNames
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).ToList());

            var targetStatus = OperationalRolePolicy.StatusFor(role);
            var result = new List<string>();
            foreach (var userId in activeUserIds)
            {
                var roles = rolesByUserId.TryGetValue(userId, out var found) ? found : new List<string>();
                if (OperationalRolePolicy.Evaluate(roles).Status == targetStatus)
                {
                    result.Add(userId);
                }
            }
            return result;
        }










        public async Task<IReadOnlyDictionary<string, RoleIntegrityStatus>> GetRoleIntegrityStatusesAsync(IReadOnlyCollection<string> userIds)
        {
            var distinctIds = userIds.Distinct(StringComparer.Ordinal).ToList();
            if (distinctIds.Count == 0)
            {
                return new Dictionary<string, RoleIntegrityStatus>(StringComparer.Ordinal);
            }

            var userRoleNames = await (
                from ur in _context.UserRoles.AsNoTracking()
                where distinctIds.Contains(ur.UserId)
                join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync();

            var rolesByUserId = userRoleNames
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).ToList());

            var result = new Dictionary<string, RoleIntegrityStatus>(StringComparer.Ordinal);
            foreach (var userId in distinctIds)
            {
                var roles = rolesByUserId.TryGetValue(userId, out var found) ? found : new List<string>();
                result[userId] = OperationalRolePolicy.Evaluate(roles).Status;
            }
            return result;
        }










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
