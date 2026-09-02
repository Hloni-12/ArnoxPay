using System.ComponentModel.DataAnnotations;
namespace ArnoxPay.Models.ViewModels;

public class FinancialOfficerCreateModel
{
    [Required, StringLength(80)]
    public string FirstName { get; set; }
    = string.Empty;
    [Required, StringLength(80)]
    public string LastName { get; set; }
    = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; }
    = string.Empty;
    [Required]
    public string PhoneNumber { get; set; }
    = string.Empty;
    [Required, StringLength(30)]
    public string EmployeeNumber { get; set; }
    = string.Empty;
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; }
    = string.Empty;
}
