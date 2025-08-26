using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FestivalManagementWeb.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // GET: Users
        public async Task<IActionResult> Index()
        {
            var viewModel = new UserViewModel
            {
                Users = await _userManager.Users.ToListAsync()
            };
            return View(viewModel);
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserViewModel model)
        {
            if (ModelState.IsValid)
            {
                var existingUser = await _userManager.FindByEmailAsync(model.NewUserEmail);
                if (existingUser != null)
                {
                    ModelState.AddModelError("NewUserEmail", "このメールアドレスは既に使用されています。");
                }
                else
                {
                    var newUser = new ApplicationUser
                    {
                        UserName = model.NewUserEmail,
                        Email = model.NewUserEmail,
                        EmailConfirmed = true
                    };
                    var result = await _userManager.CreateAsync(newUser);
                    if (result.Succeeded)
                    {
                        return RedirectToAction(nameof(Index));
                    }
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }

            // If we got here, something failed, redisplay form
            model.Users = await _userManager.Users.ToListAsync();
            return View("Index", model);
        }

        // POST: Users/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null && currentUser.Id == id)
            {
                // Add a temp data message for feedback, cannot delete self
                TempData["ErrorMessage"] = "自分自身のアカウントは削除できません。";
                return RedirectToAction(nameof(Index));
            }

            var userToDelete = await _userManager.FindByIdAsync(id.ToString());
            if (userToDelete != null)
            {
                await _userManager.DeleteAsync(userToDelete);
            }
            
            return RedirectToAction(nameof(Index));
        }
    }
}
