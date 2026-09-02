using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class AccommodationAssignmentReviewModel
{
    public int Id { get; set; }
    public AccommodationAssignmentStatus Status { get; set; }
    [StringLength(1000)]
    public string? AdministratorNote { get; set; }
}
