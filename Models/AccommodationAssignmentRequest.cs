using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class AccommodationAssignmentRequest
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int AccomodationId { get; set; }
    public Accomodation? Accomodation { get; set; }
    public AccommodationAssignmentStatus Status { get; set; }
    = AccommodationAssignmentStatus.Pending;
    [StringLength(1000)]
    public string? CustomerNote { get; set; }
    [StringLength(1000)]
    public string? AdministratorNote { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}
