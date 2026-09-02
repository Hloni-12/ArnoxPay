using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class Vehicle
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Required, StringLength(20)]
    public string RegistrationNumber { get; set; }
    = string.Empty;
    [StringLength(60)]
    public string? Make { get; set; }
    [StringLength(60)]
    public string? Model { get; set; }
    [StringLength(40)]
    public string? Colour { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
    = DateTime.UtcNow;
    public bool IsActive { get; set; }
    = true;
    public ICollection<ParkingPermit> ParkingPermits { get; set; }
    = new List<ParkingPermit>();
    public ICollection<ParkingSession> ParkingSessions { get; set; }
    = new List<ParkingSession>();
}
