using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;
public class ContactStaffModel
{
 [Required, StringLength(100)] public string Name {get;set;}=string.Empty;
 [Required,EmailAddress, StringLength(256)] public string Email {get;set;}=string.Empty;
 [Required] public string RecipientRole {get;set;}=string.Empty;
 [Required,StringLength(120)] public string Subject {get;set;}=string.Empty;
 [Required,StringLength(4000,MinimumLength=10)] public string Message {get;set;}=string.Empty;
}
