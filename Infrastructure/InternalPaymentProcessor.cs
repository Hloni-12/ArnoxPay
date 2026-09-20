namespace ArnoxPay.Infrastructure;

/// <summary>
/// Internal ArnoxPay payment submission processor.
/// Records authentication/processing outcomes for Financial Officer review.
/// Does not contact external banks; FO approval still required for PAID status.
/// </summary>
public sealed class InternalPaymentProcessor : IPaymentGateway
{
    public string EnvironmentName => "ArnoxPay payment submission";
    public bool IsLiveGateway => true;

    public Task<PaymentGatewayResult> AuthenticateAsync(PaymentAuthRequest request, CancellationToken ct = default)
    {
        if (!request.Confirmed)
        {
            return Task.FromResult(new PaymentGatewayResult
            {
                Success = false,
                StatusCode = "AUTH_DECLINED",
                Message = "Payment submission was cancelled. The transaction was not completed."
            });
        }

        return Task.FromResult(new PaymentGatewayResult
        {
            Success = true,
            StatusCode = "AUTH_OK",
            Message = "Payment submission confirmed. Proceeding to processing.",
            ProviderReference = $"ARN-AUTH-{request.TransactionReference}"
        });
    }

    public Task<PaymentGatewayResult> ProcessAsync(PaymentProcessRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new PaymentGatewayResult
        {
            Success = true,
            StatusCode = "PROC_OK",
            Message = "Payment submitted successfully and is awaiting Financial Officer review.",
            ProviderReference = $"ARN-PROC-{request.TransactionReference}"
        });
    }
}
