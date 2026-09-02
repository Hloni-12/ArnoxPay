using ArnoxPay.Models;

namespace ArnoxPay.Infrastructure;

public interface ITransactionService
{
    Task<Transaction> CreateForAcceptedPaymentAsync(
    Payment payment,
    int officerId);
}
