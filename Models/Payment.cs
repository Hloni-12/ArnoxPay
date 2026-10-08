using System.ComponentModel.DataAnnotations;

namespace ArnoxPay.Models;

public class Payment
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public PaymentType PaymentType { get; set; }

    [Range(0.01, 999999.99)]
    public decimal Amount { get; set; }

    [Required, StringLength(120)]
    public string AccountReference { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Notes { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingAuthentication;

    /// <summary>Unique public reference e.g. ARN-TXN-2026-000123</summary>
    [StringLength(40)]
    public string? TransactionReference { get; set; }

    [StringLength(40)]
    public string? PaymentMethod { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AuthenticatedAtUtc { get; set; }
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    [StringLength(300)]
    public string? FailureReason { get; set; }

    public int? FinancialOfficerId { get; set; }
    public FinancialOfficer? FinancialOfficer { get; set; }

    [StringLength(100)]
    public string? OfficerReference { get; set; }

    public Transaction? Transaction { get; set; }

    /// <summary>e.g. Mastercard •••• 1234 — never full PAN.</summary>
    [StringLength(40)]
    public string? MaskedCardDisplay { get; set; }

    [StringLength(64)]
    public string? IdempotencyKey { get; set; }

    public ICollection<PaymentItem> Items { get; set; } = new List<PaymentItem>();
}

