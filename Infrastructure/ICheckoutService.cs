using ArnoxPay.Models;

namespace ArnoxPay.Infrastructure;

public interface ICheckoutService
{
    string NextTransactionReference();
    Task<Payment> CreateCheckoutAsync(int customerId, PaymentType type, decimal amount, string accountReference, string? notes);
    Task SetPaymentMethodAsync(Payment payment, string method);
    Task<PaymentGatewayResult> AuthenticateAsync(Payment payment, bool confirmed);
    Task<PaymentGatewayResult> ProcessAsync(Payment payment);
    Task MarkFailedAsync(Payment payment, string reason);
}
