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

        // ---- Contact ----
        // Plain display fields only - never rendered as raw HTML, never an
        // authentication claim/route/internal id. ProfileController projects these
        // straight from ApplicationUser (Email/PhoneNumber are already used for
        // sign-in, not introduced here); nothing here is new database state.
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }

        // Already validated by ProfileController (absolute http/https only) - null
        // whenever the stored value is missing or unsafe, so the view never needs to
        // re-validate a URI scheme itself. Razor's default encoding still applies when
        // this is written into the href/text, same as every other field here.
        public string? SafeFacebookLink { get; set; }

        // ---- Summary ----
        public int CompletedAdventures { get; set; }
        public int UniqueTrails { get; set; }
        public int EarnedAchievementCount { get; set; }
        public int TotalAchievementCount { get; set; }
        public int TrailPoints { get; set; }

        // ---- Rank ----
        public string Tier { get; set; } = ParticipantProgressPolicy.TierFor(0);

        // Stable, policy-resolved tier identifier for the Tier emblem asset -
        // ProfileController always assigns this through
        // ParticipantProgressPolicy.SafeTierKey, so the view never needs to derive,
        // validate, or transform it itself. The only legal source for
        // `/images/tiers/tier-{key}.webp`.
        public string TierKey { get; set; } = ParticipantProgressPolicy.TierKeyFor(0);

        // All five tiers, in fixed catalog order, for the Profile Tier preview
        // carousel - always ParticipantProgressPolicy.TierPreviewEntriesFor(TrailPoints),
        // never a second, view-local list of tier names/keys/thresholds. CurrentTierIndex
        // is that same list's IsCurrent entry's Position, handed over pre-computed so
        // Razor never needs a LINQ lookup to find it.
        public IReadOnlyList<ParticipantProgressPolicy.ParticipantTierPreviewEntry> TierPreviewEntries { get; set; } =
            Array.Empty<ParticipantProgressPolicy.ParticipantTierPreviewEntry>();
        public int CurrentTierIndex { get; set; }
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
