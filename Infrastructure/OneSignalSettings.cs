namespace ArnoxPay.Infrastructure;

/// <summary>OneSignal server credentials. Set AppId and RestApiKey through deployment secrets/environment variables.</summary>
public sealed class OneSignalSettings
{
    public string AppId { get; set; } = string.Empty;
    public string RestApiKey { get; set; } = string.Empty;
}
