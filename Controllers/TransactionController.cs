using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

public class TransactionController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
    public async Task<IActionResult> Index() => View(await context.Transactions.Include(x => x.Payment!).ThenInclude(x => x.Customer!).ThenInclude(x => x.AppUser).OrderByDescending(x => x.CreatedAtUtc).AsNoTracking().ToListAsync());
    [Authorize(Roles = SystemRoles.Customer)]
    public async Task<IActionResult> MyTransactions() {
        var u = await userManager.GetUserAsync(User);
        if (u is null) return Challenge();
        var c = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == u.Id);
        if (c is null) return NotFound();
        return View(await context.Transactions
            .Include(x => x.Payment!).ThenInclude(p => p.Items)
            .Where(x => x.Payment!.CustomerId == c.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync());
    }
}
