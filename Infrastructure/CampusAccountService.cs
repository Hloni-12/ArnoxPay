using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Infrastructure;

public sealed class CampusAccountService(AppDbContext context) : ICampusAccountService
{
    public async Task<CampusAccount> GetOrCreateAsync(int customerId)
    {
        var a = await context.CampusAccounts.FirstOrDefaultAsync(x => x.CustomerId == customerId);
        if (a is not null) return a;
        a = new CampusAccount { CustomerId = customerId, Balance = 0, UpdatedAtUtc = DateTime.UtcNow };
        context.CampusAccounts.Add(a);
        await context.SaveChangesAsync();
        return a;
    }

    public async Task<decimal> GetBalanceAsync(int customerId) =>
        (await GetOrCreateAsync(customerId)).Balance;

    public async Task<(bool Ok, string Message, CampusAccountTransfer? Transfer)> DepositAsync(
        int customerId, decimal amount, string? fundingSource)
    {
        if (amount <= 0) return (false, "Payment amount is invalid", null);
        var a = await GetOrCreateAsync(customerId);
        var before = a.Balance;
        var after = before + amount;
        var t = new CampusAccountTransfer
        {
            CampusAccountId = a.Id,
            TransferReference = $"CAF-{DateTime.UtcNow.Year}-{context.CampusAccountTransfers.Count() + 1:D6}",
            Amount = amount,
            BalanceBefore = before,
            BalanceAfter = after,
            Description = "Campus account funding",
            FundingSource = fundingSource,
            CreatedAtUtc = DateTime.UtcNow
        };
        a.Balance = after;
        a.UpdatedAtUtc = DateTime.UtcNow;
        context.CampusAccountTransfers.Add(t);
        await context.SaveChangesAsync();
        return (true, $"Previous Balance: R{before:N2}. Transferred: R{amount:N2}. New Balance: R{after:N2}.", t);
    }

    public async Task<(bool Ok, string Message)> DebitAsync(int customerId, decimal amount, int paymentId, string txnRef)
    {
        if (amount <= 0) return (false, "Payment amount is invalid");
        var a = await GetOrCreateAsync(customerId);
        if (a.Balance < amount)
            return (false, $"Insufficient funds. Your campus account balance is R{a.Balance:N2}, but you need R{amount:N2}.");
        var before = a.Balance;
        var after = before - amount;
        context.CampusAccountTransfers.Add(new CampusAccountTransfer
        {
            CampusAccountId = a.Id,
            TransferReference = $"CAF-{DateTime.UtcNow.Year}-{context.CampusAccountTransfers.Count() + 1:D6}",
            Amount = amount,
            BalanceBefore = before,
            BalanceAfter = after,
            Description = $"Payment {txnRef}",
            PaymentId = paymentId,
            CreatedAtUtc = DateTime.UtcNow
        });
        a.Balance = after;
        a.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return (true, "Campus account debited.");
    }
}
