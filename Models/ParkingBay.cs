using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class ParkingBay
{
    public int Id { get; set; }
    public int ParkingAreaId { get; set; }
    public ParkingArea? ParkingArea { get; set; }
    [Required, StringLength(20)]
    public string BayNumber { get; set; }
    = string.Empty;
    [StringLength(40)]
    public string BayType { get; set; }
    = "Standard";
    public ParkingBayStatus Status { get; set; }
    = ParkingBayStatus.Available;
    public ICollection<ParkingSession> Sessions { get; set; }
    = new List<ParkingSession>();
}
