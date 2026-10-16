using System.ComponentModel.DataAnnotations;
using ArnoxPay.Models;

namespace ArnoxPay.Models.ViewModels;

public class UtilityPaymentViewModel
{
    public PaymentType PaymentType { get; set; }

    [Required]
    public int? AccomodationId { get; set; }

    [Required, Range(0.01, 50000)]
    public decimal Quantity { get; set; }

    public string UnitOfMeasure { get; set; } = "kWh";

    public decimal? UnitPrice { get; set; }

    public decimal Subtotal => Quantity * (UnitPrice ?? 0m);

    public decimal AdditionalFees { get; set; }

    public decimal Total => Subtotal + AdditionalFees;

    public string? Notes { get; set; }

    public string AccommodationDisplay { get; set; } = "";
}
