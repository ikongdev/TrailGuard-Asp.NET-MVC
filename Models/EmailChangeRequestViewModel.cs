using System.ComponentModel.DataAnnotations;
using TrailGuard.Services;

namespace TrailGuard.Models;

public sealed class EmailChangeRequestViewModel
{
    [TrailGuardEmailAddress]
    [Display(Name = "Replacement email")]
    public string NewEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Current password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;
}
