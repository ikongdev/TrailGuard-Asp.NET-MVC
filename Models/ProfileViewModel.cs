using TrailGuard.Services;

namespace TrailGuard.Models
{
    // The one safe, purpose-built projection the Profile view ever sees - never an
    // ApplicationUser, never a ProfileAccessResult (which carries the internal
    // Identity Id), and never a raw EF entity. Every field here has already passed
    // through ProfileController's authorization + projection steps; the view renders
    // this and nothing else.
    public class ProfileViewModel
    {
        // ---- Identity ----
        public string FullName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string? Bio { get; set; }
        public DateTime MemberSince { get; set; }
        public bool IsOwner { get; set; }
        public bool TargetIsActive { get; set; }
        public ProfileViewerType ViewerType { get; set; }

        // ---- Summary ----
        public int CompletedAdventures { get; set; }
        public int UniqueTrails { get; set; }
        public int EarnedAchievementCount { get; set; }
        public int TotalAchievementCount { get; set; }
        public int TrailPoints { get; set; }

        // ---- Rank ----
        public string Tier { get; set; } = ParticipantProgressPolicy.TierNames[0];
        public bool IsRanked { get; set; }
        public int? Rank { get; set; }
        public int RankedParticipantCount { get; set; }
        public int PointsIntoTier { get; set; }
        public int? PointsToNextTier { get; set; }
        public string? NextTierName { get; set; }
        public int TierProgressPercent { get; set; }
        public bool IsTopTier { get; set; }

        // ---- Achievements ----
        // Already filtered by the controller before reaching the view: the owner
        // gets all eight (locked + unlocked); an Organizer/Admin visitor gets only
        // ParticipantProgressResult.EarnedAchievements. The view never re-filters by
        // IsOwner itself, so there is exactly one place this rule can be applied.
        public IReadOnlyList<ParticipantAchievementResult> Achievements { get; set; } = Array.Empty<ParticipantAchievementResult>();

        // ---- Recent adventures ----
        public IReadOnlyList<RecentAdventureResult> RecentAdventures { get; set; } = Array.Empty<RecentAdventureResult>();
    }
}
