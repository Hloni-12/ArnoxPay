using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;

public class ServiceReview
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [Range(1, 5)]
    public int Rating { get; set; }
    [Required, StringLength(80)]
    public string Category { get; set; }
    = "General";
    [Required, StringLength(1000)]
    public string Comment { get; set; }
    = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    = DateTime.UtcNow;
}
