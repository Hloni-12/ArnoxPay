using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

/// <summary>
/// Customer registration only. Fields: Account Type, Student/Staff Number, Name, Surname, Email, Password, Confirm Password.
/// Student Number = exactly 10 digits. Staff Number = exactly 7 digits starting with 0.
/// </summary>
public class RegisterModel
{
    [Required(ErrorMessage = "Please select whether you are a Student or Staff member.")]
    [Display(Name = "Account Type")]
    public CustomerCategory? Category { get; set; }

    /// <summary>Student Number (10 digits) or Staff Number (7 digits starting with 0).</summary>
    [Required(ErrorMessage = "This number is required.")]
    [StringLength(10, MinimumLength = 7, ErrorMessage = "Enter a valid Student/Staff Number.")]
    [Display(Name = "Student / Staff Number")]
    public string UserNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(80)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Surname is required.")]
    [StringLength(80)]
    [Display(Name = "Surname")]
    public string Surname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
