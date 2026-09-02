using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class LoginModel
{
    [Required(ErrorMessage = "Email address or Student/Staff number is required.")]
    [Display(Name = "Email Address")]
    public string LoginId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
