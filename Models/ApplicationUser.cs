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
    }
}