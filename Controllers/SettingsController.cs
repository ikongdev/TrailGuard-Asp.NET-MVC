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
        // Matches the "up to 5MB" guidance already shown next to the upload
        // control - enforced here, not just in the browser.
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

            // OperationalRolePolicy.Evaluate is the same read-only classification
            // Admin > Account Management uses - a plain roles.FirstOrDefault()
            // would silently show one arbitrary role for a multi-role account and
            // hide that anything needs an Administrator's attention. Settings
            // never offers a way to change this - it's read-only, same as before.
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

            // This model class is shared with the Security form below. Those
            // fields were never part of THIS <form>'s post, so the model binder
            // still runs their validation attributes against default empty
            // values (e.g. NewPassword's MinLength) and would otherwise fail
            // every Profile submission for reasons that have nothing to do with
            // what was actually posted.
            ModelState.Remove(nameof(model.CurrentPassword));
            ModelState.Remove(nameof(model.NewPassword));
            ModelState.Remove(nameof(model.ConfirmPassword));

            // [Phone] and [Url] both reject an empty string outright (neither
            // has a notion of "optional" - they only skip null, and these
            // properties default to "" rather than null). Phone Number and
            // Facebook Link are both optional on this form, so clearing these
            // keys when nothing was entered is what actually makes them
            // optional server-side, matching the labels in the view.
            if (string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                ModelState.Remove(nameof(model.PhoneNumber));
            }

            if (string.IsNullOrWhiteSpace(model.FacebookLink))
            {
                ModelState.Remove(nameof(model.FacebookLink));
            }

            // [Url] on the model is deliberately loose (it keeps its existing
            // client-side validation working) - this is the actual "approved
            // safe absolute http/https URL" rule, matching
            // ProfileController.SafeAbsoluteHttpUrl exactly, so a value that
            // passes here is guaranteed to render as a clickable link there
            // instead of silently falling back to "Not provided".
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

            // Verify current password
            if (string.IsNullOrEmpty(confirmPassword) || !await _userManager.CheckPasswordAsync(user, confirmPassword))
            {
                TempData["Error"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Index));
            }

            // Update user info (excluding password)
            user.FirstName = model.FirstName;
            user.MiddleName = model.MiddleName;
            user.LastName = model.LastName;
            user.Email = model.Email;
            user.UserName = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.FacebookLink = model.FacebookLink;
            user.Bio = model.Bio;

            // Safe replacement order: write the new file and remember the
            // previous one's path BEFORE touching the database, and only
            // delete the previous file AFTER the account update commits.
            // validatedImageBytes/Extension came from sniffing the file's
            // actual content above, never from the client-supplied file name
            // or declared content type, so nothing client-controlled reaches
            // the storage path below.
            string? newFilePath = null;
            string? previousFilePath = null;
            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "profiles");

            if (validatedImageBytes != null && validatedImageExtension != null)
            {
                Directory.CreateDirectory(uploadsFolder);

                string uniqueFileName = Guid.NewGuid().ToString("N") + validatedImageExtension;
                newFilePath = Path.Combine(uploadsFolder, uniqueFileName);
                await System.IO.File.WriteAllBytesAsync(newFilePath, validatedImageBytes);

                // Resolved (not deleted) now, while we still know what the
                // account's PRE-update picture was; only deleted once the
                // account record durably points at the new file below.
                previousFilePath = ResolveOwnedProfileImagePath(user.ProfilePictureUrl, uploadsFolder);

                user.ProfilePictureUrl = "/images/profiles/" + uniqueFileName;
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                // The account was never updated to reference the new file -
                // remove only what this request just wrote. The previous
                // photo (both the stored reference and its file) is
                // untouched, since the database row still points at it.
                if (newFilePath != null)
                {
                    TryDeleteOwnedProfileImage(newFilePath, uploadsFolder, user.Id, "the failed profile update's own newly-written file");
                }

                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            await _signInManager.RefreshSignInAsync(user);

            // Only now, after the account record durably references the new
            // file, remove the old one. A cleanup failure here must never
            // undo the already-successful profile update or surface a
            // filesystem path to the participant - it's logged and swallowed.
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

            // Same shared-model concern as UpdateProfile, in reverse: the
            // Profile fields weren't posted by this form, so their Required/
            // [Phone]/[Url] attributes fire against empty defaults unless
            // cleared here (see the matching comment in UpdateProfile).
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

            // Collect errors and redirect with error message
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

        // Resolves a stored ProfilePictureUrl to a full filesystem path, but
        // only if it actually resolves inside the one approved profile-image
        // directory. ProfilePictureUrl is always server-generated by this
        // controller and never accepted directly from posted data, but this
        // containment check is kept as defense-in-depth anyway - a legacy or
        // manually edited value must never be able to point a deletion
        // outside wwwroot/images/profiles. There is no shared/default avatar
        // file to accidentally target: an absent picture renders as
        // CSS/initials in the view, never a file path.
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

        // Deletes a file this controller itself wrote, re-verifying
        // containment in the approved uploads directory as a last defensive
        // check right before the delete call. Failures are logged (server-
        // side only - never a raw path or exception surfaced to the
        // participant) and swallowed, since a stray file left on disk is
        // recoverable but reverting an already-successful profile update, or
        // failing a request over cleanup, is not an acceptable trade-off.
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

        // Reads the file's actual bytes and checks them against known image
        // signatures (JPEG/PNG) - never trusts the browser-supplied content
        // type or the file's extension, either of which an attacker can set to
        // anything regardless of the file's real content. Returns the bytes
        // (so the caller doesn't need to re-read the upload stream) and the
        // extension the signature itself implies, or (null, null) if the
        // content isn't a recognized image.
        //
        // KNOWN LIMITATION: this only proves the file BEGINS with a
        // recognized signature, not that the complete file is a valid,
        // decodable image - a correct header followed by truncated or random
        // bytes still passes. Full decode-based validation was evaluated and
        // is intentionally not implemented: the project references no image-
        // decoding library (checked TrailGuard.csproj - only Identity/
        // EFCore/Npgsql packages are referenced) and no platform image API is
        // already in use elsewhere in the app. System.Drawing.Common was
        // considered and rejected - it requires its own NuGet package (not
        // currently referenced), is Windows-only since .NET 6 without an
        // unsupported runtime switch, and this project's deployment target
        // (Aiven PostgreSQL/cloud) is not guaranteed to be Windows. Adding a
        // decoding dependency (e.g. SixLabors.ImageSharp) was out of scope
        // for this correction per its explicit "no new dependency without
        // approval" constraint. Closing this gap requires either an approved
        // new package or a confirmed always-Windows deployment target.
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
