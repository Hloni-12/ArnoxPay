using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator)]
public class AdminCustomerController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> List() => View(await context.Customers.Include(x => x.AppUser).Include(x => x.Accomodation).OrderBy(x => x.AppUser!.LastName).AsNoTracking().ToListAsync());
    [HttpGet]
    public async Task<IActionResult> AddUpdate(int id)
    {
        var customer = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (customer?.AppUser is null) return NotFound();
        return View(new CustomerEditModel { Id = customer.Id, UserNumber = customer.UserNumber, FirstName = customer.AppUser.FirstName, LastName = customer.AppUser.LastName, Email = customer.AppUser.Email ?? string.Empty, PhoneNumber = customer.AppUser.PhoneNumber, Category = customer.Category, UniversityNumber = customer.UniversityNumber, ResidentialAddress = customer.ResidentialAddress });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUpdate(CustomerEditModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var customer = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == model.Id);
        if (customer?.AppUser is null) return NotFound();
        customer.AppUser.FirstName = model.FirstName;
        customer.AppUser.LastName = model.LastName;
        customer.AppUser.PhoneNumber = model.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(model.Email))
            customer.AppUser.Email = model.Email.Trim();
        // Category may be adjusted by admin between Student/Staff only
        customer.Category = model.Category;
        customer.UniversityNumber = string.IsNullOrWhiteSpace(model.UniversityNumber) ? null : model.UniversityNumber.Trim();
        customer.ResidentialAddress = model.ResidentialAddress;
        await context.SaveChangesAsync();
        TempData["Success"] = "Customer details updated.";
        return RedirectToAction(nameof(List));
    }
    [HttpGet]
    public async Task<IActionResult> Delete(int id) {
        var customer = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        return customer is null ? NotFound() : View(customer);
    }
    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (customer?.AppUser is null) return NotFound();
        if (await context.Payments.AnyAsync(x => x.CustomerId == id)) {
            TempData["Error"] = "Customers with payment records cannot be deleted. Disable the account instead.";
            return RedirectToAction(nameof(List));
        }
        context.Customers.Remove(customer);
        await context.SaveChangesAsync();
        await userManager.DeleteAsync(customer.AppUser);
        TempData["Success"] = "Customer deleted.";
        return RedirectToAction(nameof(List));
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var customer = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (customer?.AppUser is null) return NotFound();
        customer.AppUser.IsActive = !customer.AppUser.IsActive;
        await context.SaveChangesAsync();
        TempData["Success"] = $"Customer is now {(customer.AppUser.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(List));
    }
}
