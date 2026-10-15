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












        public Guid PublicProfileId { get; set; } = Guid.NewGuid();
    }
}
