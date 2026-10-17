using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models;
public class StaffMessage { public int Id {get;set;} [Required,StringLength(450)] public string SenderUserId {get;set;}=string.Empty; [Required,StringLength(450)] public string RecipientUserId {get;set;}=string.Empty; [Required,StringLength(2000)] public string Body {get;set;}=string.Empty; public DateTime SentAtUtc {get;set;}=DateTime.UtcNow; public DateTime? ReadAtUtc {get;set;} }
