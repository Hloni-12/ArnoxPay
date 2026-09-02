namespace ArnoxPay.Infrastructure;

/// <summary>
/// Abstraction for payment authentication/processing.
/// Demo implementation simulates auth; a real gateway can replace this later.
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
    /// <summary>Demo only: true = confirm authentication, false = reject.</summary>
    public bool DemoConfirm { get; init; }
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
