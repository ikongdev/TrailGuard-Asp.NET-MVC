using System.ComponentModel.DataAnnotations;
using TrailGuard.Services;

namespace TrailGuard.Models
{
    public class LoginViewModel
    {
        [TrailGuardEmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }





        public string? ReturnUrl { get; set; }
    }
}
