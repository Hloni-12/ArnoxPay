using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ArnoxPay.Models;

public class CampusAccountTransfer
{
    public int Id { get; set; }
    public int CampusAccountId { get; set; }
    public CampusAccount? CampusAccount { get; set; }
    [Required, StringLength(40)]
    public string TransferReference { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceBefore { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }
    [StringLength(200)]
    public string? Description { get; set; }
    [StringLength(80)]
    public string? FundingSource { get; set; }
    public int? PaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
