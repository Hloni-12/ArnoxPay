using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
namespace ArnoxPay.Models.ViewModels;

public class AccomodationImageUploadModel
{
    public int AccomodationId { get; set; }
    [Required]
    public IFormFile? ImageFile { get; set; }
    [StringLength(160)]
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
}
