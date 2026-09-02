using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class Transaction
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    [Required, StringLength(40)]
    public string TransactionReference { get; set; }
    = string.Empty;
    [Range(0.01, 999999.99)]
    public decimal Amount { get; set; }
    public TransactionStatus Status { get; set; }
    = TransactionStatus.Initiated;
    public DateTime CreatedAtUtc { get; set; }
    = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int? ProcessedByFinancialOfficerId { get; set; }
    public FinancialOfficer? ProcessedByFinancialOfficer { get; set; }
}
