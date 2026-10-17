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
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var notifications = await context.Notifications
            .Where(x => x.CustomerId == customer.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        return View(notifications);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var notification = await context.Notifications
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id);
        if (notification is null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var unread = await context.Notifications
            .Where(x => x.CustomerId == customer.Id && !x.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        if (unread.Count > 0) await context.SaveChangesAsync();

        TempData["Success"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Marks this customer's notification as read and opens its local target, if present.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int id)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var notification = await context.Notifications
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id);
        if (notification is null) return NotFound();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        if (!string.IsNullOrWhiteSpace(notification.LinkUrl) && Url.IsLocalUrl(notification.LinkUrl))
            return Redirect(notification.LinkUrl);

        return RedirectToAction(nameof(Index));
    }

    private async Task<Customer?> CurrentCustomer()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null
            ? null
            : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
    }
}
