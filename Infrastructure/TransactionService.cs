using ArnoxPay.Data;
using ArnoxPay.Models;

namespace ArnoxPay.Infrastructure;

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _context;

    public TransactionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction> CreateForAcceptedPaymentAsync(
    Payment payment,
    int officerId)
    {
        var transaction = new Transaction
        {
            PaymentId = payment.Id,
            Amount = payment.Amount,
            ProcessedByFinancialOfficerId = officerId,
            Status = TransactionStatus.Completed,
            TransactionReference =
            $"AP-{DateTime.UtcNow:yyyyMMddHHmmss}-{payment.Id:D6}",
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        }
        ;

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        return transaction;
    }
}
