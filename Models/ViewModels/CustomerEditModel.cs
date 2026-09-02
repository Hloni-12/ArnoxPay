using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class CustomerEditModel
{
    public int Id { get; set; }

    [Display(Name = "User Number")]
    public string UserNumber { get; set; } = string.Empty;

    [Display(Name = "Account type")]
    public CustomerCategory Category { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(80)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone")]
    public string? PhoneNumber { get; set; }

    [StringLength(30)]
    [Display(Name = "Official student/staff number")]
    public string? UniversityNumber { get; set; }

    [StringLength(200)]
    [Display(Name = "Residential address")]
    public string? ResidentialAddress { get; set; }
}
