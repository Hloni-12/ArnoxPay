using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class AccommodationIssueCreateModel
{
    public int AccomodationId { get; set; }
    public AccommodationIssueCategory Category { get; set; }
    [Required, StringLength(160)]
    public string Title { get; set; }
    = string.Empty;
    [Required, StringLength(2000)]
    public string Description { get; set; }
    = string.Empty;
}
