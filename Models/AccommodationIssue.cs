using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class AccommodationIssue
{
    public int Id { get; set; }
    public int AccomodationId { get; set; }
    public Accomodation? Accomodation { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public AccommodationIssueCategory Category { get; set; }
    [Required, StringLength(160)]
    public string Title { get; set; }
    = string.Empty;
    [Required, StringLength(2000)]
    public string Description { get; set; }
    = string.Empty;
    public AccommodationIssueStatus Status { get; set; }
    = AccommodationIssueStatus.Reported;
    [StringLength(1000)]
    public string? ResolutionNote { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
