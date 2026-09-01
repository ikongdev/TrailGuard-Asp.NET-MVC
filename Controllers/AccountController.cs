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
        
        [HttpGet]
        public IActionResult Login() 
        {
            return View();
        }
        
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
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