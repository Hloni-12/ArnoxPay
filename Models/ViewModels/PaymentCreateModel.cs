using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class PaymentCreateModel
{
    public PaymentType PaymentType { get; set; }
    [Required, Range(0.01, 999999.99)]
    public decimal Amount { get; set; }
    [Required, StringLength(120)]
    public string AccountReference { get; set; }
    = string.Empty;
    [StringLength(500)]
    public string? Notes { get; set; }
}
