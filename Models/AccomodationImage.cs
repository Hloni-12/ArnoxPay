using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class AccomodationImage
{
    public int Id { get; set; }
    public int AccomodationId { get; set; }
    public Accomodation? Accomodation { get; set; }
    [Required, StringLength(260)]
    public string ImagePath { get; set; }
    = string.Empty;
    [StringLength(160)]
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    = DateTime.UtcNow;
}
