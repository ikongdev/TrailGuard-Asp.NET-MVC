using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrailGuard.Data;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{












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







            if (!access.Succeeded || access.TargetUserId == null)
            {
                return NotFound();
            }






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
                    u.DateCreated,
                    u.Email,
                    u.PhoneNumber,
                    u.FacebookLink
                })
                .FirstOrDefaultAsync();

            if (identity == null)
            {



                return NotFound();
            }








            var progress = await _participantProgressService.GetProgressAsync(access.TargetUserId);
            var recentAdventures = await _participantProgressService.GetRecentAdventuresAsync(access.TargetUserId);






            var fullName = $"{identity.FirstName} {identity.LastName}".Trim();
            var initials = string.IsNullOrEmpty(identity.FirstName) ? "U" : identity.FirstName.Substring(0, 1).ToUpper();






            var achievementsForViewer = access.IsOwner
                ? progress.Achievements
                : progress.EarnedAchievements.ToList();




            var tierPreviewEntries = ParticipantProgressPolicy.TierPreviewEntriesFor(progress.TrailPoints);
            var currentTierIndex = tierPreviewEntries.First(e => e.IsCurrent).Position;

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

                Email = identity.Email,
                PhoneNumber = identity.PhoneNumber,
                SafeFacebookLink = SafeAbsoluteHttpUrl(identity.FacebookLink),

                CompletedAdventures = progress.DistinctCompletedEventCount,
                UniqueTrails = progress.DistinctCompletedTrailCount,
                EarnedAchievementCount = progress.EarnedAchievementCount,
                TotalAchievementCount = progress.TotalAchievementCount,
                TrailPoints = progress.TrailPoints,

                Tier = progress.Tier,
                TierKey = ParticipantProgressPolicy.SafeTierKey(progress.TierKey),
                TierPreviewEntries = tierPreviewEntries,
                CurrentTierIndex = currentTierIndex,
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






        private static string? SafeAbsoluteHttpUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
            return uri.Scheme is "http" or "https" ? uri.ToString() : null;
        }
    }
}
