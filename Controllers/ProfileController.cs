using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize]
public class ProfileController(AppDbContext context, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager) : Controller
{
    [Authorize(Roles = SystemRoles.Customer)]
    public async Task<IActionResult> Index()
    {
        var c = await CurrentCustomer();
        if (c?.AppUser is null) return NotFound();
        return View(new CustomerEditModel
        {
            Id = c.Id,
            UserNumber = c.UserNumber,
            Category = c.Category,
            FirstName = c.AppUser.FirstName,
            LastName = c.AppUser.LastName,
            Email = c.AppUser.Email ?? string.Empty,
            PhoneNumber = c.AppUser.PhoneNumber,
            UniversityNumber = c.UniversityNumber,
            ResidentialAddress = c.ResidentialAddress
        });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CustomerEditModel model)
    {
        var c = await CurrentCustomer();
        if (c?.AppUser is null || c.Id != model.Id) return NotFound();

        // Keep UserNumber and Category fixed for the customer (cannot escalate role)
        model.UserNumber = c.UserNumber;
        model.Category = c.Category;

        if (!ModelState.IsValid) return View(model);

        // Email uniqueness if changed
        var newEmail = model.Email.Trim();
        if (!string.Equals(c.AppUser.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var other = await userManager.FindByEmailAsync(newEmail);
            if (other is not null && other.Id != c.AppUserId)
            {
                ModelState.AddModelError(nameof(model.Email), "This email address is already registered.");
                return View(model);
            }
            c.AppUser.Email = newEmail;
            // Keep UserName as User Number for customer login
        }

        c.AppUser.FirstName = model.FirstName.Trim();
        c.AppUser.LastName = model.LastName.Trim();
        c.AppUser.PhoneNumber = model.PhoneNumber;
        c.UniversityNumber = string.IsNullOrWhiteSpace(model.UniversityNumber) ? null : model.UniversityNumber.Trim();
        c.ResidentialAddress = model.ResidentialAddress;
        await context.SaveChangesAsync();
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword != confirmPassword || newPassword.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Passwords must match and contain at least eight characters.");
            return View();
        }
        var u = await userManager.GetUserAsync(User);
        if (u is null) return Challenge();
        var result = await userManager.ChangePasswordAsync(u, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View();
        }
        await signInManager.RefreshSignInAsync(u);
        TempData["Success"] = "Password updated.";
        if (User.IsInRole(SystemRoles.Customer)) return RedirectToAction(nameof(Index));
        if (User.IsInRole(SystemRoles.FinancialOfficer)) return RedirectToAction("Index", "Payment");
        return RedirectToAction("Index", "Home");
    }

    private async Task<Customer?> CurrentCustomer()
    {
        var u = await userManager.GetUserAsync(User);
        return u is null ? null : await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.AppUserId == u.Id);
    }
}
