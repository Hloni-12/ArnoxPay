using System.ComponentModel.DataAnnotations.Schema;

namespace ArnoxPay.Models;

public class CampusAccount
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<CampusAccountTransfer> Transfers { get; set; } = new List<CampusAccountTransfer>();
}
