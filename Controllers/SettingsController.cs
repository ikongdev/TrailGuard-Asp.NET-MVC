using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        private readonly IUploadStorage _storage;
        private readonly TrailGuard.Data.ApplicationDbContext _context;
        private readonly ILogger<SettingsController> _logger;
        private readonly IPhilippineClock _philippineClock;
        private readonly IEmailVerificationService _emailVerification;

        public SettingsController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment webHostEnvironment,
            ILogger<SettingsController> logger,
            IPhilippineClock philippineClock, IUploadStorage storage, TrailGuard.Data.ApplicationDbContext context,
            IEmailVerificationService emailVerification)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _storage = storage;
            _context = context;
            _logger = logger;
            _philippineClock = philippineClock;
            _emailVerification = emailVerification;
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
            var model = new UpdateProfileViewModel
            {
                FirstName = user.FirstName,
                MiddleName = user.MiddleName ?? "",
                LastName = user.LastName,
                Email = user.Email ?? "",
                PhoneNumber = user.PhoneNumber ?? "",
                FacebookLink = user.FacebookLink ?? "",
                Bio = user.Bio ?? "",
                CurrentProfilePictureUrl = user.ProfilePictureUrl,
                Birthday = integrity.Status == RoleIntegrityStatus.Participant ? user.Birthday : null,
                Gender = integrity.Status == RoleIntegrityStatus.Participant ? user.Gender : null
            };
            FormatParticipantPhoneForDisplay(model, integrity.Status == RoleIntegrityStatus.Participant);

            await PopulateIndexViewDataAsync(user);

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

            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            var isParticipant = integrity.Status == RoleIntegrityStatus.Participant;
            if (!TryNormalizeEmail(model.Email, nameof(model.Email), out var normalizedEmail))
                return await ReturnProfileViewAsync(model, user, isParticipant);
            model.Email = normalizedEmail;
            if (isParticipant && !EmailMatches(user.Email, model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Use the email verification section to change your email address.");
                return await ReturnProfileViewAsync(model, user, true);
            }
            if (!isParticipant)
            {
                ModelState.Remove(nameof(model.Birthday));
                ModelState.Remove(nameof(model.Gender));
            }
            else
            {
                var birthdayError = ParticipantDemographics.ValidateParticipationBirthday(model.Birthday, _philippineClock.Today);
                if (birthdayError is not null)
                {
                    ModelState.AddModelError(nameof(model.Birthday), birthdayError);
                }

                if (!string.IsNullOrWhiteSpace(model.Gender) && !ParticipantDemographics.IsValidGender(model.Gender))
                {
                    ModelState.AddModelError(nameof(model.Gender), "Select a valid Gender.");
                }
            }

            var phoneValidationFailed = false;
            if (isParticipant)
            {
                ModelState.Remove(nameof(model.PhoneNumber));
                if (string.IsNullOrWhiteSpace(model.PhoneNumber))
                {
                    model.PhoneNumber = null;
                    ModelState.AddModelError(nameof(model.PhoneNumber), "Phone Number is required.");
                    phoneValidationFailed = true;
                }
                else if (!PhilippineMobileNumber.TryNormalize(model.PhoneNumber, out var canonicalPhoneNumber))
                {
                    ModelState.AddModelError(nameof(model.PhoneNumber),
                        $"Enter a complete contact number: {PhilippineMobileNumber.LocalExample}.");
                    phoneValidationFailed = true;
                }
                else
                {
                    model.PhoneNumber = canonicalPhoneNumber;
                }
            }
            else if (string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                model.PhoneNumber = null;
                // Whitespace remains optional for non-participant Settings users.
                ModelState.Remove(nameof(model.PhoneNumber));
            }

            // A forged POST with an invalid participant number must not reach image
            // inspection, upload, or any profile mutation.
            if (phoneValidationFailed)
            {
                return await ReturnProfileViewAsync(model, user, isParticipant);
            }

            if (string.IsNullOrWhiteSpace(model.FacebookLink))
            {
                model.FacebookLink = null;
                // An empty optional value is valid; preserve all nonempty validation errors.
                ModelState.Remove(nameof(model.FacebookLink));
            }

            // This check is intentionally before image inspection/upload and profile mutation.
            // A browser checkbox or a stale page cannot substitute for persisted acceptance.
            if (isParticipant && !ParticipantProfileConsent.IsCurrent(user.ParticipantProfileConsentVersion))
            {
                ModelState.AddModelError(string.Empty, "Review and agree to the current Profile Privacy Notice before saving profile changes.");
                return await ReturnProfileViewAsync(model, user, isParticipant);
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
                return await ReturnProfileViewAsync(model, user, isParticipant);
            }


            if (string.IsNullOrEmpty(confirmPassword) || !await _userManager.CheckPasswordAsync(user, confirmPassword))
            {
                return await ReturnProfileViewAsync(model, user, isParticipant, "Current password is incorrect.");
            }


            user.FirstName = model.FirstName;
            user.MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName;
            user.LastName = model.LastName;
            // Participant email ownership has its own token-confirmed workflow.
            // Organizers and admins retain the pre-existing Settings behavior.
            if (!isParticipant)
            {
                user.Email = model.Email;
                user.UserName = model.Email;
            }
            user.PhoneNumber = model.PhoneNumber;
            user.FacebookLink = model.FacebookLink;
            user.Bio = string.IsNullOrWhiteSpace(model.Bio) ? null : model.Bio;
            if (isParticipant)
            {
                user.Birthday = model.Birthday;
                user.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender;
            }








            var previousReference = user.ProfilePictureUrl;
            await using var uploads = new UploadAttempt(_storage, _logger);
            IdentityResult? result = null;
            try
            {
                if (validatedImageBytes != null && validatedImageExtension != null)
                    user.ProfilePictureUrl = await uploads.UploadAsync(UploadCategory.Profiles, validatedImageBytes, validatedImageExtension);
                await PersistProfileUpdateAsync(user, uploads, persistedResult => result = persistedResult);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Profile update failed ({ErrorType}).", ex.GetType().Name);
                TempData["Error"] = result is { Succeeded: false }
                    ? string.Join(" ", result.Errors.Select(e => e.Description))
                    : "Unable to save your profile. Please reload and try again.";
                return RedirectToAction(nameof(Index));
            }
            if (user.ProfilePictureUrl != previousReference)
                await uploads.DeleteReplacedAsync(UploadCategory.Profiles, previousReference);
            await _signInManager.RefreshSignInAsync(user);

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptProfileConsent(ParticipantProfileConsentRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized(new { message = "Sign in again before agreeing to the notice." });

            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            if (!user.IsActive || integrity.Status != RoleIntegrityStatus.Participant)
                return Forbid();

            if (!request.Accepted)
                return BadRequest(new { message = "Select the checkbox to explicitly agree to the Profile Privacy Notice." });

            if (!ParticipantProfileConsent.IsCurrent(request.Version))
                return Conflict(new { message = "The Privacy Notice was updated. Please review the current notice before agreeing." });

            if (!ParticipantProfileConsent.IsCurrent(user.ParticipantProfileConsentVersion))
            {
                user.ParticipantProfileConsentVersion = ParticipantProfileConsent.CurrentVersion;
                user.ParticipantProfileConsentedAtUtc = DateTime.UtcNow;
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Profile consent persistence failed for an authenticated participant.");
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        new { message = "Unable to record your agreement. Please try again." });
                }
            }

            return Ok(new
            {
                version = ParticipantProfileConsent.CurrentVersion,
                acceptedAtUtc = user.ParticipantProfileConsentedAtUtc
            });
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
            ModelState.Remove(nameof(model.Birthday));
            ModelState.Remove(nameof(model.Gender));

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendCurrentEmailVerification()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            if (!user.IsActive || integrity.Status != RoleIntegrityStatus.Participant) return Forbid();

            if (user.EmailConfirmed)
            {
                TempData["Success"] = "Your current email is already verified.";
                return RedirectToAction(nameof(Index));
            }

            var send = await _emailVerification.SendConfirmationAsync(user, GetRemoteIp());
            TempData[send.Accepted ? "Success" : "Error"] = MessageFor(send, "verification email");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestEmailChange(EmailChangeRequestViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            if (!user.IsActive || integrity.Status != RoleIntegrityStatus.Participant) return Forbid();

            if (!TryNormalizeEmail(model.NewEmail, nameof(model.NewEmail), out var normalizedEmail))
            {
                TempData["Error"] = TrailGuardEmailAddress.InvalidMessage;
                return RedirectToAction(nameof(Index));
            }
            model.NewEmail = normalizedEmail;

            if (!ModelState.IsValid || !await _userManager.CheckPasswordAsync(user, model.CurrentPassword))
            {
                TempData["Error"] = !ModelState.IsValid
                    ? "Enter a valid replacement email and your current password."
                    : "Current password is incorrect.";
                return RedirectToAction(nameof(Index));
            }

            if (EmailMatches(user.Email, model.NewEmail))
            {
                TempData["Error"] = "The replacement email must be different from your current trusted email.";
                return RedirectToAction(nameof(Index));
            }

            user.PendingEmailChange = model.NewEmail.Trim();
            user.PendingEmailChangeRequestedAtUtc = DateTime.UtcNow;
            var persisted = await _userManager.UpdateAsync(user);
            if (!persisted.Succeeded)
            {
                TempData["Error"] = "We could not start the email change. Please reload and try again.";
                return RedirectToAction(nameof(Index));
            }

            var send = await _emailVerification.SendChangeConfirmationAsync(user, user.PendingEmailChange, GetRemoteIp());
            TempData[send.Accepted ? "Success" : "Error"] = MessageFor(send, "confirmation email");
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

        private async Task<IActionResult> ReturnProfileViewAsync(
            UpdateProfileViewModel model,
            ApplicationUser user,
            bool isParticipant,
            string? errorMessage = null)
        {
            TempData["Error"] = errorMessage ?? BuildValidationErrorMessage();
            FormatParticipantPhoneForDisplay(model, isParticipant);
            model.CurrentProfilePictureUrl = user.ProfilePictureUrl;
            // Keep a rejected address visible for correction. The usual successful
            // Settings render still shows the persisted trusted address.
            if (!ModelState.TryGetValue(nameof(model.Email), out var emailState) || emailState.Errors.Count == 0)
                model.Email = user.Email ?? string.Empty;
            await PopulateIndexViewDataAsync(user);
            return View(nameof(Index), model);
        }

        private static void FormatParticipantPhoneForDisplay(UpdateProfileViewModel model, bool isParticipant)
        {
            if (isParticipant && PhilippineMobileNumber.TryNormalize(model.PhoneNumber, out var canonicalPhoneNumber))
            {
                model.PhoneNumber = PhilippineMobileNumber.ToLocalDisplay(canonicalPhoneNumber);
            }
        }

        protected virtual async Task PersistProfileUpdateAsync(
            ApplicationUser user,
            UploadAttempt uploads,
            Action<IdentityResult> setResult)
        {
            await UploadPersistence.CommitAsync(_context.Database, uploads, async () =>
            {
                // Identity's ConcurrencyStamp makes a stale profile update fail across app instances.
                var result = await _userManager.UpdateAsync(user);
                setResult(result);
                if (!result.Succeeded) throw new InvalidOperationException("Profile update rejected.");
            });
        }

        private async Task PopulateIndexViewDataAsync(ApplicationUser user)
        {
            var integrity = OperationalRolePolicy.Evaluate(await _userManager.GetRolesAsync(user));
            var isParticipant = integrity.Status == RoleIntegrityStatus.Participant;
            ViewBag.Role = integrity.Status switch
            {
                RoleIntegrityStatus.Conflict => "Role conflict",
                RoleIntegrityStatus.Missing => "Role missing",
                _ => integrity.SingleRole
            };
            ViewBag.RoleNeedsAttention = integrity.Status is RoleIntegrityStatus.Conflict or RoleIntegrityStatus.Missing;
            ViewBag.IsParticipant = isParticipant;
            ViewBag.IsActive = user.IsActive;
            ViewBag.DateJoined = user.DateCreated.ToString("MMM dd, yyyy");
            ViewBag.HasCurrentProfileConsent = isParticipant && ParticipantProfileConsent.IsCurrent(user.ParticipantProfileConsentVersion);
            ViewBag.ProfileConsentAcceptedAtUtc = isParticipant ? user.ParticipantProfileConsentedAtUtc : null;
            ViewBag.EmailConfirmed = user.EmailConfirmed;
            ViewBag.ParticipantEmailVerificationRequired = user.ParticipantEmailVerificationRequired;
            ViewBag.PendingEmailChange = isParticipant ? user.PendingEmailChange : null;
            ViewBag.ParticipantBirthdayMin = isParticipant ? ParticipantDemographics.OldestEligibleBirthday(_philippineClock.Today).ToString("yyyy-MM-dd") : null;
            ViewBag.ParticipantBirthdayMax = isParticipant ? ParticipantDemographics.YoungestEligibleBirthday(_philippineClock.Today).ToString("yyyy-MM-dd") : null;
        }










        private static async Task<(byte[]? Bytes, string? Extension)> ReadValidatedImageAsync(IFormFile file)
        {
            await using var stream = file.OpenReadStream();
            var bytes = await UploadBytes.ReadAsync(stream);

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

        private string? GetRemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private bool TryNormalizeEmail(string? supplied, string key, out string normalized)
        {
            if (TrailGuardEmailAddress.TryNormalize(supplied, out normalized, out var error))
            {
                if (!string.Equals(supplied, normalized, StringComparison.Ordinal))
                    ModelState.Remove(key);
                return true;
            }

            ModelState.Remove(key);
            ModelState.AddModelError(key, error);
            return false;
        }

        private bool EmailMatches(string? current, string? candidate) => current is not null && candidate is not null &&
            string.Equals(_userManager.NormalizeEmail(current), _userManager.NormalizeEmail(candidate), StringComparison.Ordinal);

        private static string MessageFor(VerificationSendResult send, string messageType) => send.Status switch
        {
            VerificationSendStatus.Sent => $"A {messageType} was sent. It expires in 24 hours.",
            VerificationSendStatus.Cooldown => "Please wait 60 seconds before requesting another email.",
            VerificationSendStatus.RateLimited => "Too many requests were made. Please try again later.",
            VerificationSendStatus.Unavailable => "Email delivery is not configured or is currently unavailable. Please try again later.",
            _ => $"We could not send the {messageType}. Your request was saved; please try again later."
        };
    }
}
