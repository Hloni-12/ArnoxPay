using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class AccommodationIssueUpdateModel
{
    public int Id { get; set; }
    public AccommodationIssueStatus Status { get; set; }
    [StringLength(1000)]
    public string? ResolutionNote { get; set; }
}
