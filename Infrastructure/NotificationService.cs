using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Infrastructure;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly NotificationSettings _settings;
    private readonly ILogger<NotificationService> _logger;
    private static readonly HttpClient Http = new();

    public NotificationService(
    AppDbContext context,
    IOptions<NotificationSettings> options,
    ILogger<NotificationService> logger)
    {
        _context = context;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task CreateAsync(int customerId, string title, string message)
    {
        // 1) Always store in-app notification in SQL Server
        _context.Notifications.Add(new Notification
        {
            CustomerId = customerId,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        }
        );
        await _context.SaveChangesAsync();

        // 2) Load customer contact details for email / SMS
        var customer = await _context.Customers
        .Include(c => c.AppUser)
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == customerId);

        if (customer?.AppUser is null)
        {
            _logger.LogWarning("Notification {Title}: customer {CustomerId} has no user profile.", title, customerId);
            return;
        }

        var email = customer.AppUser.Email;
        var phone = customer.AppUser.PhoneNumber;

        if (_settings.EmailEnabled && !string.IsNullOrWhiteSpace(email))
        {
            try
            {
                await SendEmailAsync(email, customer.AppUser.FirstName, title, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email notification failed for {Email}", email);
            }
        }

        if (_settings.SmsEnabled && !string.IsNullOrWhiteSpace(phone))
        {
            try
            {
                await SendSmsAsync(phone, $"{title}: {message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS notification failed for {Phone}", phone);
            }
        }
    }

    private async Task SendEmailAsync(string toEmail, string firstName, string title, string message)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) ||
        string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            _logger.LogWarning("Email enabled but SmtpHost/FromEmail is not configured.");
            return;
        }

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.SmtpUseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        }
        ;

        if (!string.IsNullOrWhiteSpace(_settings.SmtpUser))
        {
            client.Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword);
        }

        var body = new StringBuilder();
        body.AppendLine($"Hello {firstName},");
        body.AppendLine();
        body.AppendLine(message);
        body.AppendLine();
        body.AppendLine("— ArnoxPay (QwaQwa estate)");
        body.AppendLine("This is an automated message. Please do not reply if this mailbox is unattended.");

        using var mail = new MailMessage
        {
            From = new MailAddress(
            _settings.FromEmail,
            string.IsNullOrWhiteSpace(_settings.FromDisplayName) ? "ArnoxPay" : _settings.FromDisplayName),
            Subject = $"[ArnoxPay] {title}",
            Body = body.ToString(),
            IsBodyHtml = false
        }
        ;
        mail.To.Add(toEmail);

        await client.SendMailAsync(mail);
        _logger.LogInformation("Email sent to {Email} — {Title}", toEmail, title);
    }

    private async Task SendSmsAsync(string toPhone, string text)
    {
        // Normalise to digits / + for Twilio
        var to = toPhone.Trim().Replace(" ", "");
        if (!to.StartsWith('+') && to.StartsWith("0") && to.Length >= 10)
        {
            // South Africa local → E.164 (common for UFS demo)
            to = "+27" + to.TrimStart('0');
        }

        if (string.IsNullOrWhiteSpace(_settings.TwilioAccountSid) ||
        string.IsNullOrWhiteSpace(_settings.TwilioAuthToken) ||
        string.IsNullOrWhiteSpace(_settings.TwilioFromNumber))
        {
            _logger.LogWarning(
            "SMS enabled but Twilio is not configured. Would send to {Phone}: {Text}",
            to, text.Length > 80 ? text[..80] + "..." : text);
            return;
        }

        // Twilio REST API (no extra NuGet package required)
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_settings.TwilioAccountSid}/Messages.json";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.TwilioFromNumber,
            ["Body"] = text.Length > 160 ? text[..157] + "..." : text
        }
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = content
        }
        ;
        var credentials = Convert.ToBase64String(
        Encoding.ASCII.GetBytes($"{_settings.TwilioAccountSid}:{_settings.TwilioAuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Twilio SMS failed ({Status}): {Body}", response.StatusCode, body);
            return;
        }

        _logger.LogInformation("SMS sent to {Phone}", to);
    }
}
