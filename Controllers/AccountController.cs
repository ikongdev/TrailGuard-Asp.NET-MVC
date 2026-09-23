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
                MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName,
                LastName = model.LastName,
                IsActive = true,
                DateCreated = DateTime.Now
            };







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
