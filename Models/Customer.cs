using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class Customer
{
    public int Id { get; set; }

    [Required]
    public string AppUserId { get; set; } = string.Empty;

    public AppUser? AppUser { get; set; }

    /// <summary>Student or Staff under the Customer role.</summary>
    public CustomerCategory Category { get; set; }

    /// <summary>Student or Staff number used for login (unique).</summary>
    [Required, StringLength(30)]
    public string UserNumber { get; set; } = string.Empty;

    /// <summary>Official university student/staff number (optional, any length).</summary>
    [StringLength(30)]
    public string? UniversityNumber { get; set; }

    [StringLength(200)]
    public string? ResidentialAddress { get; set; }

    public int? AccomodationId { get; set; }
    public Accomodation? Accomodation { get; set; }

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AccommodationIssue> AccommodationIssues { get; set; } = new List<AccommodationIssue>();
    public ICollection<AccommodationAssignmentRequest> AccommodationAssignmentRequests { get; set; } = new List<AccommodationAssignmentRequest>();
    public ICollection<ServiceReview> ServiceReviews { get; set; } = new List<ServiceReview>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<ParkingPermit> ParkingPermits { get; set; } = new List<ParkingPermit>();
}
