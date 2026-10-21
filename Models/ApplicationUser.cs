using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace TrailGuard.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? MiddleName { get; set; }

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public string? FacebookLink { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public string? Bio { get; set; }
        public DateOnly? Birthday { get; set; }
        [MaxLength(20)]
        public string? Gender { get; set; }

        // Profile consent is independent from the assessment acknowledgement.  A null
        // value deliberately represents an existing account that has not yet agreed.
        [MaxLength(100)]
        public string? ParticipantProfileConsentVersion { get; set; }
        public DateTime? ParticipantProfileConsentedAtUtc { get; set; }

        // This is a rollout marker, not a replacement for Identity's EmailConfirmed
        // flag. Existing accounts deliberately remain false until they opt in or a
        // newly self-registered participant is created.
        public bool ParticipantEmailVerificationRequired { get; set; }
        public DateTime? LastEmailVerificationSentAtUtc { get; set; }

        // A replacement address never becomes the account's email until Identity
        // validates a change-email token for this exact pending value.
        [MaxLength(256)]
        public string? PendingEmailChange { get; set; }
        public DateTime? PendingEmailChangeRequestedAtUtc { get; set; }












        public Guid PublicProfileId { get; set; } = Guid.NewGuid();
    }
}
