using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Threading.Tasks;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleAssignmentService _roleAssignmentService;
        private readonly IEmailVerificationService _emailVerification;
        private readonly TrailGuard.Data.ApplicationDbContext _context;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleAssignmentService roleAssignmentService,
            IEmailVerificationService emailVerification,
            TrailGuard.Data.ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleAssignmentService = roleAssignmentService;
            _emailVerification = emailVerification;
            _context = context;
        }





        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var model = new LoginViewModel
            {
                ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
            };
            return View(model);
        }







        [Authorize]
        [HttpGet]
        public IActionResult AccessDenied()
        {



            Response.Headers["Cache-Control"] = "private, no-store";




            Response.StatusCode = StatusCodes.Status403Forbidden;







            string dashboardController =
                User.IsInRole("Admin") ? "Admin" :
                User.IsInRole("Organizer") ? "Organizer" :
                User.IsInRole("Participant") ? "Participant" :
                "Home";

            ViewBag.DashboardController = dashboardController;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            model.ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;
            if (!TryNormalizeEmail(model.Email, nameof(model.Email), out var email))
                return View(model);
            model.Email = email;

            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);








                var result = user != null
                    ? await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: false)
                    : Microsoft.AspNetCore.Identity.SignInResult.Failed;

                if (result.Succeeded)
                {
                    if (!user!.IsActive)
                    {
                        ModelState.AddModelError(string.Empty, "This account is unavailable. Contact an administrator.");
                        return View(model);
                    }

                    var roles = await _userManager.GetRolesAsync(user);
                    var integrity = OperationalRolePolicy.Evaluate(roles);
                    if (integrity.Status is RoleIntegrityStatus.Conflict or RoleIntegrityStatus.Missing)
                    {
                        ModelState.AddModelError(string.Empty, "This account's role access needs administrator attention.");
                        return View(model);
                    }

                    if (integrity.Status == RoleIntegrityStatus.Participant &&
                        user.ParticipantEmailVerificationRequired && !user.EmailConfirmed)
                    {
                        TempData["VerificationNotice"] = "Verify your email before signing in. You can request another message below.";
                        return RedirectToAction(nameof(CheckEmail));
                    }

                    await _signInManager.SignInAsync(user, isPersistent: model.RememberMe);

                    if (integrity.Status == RoleIntegrityStatus.Admin)
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    else if (integrity.Status == RoleIntegrityStatus.Organizer)
                    {
                        return RedirectToAction("Index", "Organizer");
                    }







                    if (model.ReturnUrl != null)
                    {
                        return LocalRedirect(model.ReturnUrl);
                    }

                    return RedirectToAction("Index", "Participant");
                }
                else if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "This account has been locked. Please contact support.");
                    return View(model);
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid email or password.");
                    return View(model);
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!TryNormalizeEmail(model.Email, nameof(model.Email), out var email))
                return View(model);
            model.Email = email;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser 
            { 
                UserName = model.Email, 
                Email = model.Email,
                FirstName = model.FirstName,
                MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName,
                LastName = model.LastName,
                IsActive = true,
                DateCreated = DateTime.UtcNow,
                ParticipantEmailVerificationRequired = true
            };







            var creation = await _roleAssignmentService.CreateAccountWithRoleAsync(user, model.Password, "Participant");

            if (creation.Succeeded)
            {
                var delivery = await _emailVerification.SendConfirmationAsync(creation.User!, GetRemoteIp());
                TempData["VerificationNotice"] = delivery.Status switch
                {
                    VerificationSendStatus.Sent => "Check your email for a verification link. It expires in 24 hours.",
                    VerificationSendStatus.Cooldown or VerificationSendStatus.RateLimited => "Your account was created. Please wait before requesting another verification email.",
                    _ => "Your account was created, but we could not send a verification email. Use the resend form below after email delivery is available."
                };
                return RedirectToAction(nameof(CheckEmail));
            }

            if (creation.IdentityErrors.Count > 0)
            {
                foreach (var error in creation.IdentityErrors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, creation.GenericError ?? "We couldn't complete your registration. Please try again.");
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult CheckEmail() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerification(string? email)
        {
            if (TrailGuardEmailAddress.TryNormalize(email, out var normalizedEmail, out _))
            {
                var user = await _userManager.FindByEmailAsync(normalizedEmail);
                if (user is not null && user.IsActive && user.ParticipantEmailVerificationRequired && !user.EmailConfirmed)
                {
                    var integrity = await _roleAssignmentService.GetRoleIntegrityAsync(user);
                    if (integrity.Status == RoleIntegrityStatus.Participant)
                    {
                        await _emailVerification.SendConfirmationAsync(user, GetRemoteIp());
                    }
                }
            }

            // This deliberately does not reveal whether the address exists, is
            // eligible, configured, or was accepted by SMTP.
            TempData["VerificationNotice"] = "If that address has an eligible pending verification, a message will be sent when allowed. Check spam or try again later.";
            return RedirectToAction(nameof(CheckEmail));
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string? userId, string? email, string? code)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
                return View("VerificationResult", new VerificationResultViewModel(false, "This verification link is invalid or incomplete."));
            if (!TryDecodeToken(code, out var token))
                return View("VerificationResult", new VerificationResultViewModel(false, "This verification link is invalid or malformed."));

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive || !EmailMatches(user.Email, email))
                return View("VerificationResult", new VerificationResultViewModel(false, "This verification link is invalid or is no longer available."));

            var integrity = await _roleAssignmentService.GetRoleIntegrityAsync(user);
            if (integrity.Status != RoleIntegrityStatus.Participant)
                return View("VerificationResult", new VerificationResultViewModel(false, "This verification link is no longer available."));

            if (user.EmailConfirmed)
                return View("VerificationResult", new VerificationResultViewModel(true, "Your email is already verified. You can sign in."));

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
                return View("VerificationResult", new VerificationResultViewModel(false, "This verification link is invalid, expired, or has already been replaced. Request a new email and try again."));

            user.ParticipantEmailVerificationRequired = true;
            var rollout = await _userManager.UpdateAsync(user);
            if (!rollout.Succeeded)
                return View("VerificationResult", new VerificationResultViewModel(false, "Your email was verified, but we could not finish setup. Please contact support before signing in."));

            return View("VerificationResult", new VerificationResultViewModel(true, "Your email has been verified. You can now sign in."));
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmailChange(string? userId, string? email, string? code)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
                return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is invalid or incomplete."));
            if (!TryDecodeToken(code, out var token))
                return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is invalid or malformed."));

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive)
                return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is invalid, expired, or has been superseded."));

            var integrity = await _roleAssignmentService.GetRoleIntegrityAsync(user);
            if (integrity.Status != RoleIntegrityStatus.Participant)
                return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is no longer available."));

            if (user.EmailConfirmed && string.IsNullOrWhiteSpace(user.PendingEmailChange) && EmailMatches(user.Email, email))
                return View("VerificationResult", new VerificationResultViewModel(true, "Your replacement email is already confirmed. You can sign in with it."));

            if (!EmailMatches(user.PendingEmailChange, email))
                return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is invalid, expired, or has been superseded."));

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                user.PendingEmailChange = null;
                user.PendingEmailChangeRequestedAtUtc = null;
                user.EmailConfirmed = false;
                var result = await _userManager.ChangeEmailAsync(user, email, token);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return View("VerificationResult", new VerificationResultViewModel(false, "This email-change link is invalid, expired, or has already been replaced. Your current email was not changed."));
                }

                var userName = await _userManager.SetUserNameAsync(user, email);
                if (!userName.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return View("VerificationResult", new VerificationResultViewModel(false, "This replacement email cannot be used. Your current trusted email was not changed."));
                }

                user.ParticipantEmailVerificationRequired = true;
                user.EmailConfirmed = true;
                var verify = await _userManager.UpdateAsync(user);
                if (!verify.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return View("VerificationResult", new VerificationResultViewModel(false, "We could not complete the replacement email confirmation. Your current trusted email was not changed."));
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                return View("VerificationResult", new VerificationResultViewModel(false, "We could not complete the replacement email confirmation. Your current trusted email was not changed."));
            }

            return View("VerificationResult", new VerificationResultViewModel(true, "Your replacement email has been confirmed. You can sign in with it."));
        }

        private string? GetRemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

        private bool TryNormalizeEmail(string? supplied, string key, out string normalized)
        {
            if (TrailGuardEmailAddress.TryNormalize(supplied, out normalized, out var error))
            {
                // Trim only a legitimate address; retain malformed posted text for correction.
                if (!string.Equals(supplied, normalized, StringComparison.Ordinal))
                    ModelState.Remove(key);
                return true;
            }

            ModelState.Remove(key);
            ModelState.AddModelError(key, error);
            return false;
        }

        private bool EmailMatches(string? trusted, string supplied) => trusted is not null &&
            string.Equals(_userManager.NormalizeEmail(trusted), _userManager.NormalizeEmail(supplied), StringComparison.Ordinal);

        private static bool TryDecodeToken(string encoded, out string token)
        {
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
                return !string.IsNullOrEmpty(token);
            }
            catch (FormatException)
            {
                token = string.Empty;
                return false;
            }
        }
    }
}
