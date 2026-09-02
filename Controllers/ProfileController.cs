using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    // Read-only Participant Profile - GET /Profile (own) and
    // GET /Profile/{publicProfileId:guid} (contextual, authorized access). Every
    // authorization decision is delegated to ProfileAccessService; this controller
    // never re-derives role integrity, the Organizer relationship rule, or ownership
    // itself, and never accepts a user id, role, or scope from the client - only an
    // opaque PublicProfileId from the route.
    //
    // [Authorize] with no role restriction: any authenticated user (Participant,
    // Organizer, or Admin) may reach this action - ProfileAccessService is what
    // actually decides whether the specific request resolves to anything. An
    // anonymous request is challenged by this attribute before either action method
    // body runs.
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ProfileAccessService _profileAccessService;
        private readonly ParticipantProgressService _participantProgressService;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ProfileAccessService profileAccessService,
            ParticipantProgressService participantProgressService)
        {
            _userManager = userManager;
            _context = context;
            _profileAccessService = profileAccessService;
            _participantProgressService = participantProgressService;
        }

        // publicProfileId is optional so one action serves both routes: absent for
        // GET /Profile (resolve the caller's own Profile), present for
        // GET /Profile/{publicProfileId:guid} (resolve a specific target, subject to
        // ProfileAccessService's authorization matrix).
        [HttpGet]
        [Route("Profile")]
        [Route("Profile/{publicProfileId:guid}")]
        public async Task<IActionResult> Index(Guid? publicProfileId)
        {
            var viewer = await _userManager.GetUserAsync(User);
            if (viewer == null)
            {
                return NotFound();
            }

            var access = publicProfileId.HasValue
                ? await _profileAccessService.ResolveAsync(viewer, publicProfileId.Value)
                : await _profileAccessService.ResolveOwnAsync(viewer);

            // Every denied/unresolvable case - unknown id, non-Participant target,
            // unauthorized Participant viewer, unrelated Organizer, inactive target
            // viewed by an Organizer, a Rejected/Cancelled/Voided-only relationship,
            // or a conflicted/missing-role viewer - reaches this same generic
            // NotFound(). Nothing here branches on ProfileAccessResult.Succeeded
            // being false for a different reason.
            if (!access.Succeeded || access.TargetUserId == null)
            {
                return NotFound();
            }

            // Safe identity projection only - selected fields, never the
            // ApplicationUser entity itself (which also carries PasswordHash,
            // SecurityStamp, Email, PhoneNumber, FacebookLink, and the internal Id).
            var identity = await _userManager.Users
                .AsNoTracking()
                .Where(u => u.Id == access.TargetUserId)
                .Select(u => new
                {
                    u.FirstName,
                    u.MiddleName,
                    u.LastName,
                    u.ProfilePictureUrl,
                    u.Bio,
                    u.DateCreated
                })
                .FirstOrDefaultAsync();

            if (identity == null)
            {
                // The account resolved a moment ago (ProfileAccessService already read
                // it) but is gone now - treat exactly like every other unresolvable
                // target rather than a different error.
                return NotFound();
            }

            // ParticipantProgressService.GetProgressAsync already decides leaderboard
            // eligibility entirely on its own (active + clean single Participant role
            // + at least one qualifying completion - see CLAUDE.md, Participant
            // Progress) - this controller passes no eligibility flag and cannot
            // override that decision. An inactive target (Admin-viewed) still gets
            // its full historical stats/tier/achievements; it simply never comes back
            // ranked.
            var progress = await _participantProgressService.GetProgressAsync(access.TargetUserId);
            var recentAdventures = await _participantProgressService.GetRecentAdventuresAsync(access.TargetUserId);

            // Established display full-name convention (AccountManagementViewModel.FullName,
            // Admin > Account Management): FirstName + LastName, no MiddleName. MiddleName
            // appears elsewhere only in a separate legacy string-matching convention
            // (reconciling a free-text OrganizedBy snapshot against a User row), not in
            // any display convention - so it is read here but not rendered.
            var fullName = $"{identity.FirstName} {identity.LastName}".Trim();
            var initials = string.IsNullOrEmpty(identity.FirstName) ? "U" : identity.FirstName.Substring(0, 1).ToUpper();

            // Owner sees every achievement (locked + unlocked, catalog order, already
            // guaranteed by ParticipantAchievementEvaluator); an Organizer/Admin
            // visitor sees only earned ones - ParticipantProgressResult.EarnedAchievements
            // is a computed, non-duplicated view over the same Achievements list, so
            // this is the one and only place that filter is applied.
            var achievementsForViewer = access.IsOwner
                ? progress.Achievements
                : progress.EarnedAchievements.ToList();

            var viewModel = new ProfileViewModel
            {
                FullName = fullName,
                Initials = initials,
                ProfilePictureUrl = identity.ProfilePictureUrl,
                Bio = identity.Bio,
                MemberSince = identity.DateCreated,
                IsOwner = access.IsOwner,
                TargetIsActive = access.TargetIsActive,
                ViewerType = access.ViewerType,

                CompletedAdventures = progress.DistinctCompletedEventCount,
                UniqueTrails = progress.DistinctCompletedTrailCount,
                EarnedAchievementCount = progress.EarnedAchievementCount,
                TotalAchievementCount = progress.TotalAchievementCount,
                TrailPoints = progress.TrailPoints,

                Tier = progress.Tier,
                IsRanked = progress.IsRanked,
                Rank = progress.Rank,
                RankedParticipantCount = progress.RankedParticipantCount,
                PointsIntoTier = progress.PointsIntoTier,
                PointsToNextTier = progress.PointsToNextTier,
                NextTierName = ParticipantProgressPolicy.NextTierName(progress.TrailPoints),
                TierProgressPercent = ParticipantProgressPolicy.TierProgressPercent(progress.TrailPoints),
                IsTopTier = !progress.HasNextTier,

                Achievements = achievementsForViewer,
                RecentAdventures = recentAdventures
            };

            return View(viewModel);
        }
    }
}
