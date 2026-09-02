using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator)]
public class AdminFinancialOfficerController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> List() => View(await context.FinancialOfficers.Include(x => x.AppUser).OrderBy(x => x.AppUser!.LastName).AsNoTracking().ToListAsync());
    [HttpGet]
    public IActionResult AddUpdate() => View(new FinancialOfficerCreateModel());
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUpdate(FinancialOfficerCreateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = new AppUser {
            UserName = model.Email, Email = model.Email, PhoneNumber = model.PhoneNumber, FirstName = model.FirstName, LastName = model.LastName, IsStaffCreation = true
        }
        ;
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded) {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await userManager.AddToRoleAsync(user, SystemRoles.FinancialOfficer);
        context.FinancialOfficers.Add(new FinancialOfficer { AppUserId = user.Id, EmployeeNumber = model.EmployeeNumber, IsAuthorized = true });
        await context.SaveChangesAsync();
        TempData["Success"] = "Financial officer created and authorized.";
        return RedirectToAction(nameof(List));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAuthorization(int id)
    {
        var officer = await context.FinancialOfficers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (officer?.AppUser is null) return NotFound();
        officer.IsAuthorized = !officer.IsAuthorized;
        officer.AppUser.IsActive = officer.IsAuthorized;
        officer.AuthorizedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
        TempData["Success"] = $"Financial officer is now {(officer.IsAuthorized ? "authorized" : "disabled")}.";
        return RedirectToAction(nameof(List));
    }
}
