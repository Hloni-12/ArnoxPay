using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Infrastructure;

public interface IEmailDeliveryService
{
    Task<bool> SendAsync(string to, string subject, string body);
}

/// <summary>Best-effort outbound SMTP delivery using the application's existing NotificationSettings.</summary>
public sealed class EmailDeliveryService(
    IOptions<NotificationSettings> options,
    ILogger<EmailDeliveryService> logger) : IEmailDeliveryService
{
    private readonly NotificationSettings _settings = options.Value;

    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        if (!_settings.EmailEnabled || string.IsNullOrWhiteSpace(_settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(_settings.FromEmail) || string.IsNullOrWhiteSpace(_settings.SmtpUser) ||
            string.IsNullOrWhiteSpace(_settings.SmtpPassword))
        {
            logger.LogWarning("ArnoxPay email not sent: email is disabled or SMTP credentials/sender are incomplete.");
            return false;
        }

        try
        {
            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.SmtpUseSsl,
                Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000
            };
            using var email = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, string.IsNullOrWhiteSpace(_settings.FromDisplayName)
                    ? "ArnoxPay" : _settings.FromDisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            email.To.Add(new MailAddress(to));
            await client.SendMailAsync(email).WaitAsync(TimeSpan.FromSeconds(15));
            logger.LogInformation("ArnoxPay email delivery succeeded.");
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ArnoxPay email delivery failed.");
            return false;
        }
    }
}
