using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class FinancialOfficer
{
    public int Id { get; set; }
    [Required]
    public string AppUserId { get; set; }
    = string.Empty;
    public AppUser? AppUser { get; set; }
    [Required, StringLength(30)]
    public string EmployeeNumber { get; set; }
    = string.Empty;
    public bool IsAuthorized { get; set; }
    = true;
    public DateTime AuthorizedAtUtc { get; set; }
    = DateTime.UtcNow;
    public ICollection<Payment> ReviewedPayments { get; set; }
    = new List<Payment>();
    public ICollection<Transaction> ProcessedTransactions { get; set; }
    = new List<Transaction>();
}
