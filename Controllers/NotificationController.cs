using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class NotificationController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();

        var list = await context.Notifications
            .Where(x => x.CustomerId == c.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();

        var n = await context.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == c.Id);
        if (n is null) return NotFound();

        if (!n.IsRead)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();

        var unread = await context.Notifications
            .Where(x => x.CustomerId == c.Id && !x.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }

        if (unread.Count > 0)
            await context.SaveChangesAsync();

        TempData["Success"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Marks a notification read when opened (e.g. from a deep link).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int id)
    {
        return await MarkRead(id);
    }

    private async Task<Customer?> CurrentCustomer()
    {
        var u = await userManager.GetUserAsync(User);
        return u is null ? null : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == u.Id);
    }
}
