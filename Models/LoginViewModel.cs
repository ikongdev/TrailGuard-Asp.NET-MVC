using System.ComponentModel.DataAnnotations;

namespace TrailGuard.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        // Local-only post-login redirect target (e.g. back to Browse Trails after
        // an anonymous click triggered the Identity login challenge). Re-validated
        // server-side with Url.IsLocalUrl on every GET/POST - never trusted as-is
        // from the posted hidden field. Not persisted anywhere.
        public string? ReturnUrl { get; set; }
    }
}