using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Infrastructure;

/// <summary>
/// Checkout lifecycle. Gateway success never marks a payment as PAID.
/// PAID requires Financial Officer acceptance.
/// </summary>
public sealed class CheckoutService(AppDbContext context, IPaymentGateway gateway) : ICheckoutService
{
    public string NextTransactionReference()
    {
        var year = DateTime.UtcNow.Year;
        var seq = context.Payments.Count() + 1;
        return $"ARN-TXN-{year}-{seq:D6}";
    }

    public async Task<Payment> CreateCheckoutAsync(
        int customerId,
        PaymentType type,
        decimal amount,
        string accountReference,
        string? notes)
    {
        var payment = new Payment
        {
            CustomerId = customerId,
            PaymentType = type,
            Amount = amount,
            AccountReference = accountReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = PaymentStatus.AwaitingAuthentication,
            TransactionReference = NextTransactionReference(),
            SubmittedAtUtc = DateTime.UtcNow
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment;
    }

    public async Task SetPaymentMethodAsync(Payment payment, string method)
    {
        payment.PaymentMethod = method;
        await context.SaveChangesAsync();
    }

    public async Task<PaymentGatewayResult> AuthenticateAsync(Payment payment, bool confirmed)
    {
        if (payment.Status != PaymentStatus.AwaitingAuthentication)
        {
            return new PaymentGatewayResult
            {
                Success = false,
                StatusCode = "INVALID_STATE",
                Message = "This payment is not awaiting authentication."
            };
        }

        var result = await gateway.AuthenticateAsync(new PaymentAuthRequest
        {
            TransactionReference = payment.TransactionReference ?? payment.Id.ToString(),
            Amount = payment.Amount,
            ServiceName = payment.PaymentType.ToString(),
            Confirmed = confirmed
        });

        if (!result.Success)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = result.Message;
            payment.CompletedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return result;
        }

        payment.AuthenticatedAtUtc = DateTime.UtcNow;
        payment.Status = PaymentStatus.Processing;
        payment.ProcessingStartedAtUtc = DateTime.UtcNow;
        // Store gateway provider ref in notes if empty of gateway field
        if (!string.IsNullOrWhiteSpace(result.ProviderReference))
            payment.OfficerReference ??= result.ProviderReference;

        await context.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// After payment gateway processing succeeds, payment becomes PENDING REVIEW —
    /// NOT Accepted/PAID. Financial Officer must accept before PAID.
    /// </summary>
    public async Task<PaymentGatewayResult> ProcessAsync(Payment payment)
    {
        if (payment.Status != PaymentStatus.Processing)
        {
            return new PaymentGatewayResult
            {
                Success = false,
                StatusCode = "INVALID_STATE",
                Message = "This payment is not in a processing state."
            };
        }

        var result = await gateway.ProcessAsync(new PaymentProcessRequest
        {
            TransactionReference = payment.TransactionReference ?? payment.Id.ToString(),
            Amount = payment.Amount,
            ServiceName = payment.PaymentType.ToString()
        });

        if (!result.Success)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = result.Message;
            payment.CompletedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return result;
        }

        // CRITICAL: gateway success → pending FO review (NOT PAID)
        payment.Status = PaymentStatus.Pending;
        payment.FailureReason = null;
        payment.CompletedAtUtc = null; // not complete until FO accepts
        if (!string.IsNullOrWhiteSpace(result.ProviderReference))
            payment.OfficerReference ??= result.ProviderReference;

        await context.SaveChangesAsync();
        return result;
    }

    public async Task MarkFailedAsync(Payment payment, string reason)
    {
        payment.Status = PaymentStatus.Failed;
        payment.FailureReason = reason;
        payment.CompletedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
