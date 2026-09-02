using System.ComponentModel.DataAnnotations.Schema;
namespace ArnoxPay.Models;

public class ParkingSession
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int ParkingBayId { get; set; }
    public ParkingBay? ParkingBay { get; set; }
    public DateTime EntryTime { get; set; }
    = DateTime.UtcNow;
    public DateTime? ExitTime { get; set; }
    public ParkingSessionStatus Status { get; set; }
    = ParkingSessionStatus.Active;
    [Column(TypeName = "decimal(18,2)")]
    public decimal Fee { get; set; }
}
