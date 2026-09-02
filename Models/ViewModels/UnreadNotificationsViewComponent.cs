using ArnoxPay.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Models.ViewModels;

public class UnreadNotificationsViewComponent(
    AppDbContext context,
    UserManager<AppUser> userManager) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var count = 0;
        if (User.Identity?.IsAuthenticated == true && User.IsInRole(SystemRoles.Customer))
        {
            var user = await userManager.GetUserAsync(HttpContext.User);
            if (user is not null)
            {
                var customerId = await context.Customers
                    .Where(c => c.AppUserId == user.Id)
                    .Select(c => (int?)c.Id)
                    .FirstOrDefaultAsync();
                if (customerId.HasValue)
                {
                    count = await context.Notifications
                        .CountAsync(n => n.CustomerId == customerId.Value && !n.IsRead);
                }
            }
        }

        return View(count);
    }
}
