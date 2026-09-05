using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleAssignmentService roleAssignmentService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleAssignmentService = roleAssignmentService;
        }
        
        // returnUrl arrives from Identity's own login challenge (e.g. an anonymous
        // click on Popular Trails' "Browse trails" link, which points straight at
        // ParticipantController.Trails). Url.IsLocalUrl rejects anything external,
        // protocol-relative, or malformed - only a same-site path is ever kept.
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var model = new LoginViewModel
            {
                ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
            };
            return View(model);
        }
        
        // [Authorize] with no Roles requirement: an anonymous request is
        // challenged to LoginPath (never AccessDeniedPath - Identity only
        // routes an already-authenticated-but-wrong-role user here), while a
        // signed-in user of any role is let through to see the page. Do not
        // call Forbid() from inside this action - the cookie handler would
        // redirect it straight back to AccessDeniedPath and loop.
        [Authorize]
        [HttpGet]
        public IActionResult AccessDenied()
        {
            // Established no-store convention (see DocumentsController) so a
            // denied page is never restored from the back/forward cache
            // after a role or session change.
            Response.Headers["Cache-Control"] = "private, no-store";

            // Set explicitly rather than via Forbid()/StatusCodeResult - this
            // action must render the normal view body with a real 403, not
            // trigger another authentication challenge.
            Response.StatusCode = StatusCodes.Status403Forbidden;

            // Same Admin > Organizer > Participant precedence as the navbar's
            // defensive fallback in _Layout.cshtml and the Login redirect
            // above - a defensive fallback for a stale/conflicted session,
            // not a new policy. A user with no recognizable operational role
            // falls back to Home, the only destination with no [Authorize]
            // restriction at all, so it can never redirect back here.
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
            // Never trust the posted hidden field - re-validate on every submission,
            // whether it succeeds, fails, or redisplays the form below.
            model.ReturnUrl = Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;

            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);

                // CheckPasswordSignInAsync validates the password (and applies
                // the same lockout counting PasswordSignInAsync used to) without
                // issuing a cookie. IsActive is only ever consulted *after* the
                // password has already been confirmed correct below - a wrong
                // password looks identical whether the account is active,
                // disabled, or doesn't exist at all, so disabled-ness can never
                // be inferred from a failed login attempt.
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

                    await _signInManager.SignInAsync(user, isPersistent: model.RememberMe);

                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("Admin"))
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    else if (roles.Contains("Organizer"))
                    {
                        return RedirectToAction("Index", "Organizer");
                    }

                    // Participant (or any other/no recognized role): restore to
                    // wherever the Identity challenge sent them from - e.g. Popular
                    // Trails' Browse Trails link - when a valid local return URL
                    // exists, otherwise the usual dashboard. Admin/Organizer above
                    // never consult ReturnUrl, so their dashboard destination is
                    // never overridden by it.
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
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser 
            { 
                UserName = model.Email, 
                Email = model.Email,
                FirstName = model.FirstName,
                MiddleName = model.MiddleName,
                LastName = model.LastName,
                IsActive = true,
                DateCreated = DateTime.Now
            };

            // Public registration can never choose a role - RegisterViewModel has
            // no Role field, and this is the only role this account will ever be
            // assigned: Participant, unconditionally. CreateAccountWithRoleAsync
            // creates the user and assigns that role in one transaction, so a
            // role-assignment failure can never leave a committed, role-less
            // account behind - there is nothing to compensate for afterward.
            var creation = await _roleAssignmentService.CreateAccountWithRoleAsync(user, model.Password, "Participant");

            if (creation.Succeeded)
            {
                await _signInManager.SignInAsync(creation.User!, isPersistent: false);
                return RedirectToAction("Index", "Participant");
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
    }
}