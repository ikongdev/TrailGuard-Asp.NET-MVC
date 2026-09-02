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

        // Opaque public identifier for the future Profile route
        // (GET /Profile/{publicProfileId:guid}) - never derived from email,
        // name, or the internal Identity Id, and never itself used as an
        // authorization key (ProfileAccessService still resolves and checks
        // against the internal Id). Assigned client-side at construction so
        // every code path that does `new ApplicationUser { ... }` (Register,
        // AdminController.AddAccount, DbSeeder) gets a value with no
        // controller-specific assignment; existing rows are backfilled by the
        // migration that introduces the column, since this initializer only
        // ever runs for newly constructed instances, never for rows EF
        // materializes from already-stored data.
        public Guid PublicProfileId { get; set; } = Guid.NewGuid();
    }
}