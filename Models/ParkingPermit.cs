using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ArnoxPay.Models;

public class ParkingPermit
{
    public int Id { get; set; }
    [Required, StringLength(40)]
    public string PermitNumber { get; set; }
    = string.Empty;
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    [StringLength(40)]
    public string PermitType { get; set; }
    = "Student annual";
    public DateTime IssueDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public ParkingPermitStatus Status { get; set; }
    = ParkingPermitStatus.PendingApproval;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Fee { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    [StringLength(500)]
    public string? ReviewNote { get; set; }
}
