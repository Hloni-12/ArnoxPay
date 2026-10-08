using ArnoxPay.Models;

namespace ArnoxPay.Infrastructure;

public interface ICampusAccountService
{
    Task<CampusAccount> GetOrCreateAsync(int customerId);
    Task<decimal> GetBalanceAsync(int customerId);
    Task<(bool Ok, string Message, CampusAccountTransfer? Transfer)> DepositAsync(int customerId, decimal amount, string? fundingSource);
    Task<(bool Ok, string Message)> DebitAsync(int customerId, decimal amount, int paymentId, string txnRef);
}
