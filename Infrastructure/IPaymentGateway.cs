namespace ArnoxPay.Infrastructure;

/// <summary>
/// Abstraction for payment submission processing.
/// Implementations persist outcomes via the checkout service into SQL Server.
/// </summary>
public interface IPaymentGateway
{
    string EnvironmentName { get; }
    bool IsLiveGateway { get; }

    Task<PaymentGatewayResult> AuthenticateAsync(PaymentAuthRequest request, CancellationToken ct = default);
    Task<PaymentGatewayResult> ProcessAsync(PaymentProcessRequest request, CancellationToken ct = default);
}

public sealed class PaymentAuthRequest
{
    public string TransactionReference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    /// <summary>True when the customer confirms submission; false when cancelled.</summary>
    public bool Confirmed { get; init; }
}

public sealed class PaymentProcessRequest
{
    public string TransactionReference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string ServiceName { get; init; } = string.Empty;
}

public sealed class PaymentGatewayResult
{
    public bool Success { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ProviderReference { get; init; }
}
