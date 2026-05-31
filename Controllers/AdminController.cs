using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrailGuard.Models;

namespace TrailGuard.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Accounts()
        {
            var users = await _userManager.Users.ToListAsync();
            
            var viewModel = new AccountManagementViewModel
            {
                TotalAccounts = users.Count,
                ActiveAccounts = users.Count(u => u.IsActive),
                TotalOrganizers = 0,
                TotalParticipants = 0,
                Accounts = new List<AccountItemViewModel>()
            };

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var primaryRole = roles.FirstOrDefault() ?? "Participant";

                if (primaryRole == "Organizer") viewModel.TotalOrganizers++;
                if (primaryRole == "Participant") viewModel.TotalParticipants++;

                viewModel.Accounts.Add(new AccountItemViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Initials = $"{user.FirstName?.FirstOrDefault()}{user.LastName?.FirstOrDefault()}".ToUpper(),
                    Email = user.Email ?? "No Email",
                    Role = primaryRole,
                    IsActive = user.IsActive,
                    DateCreated = user.DateCreated.ToString("MMM dd, yyyy")
                });
            }

            viewModel.Accounts = viewModel.Accounts.OrderByDescending(a => a.DateCreated).ToList();

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleAccountStatus(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Accounts));
            }

            return View("Error"); 
        }

        [HttpGet]
        public IActionResult AddAccount()
        {
            return View(new AddAccountViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> AddAccount(AddAccountViewModel model)
        {
            if (ModelState.IsValid)
            {
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

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, model.Role);

                    return RedirectToAction("Accounts");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }
    }
}