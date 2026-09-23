using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using TrailGuard.Models;
using TrailGuard.Services;
using System.IO;

namespace TrailGuard.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {


        private const long MaxProfileImageBytes = 5 * 1024 * 1024;

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment webHostEnvironment,
            ILogger<SettingsController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }






            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            ViewBag.Role = integrity.Status switch
            {
                RoleIntegrityStatus.Conflict => "Role conflict",
                RoleIntegrityStatus.Missing => "Role missing",
                _ => integrity.SingleRole
            };
            ViewBag.RoleNeedsAttention = integrity.Status is RoleIntegrityStatus.Conflict or RoleIntegrityStatus.Missing;
            ViewBag.DateJoined = user.DateCreated.ToString("MMM dd, yyyy");
            ViewBag.IsActive = user.IsActive;

            var model = new UpdateProfileViewModel
            {
                FirstName = user.FirstName,
                MiddleName = user.MiddleName ?? "",
                LastName = user.LastName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber ?? "",
                FacebookLink = user.FacebookLink ?? "",
                Bio = user.Bio ?? "",
                CurrentProfilePictureUrl = user.ProfilePictureUrl
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(UpdateProfileViewModel model, string confirmPassword)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }







            ModelState.Remove(nameof(model.CurrentPassword));
            ModelState.Remove(nameof(model.NewPassword));
            ModelState.Remove(nameof(model.ConfirmPassword));







            if (string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                ModelState.Remove(nameof(model.PhoneNumber));
            }

            if (string.IsNullOrWhiteSpace(model.FacebookLink))
            {
                ModelState.Remove(nameof(model.FacebookLink));
            }







            if (!string.IsNullOrWhiteSpace(model.FacebookLink) &&
                !(Uri.TryCreate(model.FacebookLink, UriKind.Absolute, out var facebookUri) &&
                  (facebookUri.Scheme == Uri.UriSchemeHttp || facebookUri.Scheme == Uri.UriSchemeHttps)))
            {
                ModelState.AddModelError(nameof(model.FacebookLink), "Facebook link must be a valid http or https URL.");
            }

            byte[]? validatedImageBytes = null;
            string? validatedImageExtension = null;
            if (model.ProfileImage != null && model.ProfileImage.Length > 0)
            {
                if (model.ProfileImage.Length > MaxProfileImageBytes)
                {
                    ModelState.AddModelError(nameof(model.ProfileImage), "Profile picture must be 5MB or smaller.");
                }
                else
                {
                    (validatedImageBytes, validatedImageExtension) = await ReadValidatedImageAsync(model.ProfileImage);
                    if (validatedImageExtension == null)
                    {
                        ModelState.AddModelError(nameof(model.ProfileImage), "Profile picture must be a JPG or PNG image.");
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildValidationErrorMessage();
                return RedirectToAction(nameof(Index));
            }


            if (string.IsNullOrEmpty(confirmPassword) || !await _userManager.CheckPasswordAsync(user, confirmPassword))
            {
                TempData["Error"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Index));
            }


            user.FirstName = model.FirstName;
            user.MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName;
            user.LastName = model.LastName;
            user.Email = model.Email;
            user.UserName = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.FacebookLink = model.FacebookLink;
            user.Bio = string.IsNullOrWhiteSpace(model.Bio) ? null : model.Bio;








            string? newFilePath = null;
            string? previousFilePath = null;
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "profiles");

            if (validatedImageBytes != null && validatedImageExtension != null)
            {
                Directory.CreateDirectory(uploadsFolder);

                string uniqueFileName = Guid.NewGuid().ToString("N") + validatedImageExtension;
                newFilePath = Path.Combine(uploadsFolder, uniqueFileName);
                await System.IO.File.WriteAllBytesAsync(newFilePath, validatedImageBytes);




                previousFilePath = ResolveOwnedProfileImagePath(user.ProfilePictureUrl, uploadsFolder);

                user.ProfilePictureUrl = "/images/profiles/" + uniqueFileName;
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {




                if (newFilePath != null)
                {
                    TryDeleteOwnedProfileImage(newFilePath, uploadsFolder, user.Id, "the failed profile update's own newly-written file");
                }

                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _signInManager.RefreshSignInAsync(user);





            if (previousFilePath != null)
            {
                TryDeleteOwnedProfileImage(previousFilePath, uploadsFolder, user.Id, "the previous profile picture");
            }

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(UpdateProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }





            ModelState.Remove(nameof(model.FirstName));
            ModelState.Remove(nameof(model.LastName));
            ModelState.Remove(nameof(model.Email));
            ModelState.Remove(nameof(model.PhoneNumber));
            ModelState.Remove(nameof(model.FacebookLink));

            if (string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                ModelState.AddModelError(string.Empty, "Current password and new password are required.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildValidationErrorMessage();
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                TempData["Success"] = "Password changed successfully!";
                return RedirectToAction(nameof(Index));
            }


            var errors = string.Join(" ", result.Errors.Select(e => e.Description));
            TempData["Error"] = errors;
            return RedirectToAction(nameof(Index));
        }

        private string BuildValidationErrorMessage()
        {
            var messages = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct();

            var combined = string.Join(" ", messages);
            return string.IsNullOrWhiteSpace(combined) ? "Please correct the highlighted fields." : combined;
        }










        private string? ResolveOwnedProfileImagePath(string? storedUrl, string uploadsFolder)
        {
            if (string.IsNullOrEmpty(storedUrl))
            {
                return null;
            }

            string relative = storedUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, relative));
            string normalizedUploadsFolder = Path.GetFullPath(uploadsFolder) + Path.DirectorySeparatorChar;

            return candidate.StartsWith(normalizedUploadsFolder, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }








        private void TryDeleteOwnedProfileImage(string filePath, string uploadsFolder, string userId, string description)
        {
            string normalizedUploadsFolder = Path.GetFullPath(uploadsFolder) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(filePath).StartsWith(normalizedUploadsFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to delete {Description} for user {UserId}.", description, userId);
            }
        }

























        private static async Task<(byte[]? Bytes, string? Extension)> ReadValidatedImageAsync(IFormFile file)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return (bytes, ".jpg");
            }

            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return (bytes, ".png");
            }

            return (null, null);
        }
    }
}
