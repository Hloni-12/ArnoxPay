using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class ParkingArea
{
    public int Id { get; set; }
    [Required, StringLength(120)]
    public string Name { get; set; }
    = string.Empty;
    [StringLength(200)]
    public string? Location { get; set; }
    public int Capacity { get; set; }
    public ParkingAreaStatus Status { get; set; }
    = ParkingAreaStatus.Open;
    public ICollection<ParkingBay> Bays { get; set; }
    = new List<ParkingBay>();
}
