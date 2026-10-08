using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ArnoxPay.Models;

/// <summary>Line item under a Payment so multiple services/vehicles share one transaction reference.</summary>
public class PaymentItem
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public PaymentType PaymentType { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    [StringLength(200)]
    public string? Description { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    [StringLength(20)]
    public string? VehicleRegistration { get; set; }
    [StringLength(80)]
    public string? OutstandingChargeKey { get; set; }
    public bool IsPaid { get; set; }
}
