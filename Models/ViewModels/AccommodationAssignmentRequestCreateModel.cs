using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class AccommodationAssignmentRequestCreateModel
{
    public int AccomodationId { get; set; }
    [StringLength(1000)]
    public string? CustomerNote { get; set; }
    public bool RequiresAccessibleRoom { get; set; }
}
