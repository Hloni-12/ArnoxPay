using System.Globalization;

namespace ArnoxPay.Infrastructure;

public sealed class CardValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? LastFour { get; init; }
    public string? MaskedNumber { get; init; }
}

/// <summary>Server-side Mastercard validation. Never logs or persists full PAN/CVV.</summary>
public static class CardPaymentValidator
{
    public static CardValidationResult Validate(
        string? cardholderName, string? cardNumber,
        string? expiryMonth, string? expiryYear, string? cvv, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(cardholderName) || cardholderName.Trim().Length < 2)
            return Fail("Cardholder name is required.");
        if (amount <= 0)
            return Fail("Payment amount is invalid");

        var digits = DigitsOnly(cardNumber);
        if (digits.Length != 16 || !PassesLuhn(digits))
            return Fail("Invalid card number");
        if (!IsMastercard(digits))
            return Fail("Invalid card number"); // or "Card is not a supported Mastercard."

        if (!TryParseExpiry(expiryMonth, expiryYear, out var m, out var y))
            return Fail("Expiry date is invalid.");
        var now = DateTime.UtcNow;
        if (y < now.Year || (y == now.Year && m < now.Month))
            return Fail("Card has expired");

        if (DigitsOnly(cvv).Length != 3)
            return Fail("Invalid CVV");

        var last4 = digits[^4..];
        return new CardValidationResult
        {
            IsValid = true,
            LastFour = last4,
            MaskedNumber = $"Mastercard •••• {last4}"
        };
    }

    public static bool IsMastercard(string d)
    {
        if (d.Length != 16) return false;
        if (int.TryParse(d[..2], out var two) && two is >= 51 and <= 55) return true;
        if (int.TryParse(d[..4], out var four) && four is >= 2221 and <= 2720) return true;
        return false;
    }

    public static bool PassesLuhn(string digits)
    {
        var sum = 0; var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alt) { n *= 2; if (n > 9) n -= 9; }
            sum += n; alt = !alt;
        }
        return sum % 10 == 0;
    }

    private static bool TryParseExpiry(string? month, string? year, out int m, out int y)
    {
        m = 0; y = 0;
        if (string.IsNullOrWhiteSpace(month) || string.IsNullOrWhiteSpace(year)) return false;
        if (!int.TryParse(month.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out m) || m is < 1 or > 12) return false;
        if (!int.TryParse(year.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out y)) return false;
        if (year!.Trim().Length == 2) y += 2000;
        return y is >= 2000 and <= 2100;
    }

    private static string DigitsOnly(string? v) =>
        string.IsNullOrEmpty(v) ? "" : new string(v.Where(char.IsDigit).ToArray());

    private static CardValidationResult Fail(string msg) =>
        new() { IsValid = false, ErrorMessage = msg };
}
