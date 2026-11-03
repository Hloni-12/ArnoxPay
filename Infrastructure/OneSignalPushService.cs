using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Infrastructure;

public interface IOneSignalPushService
{
    Task<bool> SendToUserAsync(string userId, string title, string message, string route);
}

/// <summary>Sends transactional Android pushes through OneSignal. Credentials are server-only.</summary>
public sealed class OneSignalPushService(
    HttpClient httpClient,
    IOptions<OneSignalSettings> options,
    ILogger<OneSignalPushService> logger) : IOneSignalPushService
{
    private const string Endpoint = "https://api.onesignal.com/notifications";
    private readonly OneSignalSettings _settings = options.Value;

    public async Task<bool> SendToUserAsync(string userId, string title, string message, string route)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(_settings.AppId) ||
            string.IsNullOrWhiteSpace(_settings.RestApiKey))
        {
            logger.LogWarning("OneSignal push not sent: AppId or server REST key is not configured.");
            return false;
        }

        var payload = new
        {
            app_id = _settings.AppId,
            include_aliases = new { external_id = new[] { userId } },
            target_channel = "push",
            headings = new Dictionary<string, string> { ["en"] = title },
            contents = new Dictionary<string, string> { ["en"] = message },
            data = new { route }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Key", _settings.RestApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("OneSignal push failed with HTTP {StatusCode} for route {Route}.",
                    (int)response.StatusCode, route);
                return false;
            }

            logger.LogInformation("OneSignal push accepted for route {Route}.", route);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "OneSignal push delivery failed for route {Route}.", route);
            return false;
        }
    }
}
