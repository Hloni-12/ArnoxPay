
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

    /// <summary>Corridor within the unit (Property → Unit → Corridor → Room).</summary>
    [StringLength(80)]
    public string? Corridor { get; set; }

    /// <summary>Room identifier within the corridor. Null when a room has not been allocated.</summary>
    [StringLength(40)]
    public string? RoomNumber { get; set; }

    [Required, StringLength(200)]
    public string Address { get; set; } = string.Empty;

    public AccommodationType AccommodationType { get; set; }

    [Range(1, 20)]
    public int Capacity { get; set; } = 1;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Monthly rent in South African rand; null means the administrator has not entered a verified price.</summary>
    [Column(TypeName = "decimal(18,2)")]
    [Range(0, 1000000)]
    public decimal? MonthlyRent { get; set; }

    public AccommodationRoomType? RoomType { get; set; }

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
