
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ArnoxPay.Models;

public class Accomodation
{
    public int Id { get; set; }

    // Firestore document ID, separate from the existing integer ID.
    [NotMapped]
    public string? FirestoreId { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string UnitNumber { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Address { get; set; } = string.Empty;

    public AccommodationType AccommodationType { get; set; }

    [Range(1, 20)]
    public int Capacity { get; set; } = 1;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Customer> Residents { get; set; }
        = new List<Customer>();

    public ICollection<AccomodationImage> Images { get; set; }
        = new List<AccomodationImage>();

    public ICollection<AccommodationIssue> Issues { get; set; }
        = new List<AccommodationIssue>();

    public ICollection<AccommodationAssignmentRequest> AssignmentRequests
    { get; set; }
        = new List<AccommodationAssignmentRequest>();
}
