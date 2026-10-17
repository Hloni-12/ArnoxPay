using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator + "," + SystemRoles.FinancialOfficer)]
public class StaffCommunicationController(AppDbContext db, UserManager<AppUser> users) : Controller
{
    public async Task<IActionResult> Index()
    {
        var me = await users.GetUserAsync(User);
        if (me is null) return Challenge();
        var peerRole = User.IsInRole(SystemRoles.Administrator) ? SystemRoles.FinancialOfficer : SystemRoles.Administrator;
        var peers = await users.GetUsersInRoleAsync(peerRole);
        var peerIds = peers.Select(x => x.Id).ToArray();
        var messages = await db.StaffMessages.Where(x =>
                (x.SenderUserId == me.Id && peerIds.Contains(x.RecipientUserId)) ||
                (x.RecipientUserId == me.Id && peerIds.Contains(x.SenderUserId)))
            .OrderBy(x => x.SentAtUtc).AsNoTracking().ToListAsync();
        var unread = await db.StaffMessages.Where(x => x.RecipientUserId == me.Id && x.ReadAtUtc == null).ToListAsync();
        foreach (var message in unread) message.ReadAtUtc = DateTime.UtcNow;
        var messageAlerts = await db.StaffNotifications.Where(n => n.UserId == me.Id && n.Title == "New staff message" && !n.IsRead).ToListAsync();
        foreach (var alert in messageAlerts) { alert.IsRead = true; alert.ReadAtUtc = DateTime.UtcNow; }
        if (unread.Count > 0 || messageAlerts.Count > 0) await db.SaveChangesAsync();
        ViewBag.Peers = peers;
        ViewBag.Users = await users.Users.ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName);
        return View(messages);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(string recipientUserId, string body)
    {
        var me = await users.GetUserAsync(User);
        var otherRole = User.IsInRole(SystemRoles.Administrator) ? SystemRoles.FinancialOfficer : SystemRoles.Administrator;
        var recipients = await users.GetUsersInRoleAsync(otherRole);
        if (me is null || !recipients.Any(x => x.Id == recipientUserId)) return Forbid();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000)
        {
            TempData["Error"] = "Message must contain 1–2000 characters.";
            return RedirectToAction(nameof(Index));
        }
        db.StaffMessages.Add(new StaffMessage { SenderUserId = me.Id, RecipientUserId = recipientUserId, Body = body.Trim() });
        db.StaffNotifications.Add(new StaffNotification { UserId = recipientUserId, Title = "New staff message", Message = $"New message from {me.FirstName} {me.LastName}.", Url = "/StaffCommunication/Index" });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

public class AnnouncementController(AppDbContext db, UserManager<AppUser> users) : Controller
{
    [Authorize(Roles = SystemRoles.Administrator + "," + SystemRoles.FinancialOfficer + "," + SystemRoles.Customer)]
    public async Task<IActionResult> Inbox()
    {
        var role = CurrentRole();
        var list = await db.Announcements.Where(x => x.IsPublished && (x.Audience == "All" || x.Audience == role))
            .OrderByDescending(x => x.PublishedAtUtc).AsNoTracking().ToListAsync();
        return View(list);
    }

    [Authorize(Roles = SystemRoles.Administrator)]
    public async Task<IActionResult> Index() => View(await db.Announcements.Where(x => x.IsPublished)
        .OrderByDescending(x => x.PublishedAtUtc).AsNoTracking().ToListAsync());

    [Authorize(Roles = SystemRoles.Administrator), HttpGet]
    public IActionResult Create() => View(new Announcement());

    [Authorize(Roles = SystemRoles.Administrator), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Announcement announcement)
    {
        if (!new[] { "All", SystemRoles.Administrator, SystemRoles.FinancialOfficer, SystemRoles.Customer }.Contains(announcement.Audience))
            ModelState.AddModelError(nameof(announcement.Audience), "Choose a permitted audience.");
        if (!new[] { "Low", "Normal", "High", "Urgent" }.Contains(announcement.Priority))
            ModelState.AddModelError(nameof(announcement.Priority), "Choose a valid priority.");
        if (!ModelState.IsValid) return View(announcement);
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        announcement.PublishedByUserId = user.Id;
        announcement.PublishedAtUtc = DateTime.UtcNow;
        announcement.IsPublished = true;
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync();
        TempData["Success"] = "Announcement published.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = SystemRoles.Administrator + "," + SystemRoles.FinancialOfficer + "," + SystemRoles.Customer)]
    public async Task<IActionResult> Read(int id)
    {
        var role = CurrentRole();
        var announcement = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id && x.IsPublished && (x.Audience == "All" || x.Audience == role));
        return announcement is null ? NotFound() : View(announcement);
    }

    private string CurrentRole() => User.IsInRole(SystemRoles.Administrator) ? SystemRoles.Administrator :
        User.IsInRole(SystemRoles.FinancialOfficer) ? SystemRoles.FinancialOfficer : SystemRoles.Customer;
}
