namespace ArnoxPay.Infrastructure;

/// <summary>
/// Demonstration gateway only. Does not contact any bank or card network.
/// </summary>
public sealed class DemoPaymentGateway : IPaymentGateway
{
    public string EnvironmentName => "DEMO PAYMENT ENVIRONMENT";
    public bool IsLiveGateway => false;

    public Task<PaymentGatewayResult> AuthenticateAsync(PaymentAuthRequest request, CancellationToken ct = default)
    {
        if (!request.DemoConfirm)
        {
            return Task.FromResult(new PaymentGatewayResult
            {
                Success = false,
                StatusCode = "AUTH_DECLINED",
                Message = "Demo authentication was cancelled or declined. The transaction was not completed."
            });
        }

        return Task.FromResult(new PaymentGatewayResult
        {
            Success = true,
            StatusCode = "AUTH_OK",
            Message = "Demo authentication confirmed. Proceeding to processing.",
            ProviderReference = $"DEMO-{request.TransactionReference}"
        });
    }

    public async Task<PaymentGatewayResult> ProcessAsync(PaymentProcessRequest request, CancellationToken ct = default)
    {
        // Brief simulated processing delay (demo only)
        await Task.Delay(1200, ct);

        return new PaymentGatewayResult
        {
            Success = true,
            StatusCode = "PROC_OK",
            Message = "Demo processing completed successfully.",
            ProviderReference = $"DEMO-PROC-{request.TransactionReference}"
        };
    }
}
