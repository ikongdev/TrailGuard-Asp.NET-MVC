using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;

namespace TrailGuard.Services
{
    public enum ProfileViewerType
    {
        Owner,
        Admin,
        Organizer
    }







    public sealed class ProfileAccessResult
    {
        public bool Succeeded { get; init; }
        public string? TargetUserId { get; init; }
        public Guid TargetPublicProfileId { get; init; }
        public ProfileViewerType ViewerType { get; init; }
        public bool IsOwner { get; init; }
        public bool TargetIsActive { get; init; }




        public static readonly ProfileAccessResult Denied = new() { Succeeded = false };
    }






    public class ProfileAccessService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public ProfileAccessService(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }







        public async Task<ProfileAccessResult> ResolveOwnAsync(ApplicationUser viewer)
        {
            var integrity = await GetIntegrityAsync(viewer);
            if (integrity.Status != RoleIntegrityStatus.Participant)
            {
                return ProfileAccessResult.Denied;
            }

            return BuildAllowed(viewer, ProfileViewerType.Owner, isOwner: true);
        }










        public async Task<ProfileAccessResult> ResolveAsync(ApplicationUser viewer, Guid targetPublicProfileId)
        {
            var target = await _userManager.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.PublicProfileId == targetPublicProfileId);
            if (target == null)
            {
                return ProfileAccessResult.Denied;
            }





            if (string.Equals(viewer.Id, target.Id, StringComparison.Ordinal))
            {
                var ownIntegrity = await GetIntegrityAsync(viewer);
                return ownIntegrity.Status == RoleIntegrityStatus.Participant
                    ? BuildAllowed(target, ProfileViewerType.Owner, isOwner: true)
                    : ProfileAccessResult.Denied;
            }

            var targetIntegrity = await GetIntegrityAsync(target);
            if (targetIntegrity.Status != RoleIntegrityStatus.Participant)
            {



                return ProfileAccessResult.Denied;
            }

            var viewerIntegrity = await GetIntegrityAsync(viewer);

            if (viewerIntegrity.Status == RoleIntegrityStatus.Admin)
            {





                return BuildAllowed(target, ProfileViewerType.Admin, isOwner: false);
            }

            if (viewerIntegrity.Status == RoleIntegrityStatus.Organizer)
            {
                if (!target.IsActive)
                {
                    return ProfileAccessResult.Denied;
                }






                var candidateStatuses = await _context.EventRegistrations
                    .AsNoTracking()
                    .Where(r => r.UserId == target.Id
                        && r.Event != null
                        && r.Event.OrganizerId == viewer.Id)
                    .Select(r => r.Status)
                    .ToListAsync();

                var hasRelationship = candidateStatuses.Any(ProfileAccessPolicy.AllowsOrganizerRelationship);

                return hasRelationship
                    ? BuildAllowed(target, ProfileViewerType.Organizer, isOwner: false)
                    : ProfileAccessResult.Denied;
            }






            return ProfileAccessResult.Denied;
        }

        private async Task<RoleIntegrityResult> GetIntegrityAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return OperationalRolePolicy.Evaluate(roles);
        }

        private static ProfileAccessResult BuildAllowed(ApplicationUser target, ProfileViewerType viewerType, bool isOwner) =>
            new()
            {
                Succeeded = true,
                TargetUserId = target.Id,
                TargetPublicProfileId = target.PublicProfileId,
                ViewerType = viewerType,
                IsOwner = isOwner,
                TargetIsActive = target.IsActive
            };
    }
}
