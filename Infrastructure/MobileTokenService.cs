using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ArnoxPay.Infrastructure;

public sealed class MobileTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);
    private readonly IDataProtector _protector;

    public MobileTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ArnoxPay.Mobile.AccessToken.v1");
    }

    public IssuedMobileToken Issue(string userId, string? securityStamp)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        var payload = new MobileTokenPayload(userId, securityStamp ?? string.Empty, expiresAt.ToUnixTimeSeconds());
        var token = _protector.Protect(JsonSerializer.Serialize(payload));
        return new IssuedMobileToken(token, expiresAt.UtcDateTime);
    }

    public bool TryReadBearer(string? authorizationHeader, out MobileTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authorizationHeader[7..].Trim();
        if (token.Length is < 20 or > 4096) return false;

        try
        {
            var json = _protector.Unprotect(token);
            var decoded = JsonSerializer.Deserialize<MobileTokenPayload>(json);
            if (decoded is null || string.IsNullOrWhiteSpace(decoded.UserId) ||
                decoded.ExpiresAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return false;
            payload = decoded;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record IssuedMobileToken(string AccessToken, DateTime ExpiresAtUtc);
public sealed record MobileTokenPayload(string UserId, string SecurityStamp, long ExpiresAtUnix);
