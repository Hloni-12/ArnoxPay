using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class PaymentReviewModel
{
    public int PaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    [StringLength(100)]
    public string? OfficerReference { get; set; }
}
