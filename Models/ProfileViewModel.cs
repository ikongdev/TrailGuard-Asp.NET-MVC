using TrailGuard.Services;

namespace TrailGuard.Models
{





    public class ProfileViewModel
    {

        public string FullName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string? Bio { get; set; }
        public DateTime MemberSince { get; set; }
        public bool IsOwner { get; set; }
        public bool TargetIsActive { get; set; }
        public ProfileViewerType ViewerType { get; set; }






        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }





        public string? SafeFacebookLink { get; set; }


        public int CompletedAdventures { get; set; }
        public int UniqueTrails { get; set; }
        public int EarnedAchievementCount { get; set; }
        public int TotalAchievementCount { get; set; }
        public int TrailPoints { get; set; }


        public string Tier { get; set; } = ParticipantProgressPolicy.TierFor(0);






        public string TierKey { get; set; } = ParticipantProgressPolicy.TierKeyFor(0);






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






        public IReadOnlyList<ParticipantAchievementResult> Achievements { get; set; } = Array.Empty<ParticipantAchievementResult>();


        public IReadOnlyList<RecentAdventureResult> RecentAdventures { get; set; } = Array.Empty<RecentAdventureResult>();
    }
}
