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

    /// <summary>Number of utility units purchased (kWh, kL, etc.).</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? Quantity { get; set; }

    /// <summary>Price per unit in ZAR.</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>Unit of measure e.g. kWh, kL.</summary>
    [StringLength(20)]
    public string? UnitOfMeasure { get; set; }

    /// <summary>Accommodation / unit this line item applies to.</summary>
    public int? AccomodationId { get; set; }
    public Accomodation? Accomodation { get; set; }

    /// <summary>Vehicle position for registration fees (1 = first/free, 2 = second, ...).</summary>
    public int? VehiclePosition { get; set; }
}
