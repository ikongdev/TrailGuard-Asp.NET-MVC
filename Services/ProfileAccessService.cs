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

    // Everything a future ProfileController needs to render or reject a Profile
    // request - no email, password hash, security stamp, medical/payment data, or the
    // raw internal Identity Id ever leaves this type into a view. TargetUserId exists
    // only so the controller can hand it straight to ParticipantProgressService within
    // the same request; it must never be rendered, logged, or returned in a response
    // body (PublicProfileId is what the URL and any client-visible identifier use).
    public sealed class ProfileAccessResult
    {
        public bool Succeeded { get; init; }
        public string? TargetUserId { get; init; }
        public Guid TargetPublicProfileId { get; init; }
        public ProfileViewerType ViewerType { get; init; }
        public bool IsOwner { get; init; }
        public bool TargetIsActive { get; init; }

        // One generic failure for every denied/unresolvable case - see this class's
        // remarks on ResolveAsync for the full list. A future controller maps this
        // straight to NotFound() and must never branch on why it failed.
        public static readonly ProfileAccessResult Denied = new() { Succeeded = false };
    }

    // Authorization foundation for the future Profile routes (GET /Profile and
    // GET /Profile/{publicProfileId:guid} - neither is routed yet). Every resolution
    // path goes through here rather than a future controller re-deriving role
    // integrity or the Organizer relationship rule itself, so the two routes and any
    // future API can never enforce slightly different rules for the same question.
    public class ProfileAccessService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        // Organizer Profile access is a distinct, narrower policy from
        // RegistrationStatusHelper.ActiveStatuses, which exists for capacity/duplicate-
        // registration purposes and answers a different question. Alternative
        // Recommended is included here - an Organizer who redirected a participant
        // elsewhere still has a legitimate relationship to them - while
        // Rejected/Cancelled/Voided are excluded even though none of those three
        // appear in ActiveStatuses either. This list is deliberately private and
        // separate: a future change to registration-capacity semantics must never
        // silently change who can view a Profile.
        private static readonly string[] OrganizerRelationshipStatuses =
        {
            "Pending", "Awaiting Payment", "For Payment Verification", "Alternative Recommended", "Accepted"
        };

        public ProfileAccessService(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // GET /Profile (not yet routed) - a Participant viewing their own Profile with
        // no id in the URL. Only a clean, single-role Participant may resolve this;
        // anyone else (Admin, Organizer, a conflicted/missing-role account) gets the
        // same generic Denied result a future controller turns into NotFound(). An
        // anonymous caller is rejected by the future controller's own [Authorize]
        // boundary before this is ever called.
        public async Task<ProfileAccessResult> ResolveOwnAsync(ApplicationUser viewer)
        {
            var integrity = await GetIntegrityAsync(viewer);
            if (integrity.Status != RoleIntegrityStatus.Participant)
            {
                return ProfileAccessResult.Denied;
            }

            return BuildAllowed(viewer, ProfileViewerType.Owner, isOwner: true);
        }

        // GET /Profile/{publicProfileId:guid} (not yet routed). Every failure path
        // below - unknown public Profile id, a malformed/unresolvable target, a target
        // that isn't a clean single-role Participant, an unauthorized Participant
        // viewer, an unrelated Organizer, an inactive target viewed by an Organizer, or
        // a conflicted/missing-role viewer of any kind - returns the identical
        // ProfileAccessResult.Denied. That is by design: a future controller must never
        // be able to tell "this id doesn't exist" apart from "it exists but you can't
        // see it," and no client-supplied query string, hidden field, or role name read
        // here can widen any of these checks.
        public async Task<ProfileAccessResult> ResolveAsync(ApplicationUser viewer, Guid targetPublicProfileId)
        {
            var target = await _userManager.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.PublicProfileId == targetPublicProfileId);
            if (target == null)
            {
                return ProfileAccessResult.Denied;
            }

            // Checked before the general Participant-viewer branch below: a Participant
            // must be able to resolve their own public Profile id even though "cannot
            // view another Participant" blocks every other Participant-viewing-
            // Participant case.
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
                // Covers an Organizer/Admin account and any conflicted/missing-role
                // target - an Admin cannot resolve those as a Participant Profile
                // either, regardless of the viewer.
                return ProfileAccessResult.Denied;
            }

            var viewerIntegrity = await GetIntegrityAsync(viewer);

            if (viewerIntegrity.Status == RoleIntegrityStatus.Admin)
            {
                // Admin may view both active and inactive clean Participants.
                // ParticipantProgressService.GetProgressAsync decides leaderboard
                // eligibility itself from the target's own active/role state - this
                // service only reports TargetIsActive for display, it never passes a
                // leaderboard-inclusion flag anywhere.
                return BuildAllowed(target, ProfileViewerType.Admin, isOwner: false);
            }

            if (viewerIntegrity.Status == RoleIntegrityStatus.Organizer)
            {
                if (!target.IsActive)
                {
                    return ProfileAccessResult.Denied;
                }

                var hasRelationship = await _context.EventRegistrations
                    .AsNoTracking()
                    .AnyAsync(r => r.UserId == target.Id
                        && r.Event != null
                        && r.Event.OrganizerId == viewer.Id
                        && OrganizerRelationshipStatuses.Contains(r.Status));

                return hasRelationship
                    ? BuildAllowed(target, ProfileViewerType.Organizer, isOwner: false)
                    : ProfileAccessResult.Denied;
            }

            // A Participant viewing someone else, or a conflicted/missing-role viewer
            // of any kind (including one whose raw role rows happen to include
            // "Admin") - none of these grant any Profile privilege. The Admin-first
            // navbar fallback is a transitional display convenience elsewhere in the
            // app, never an authorization rule this service honors.
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
