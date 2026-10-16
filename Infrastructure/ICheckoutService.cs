using ArnoxPay.Models;

namespace ArnoxPay.Infrastructure;

public interface ICheckoutService
{
    string NextTransactionReference();
    Task<Payment> CreateCheckoutAsync(int customerId, PaymentType type, decimal amount, string accountReference, string? notes);
    Task<Payment> CreateMultiCheckoutAsync(int customerId, IReadOnlyList<CheckoutLineItem> items, string? notes, string? idempotencyKey);
    Task SetPaymentMethodAsync(Payment payment, string method);
    Task SetMaskedCardAsync(Payment payment, string maskedDisplay);
    Task<PaymentGatewayResult> AuthenticateAsync(Payment payment, bool confirmed);
    Task<PaymentGatewayResult> ProcessAsync(Payment payment);
    Task MarkFailedAsync(Payment payment, string reason);
}

public sealed class CheckoutLineItem
{
    public PaymentType PaymentType { get; init; }
    public decimal Amount { get; init; }
    public string? Description { get; init; }
    public int? VehicleId { get; init; }
    public string? VehicleRegistration { get; init; }
    public string? OutstandingChargeKey { get; init; }
    public string? AccountReference { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? UnitOfMeasure { get; init; }
    public int? AccommodationId { get; init; }
}
