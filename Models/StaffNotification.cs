using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;
public class StaffNotification { public int Id {get;set;} [Required,StringLength(450)] public string UserId {get;set;}=string.Empty; [Required,StringLength(150)] public string Title {get;set;}=string.Empty; [Required,StringLength(500)] public string Message {get;set;}=string.Empty; [StringLength(300)] public string? Url {get;set;} public bool IsRead {get;set;} public DateTime CreatedAtUtc {get;set;}=DateTime.UtcNow; public DateTime? ReadAtUtc {get;set;} }
