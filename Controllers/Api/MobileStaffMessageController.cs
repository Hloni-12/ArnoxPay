using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers.Api;

[ApiController]
[Route("api/mobile")]
public sealed class MobileStaffMessageController(
    AppDbContext db,
    UserManager<AppUser> users,
    MobileTokenService mobileTokens,
    IOneSignalPushService push,
    IEmailDeliveryService email,
    ILogger<MobileStaffMessageController> logger) : ControllerBase
{
    public sealed record SendMessageRequest(string RecipientUserId, string Body);

    private async Task<(AppUser? User, string? Role)> CurrentStaffAsync()
    {
        if (!mobileTokens.TryReadBearer(Request.Headers.Authorization.ToString(), out var token) || token is null)
            return (null, null);

        var user = await users.FindByIdAsync(token.UserId);
        if (user is null || !user.IsActive ||
            !string.Equals(await users.GetSecurityStampAsync(user), token.SecurityStamp, StringComparison.Ordinal))
            return (null, null);

        var roles = await users.GetRolesAsync(user);
        var role = roles.Contains(SystemRoles.Administrator) ? SystemRoles.Administrator
            : roles.Contains(SystemRoles.FinancialOfficer) ? SystemRoles.FinancialOfficer : null;
        return (user, role);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> Messages()
    {
        var (me, role) = await CurrentStaffAsync();
        if (me is null) return Unauthorized(new { success = false, message = "Not authenticated." });
        if (role is null) return Forbid();

        var oppositeRole = role == SystemRoles.Administrator ? SystemRoles.FinancialOfficer : SystemRoles.Administrator;
        var peers = await users.GetUsersInRoleAsync(oppositeRole);
        var peerIds = peers.Select(x => x.Id).ToArray();
        var messages = await db.StaffMessages.Where(x =>
                (x.SenderUserId == me.Id && peerIds.Contains(x.RecipientUserId)) ||
                (x.RecipientUserId == me.Id && peerIds.Contains(x.SenderUserId)))
            .OrderBy(x => x.SentAtUtc).AsNoTracking().ToListAsync();

        var userIds = messages.SelectMany(x => new[] { x.SenderUserId, x.RecipientUserId })
            .Append(me.Id).Distinct().ToArray();
        var names = await users.Users.Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (x.FirstName + " " + x.LastName).Trim());

        var unread = await db.StaffMessages.Where(x => x.RecipientUserId == me.Id &&
            peerIds.Contains(x.SenderUserId) && x.ReadAtUtc == null).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var message in unread) message.ReadAtUtc = now;
        var alerts = await db.StaffNotifications.Where(x => x.UserId == me.Id &&
            x.Title == "New staff message" && !x.IsRead).ToListAsync();
        foreach (var alert in alerts) { alert.IsRead = true; alert.ReadAtUtc = now; }
        if (unread.Count > 0 || alerts.Count > 0) await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            role,
            peers = peers.Select(x => new { id = x.Id, name = (x.FirstName + " " + x.LastName).Trim(), email = x.Email }),
            messages = messages.Select(x => new
            {
                id = x.Id,
                senderUserId = x.SenderUserId,
                senderName = names.GetValueOrDefault(x.SenderUserId, "ArnoxPay staff"),
                recipientUserId = x.RecipientUserId,
                recipientName = names.GetValueOrDefault(x.RecipientUserId, "ArnoxPay staff"),
                body = x.Body,
                sentAtUtc = x.SentAtUtc,
                isMine = x.SenderUserId == me.Id,
                readAtUtc = x.ReadAtUtc
            })
        });
    }

    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest? request)
    {
        var (me, role) = await CurrentStaffAsync();
        if (me is null) return Unauthorized(new { success = false, message = "Not authenticated." });
        if (role is null) return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 2000)
            return BadRequest(new { success = false, message = "Message must contain 1–2000 characters." });

        var oppositeRole = role == SystemRoles.Administrator ? SystemRoles.FinancialOfficer : SystemRoles.Administrator;
        var peers = await users.GetUsersInRoleAsync(oppositeRole);
        var recipient = peers.FirstOrDefault(x => x.Id == request.RecipientUserId);
        if (recipient is null) return Forbid();

        var body = request.Body.Trim();
        var senderName = (me.FirstName + " " + me.LastName).Trim();
        var message = new StaffMessage
        {
            SenderUserId = me.Id,
            RecipientUserId = recipient.Id,
            Body = body,
            SentAtUtc = DateTime.UtcNow
        };
        db.StaffMessages.Add(message);
        db.StaffNotifications.Add(new StaffNotification
        {
            UserId = recipient.Id,
            Title = "New staff message",
            Message = $"New message from {senderName}.",
            Url = "/StaffCommunication/Index"
        });
        await db.SaveChangesAsync();

        // Delivery is best-effort: persistence succeeds independently, so a provider outage cannot lose a message.
        var pushDelivered = await push.SendToUserAsync(recipient.Id, "New ArnoxPay message",
            $"{senderName} sent you a message. Sign in to ArnoxPay to read it.", "messages");
        var emailDelivered = false;
        if (!string.IsNullOrWhiteSpace(recipient.Email))
        {
            var emailBody = $"Hello {recipient.FirstName},\n\nYou have a new message from {senderName}:\n\n{body}\n\nSign in to ArnoxPay to reply.\n\n— ArnoxPay";
            emailDelivered = await email.SendAsync(recipient.Email, $"New ArnoxPay message from {senderName}", emailBody);
            if (!emailDelivered) logger.LogWarning("Message {MessageId} was saved, but its recipient email was not delivered.", message.Id);
        }
        else
        {
            logger.LogWarning("Message {MessageId} was saved, but recipient {RecipientId} has no email address.", message.Id, recipient.Id);
        }

        return Ok(new
        {
            success = true,
            messageId = message.Id,
            message = "Message saved.",
            emailDelivered,
            pushDelivered
        });
    }
}
