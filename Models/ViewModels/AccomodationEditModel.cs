using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class AccomodationEditModel
{
    public int Id { get; set; }
    [Required, StringLength(120)]
    public string Name { get; set; }
    = string.Empty;
    [Required, StringLength(40)]
    public string UnitNumber { get; set; }
    = string.Empty;
    [Required, StringLength(200)]
    public string Address { get; set; }
    = string.Empty;
    public AccommodationType AccommodationType { get; set; }
    [Range(1, 20)]
    public int Capacity { get; set; }
    = 1;
    [StringLength(1000)]
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    = true;
}
