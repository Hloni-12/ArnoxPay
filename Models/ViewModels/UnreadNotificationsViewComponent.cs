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
        if (User.Identity?.IsAuthenticated == true && (User.IsInRole(SystemRoles.FinancialOfficer) || User.IsInRole(SystemRoles.Administrator)))
        {
            var user = await userManager.GetUserAsync(HttpContext.User);
            if (user is not null)
            {
                count = await context.StaffNotifications.CountAsync(n => n.UserId == user.Id && !n.IsRead);
                var role = User.IsInRole(SystemRoles.Administrator) ? SystemRoles.Administrator : SystemRoles.FinancialOfficer;
                count += await context.Announcements.CountAsync(a => a.IsPublished && (a.Audience == "All" || a.Audience == role) && !context.StaffNotifications.Any(n => n.UserId == user.Id && n.Url == "/Announcement/Read/" + a.Id));
                if (User.IsInRole(SystemRoles.FinancialOfficer))
                {
                    var pendingIds = await context.Payments.Where(p => p.Status == ArnoxPay.Models.PaymentStatus.Pending).Select(p => p.Id).ToListAsync();
                    foreach (var paymentId in pendingIds)
                        if (!await context.StaffNotifications.AnyAsync(n => n.UserId == user.Id && n.Title == "Payment pending review" && n.Message.Contains("#" + paymentId))) count++;
                }
            }
        }
        else if (User.Identity?.IsAuthenticated == true && User.IsInRole(SystemRoles.Customer))
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
                    var roleAnnouncements = await context.Announcements.Where(a => a.IsPublished && (a.Audience == "All" || a.Audience == SystemRoles.Customer)).ToListAsync();
                    foreach (var announcement in roleAnnouncements)
                    {
                        var link = "/Announcement/Read/" + announcement.Id;
                        if (!await context.Notifications.AnyAsync(n => n.CustomerId == customerId.Value && n.LinkUrl == link))
                            context.Notifications.Add(new ArnoxPay.Models.Notification { CustomerId = customerId.Value, Title = "Announcement: " + announcement.Title, Message = announcement.Body.Length > 480 ? announcement.Body[..480] : announcement.Body, LinkUrl = link });
                        var reminderLink = link + "?reminder=1";
                        if (announcement.ReminderAtUtc <= DateTime.UtcNow && announcement.ReminderAtUtc.HasValue && !await context.Notifications.AnyAsync(n => n.CustomerId == customerId.Value && n.LinkUrl == reminderLink))
                            context.Notifications.Add(new ArnoxPay.Models.Notification { CustomerId = customerId.Value, Title = "Reminder: " + announcement.Title, Message = announcement.DeadlineUtc.HasValue ? "Deadline: " + announcement.DeadlineUtc.Value.ToLocalTime().ToString("g") : "Please review this announcement.", LinkUrl = reminderLink });
                    }
                    if (roleAnnouncements.Count > 0) await context.SaveChangesAsync();
                    count = await context.Notifications.CountAsync(n => n.CustomerId == customerId.Value && !n.IsRead);
                }
            }
        }

        var isStaff = User.IsInRole(SystemRoles.Administrator) || User.IsInRole(SystemRoles.FinancialOfficer);
        return View("Default", (Count: count, IsStaff: isStaff));
    }
}
