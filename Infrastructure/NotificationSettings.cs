namespace ArnoxPay.Infrastructure;

/// <summary>
/// Bound from appsettings.json section "NotificationSettings".
/// Enable email (SMTP) and/or SMS (Twilio) without changing code.
/// </summary>
public class NotificationSettings
{
    public bool EmailEnabled {
        get;
        set;
    }
    public string SmtpHost {
        get;
        set;
    }
    = "smtp.gmail.com";
    public int SmtpPort {
        get;
        set;
    }
    = 587;
    public bool SmtpUseSsl {
        get;
        set;
    }
    = true;
    public string SmtpUser {
        get;
        set;
    }
    = string.Empty;
    public string SmtpPassword {
        get;
        set;
    }
    = string.Empty;
    public string FromEmail {
        get;
        set;
    }
    = string.Empty;
    public string FromDisplayName {
        get;
        set;
    }
    = "ArnoxPay";

    public bool SmsEnabled {
        get;
        set;
    }
    /// <summary>Twilio Account SID (optional).</summary>
    public string TwilioAccountSid {
        get;
        set;
    }
    = string.Empty;
    public string TwilioAuthToken {
        get;
        set;
    }
    = string.Empty;
    /// <summary>Twilio phone number in E.164 format, e.g. +2711...</summary>
    public string TwilioFromNumber {
        get;
        set;
    }
    = string.Empty;
}
