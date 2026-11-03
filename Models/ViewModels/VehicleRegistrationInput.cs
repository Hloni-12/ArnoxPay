using System.ComponentModel.DataAnnotations;

namespace ArnoxPay.Models.ViewModels;

public sealed class VehicleRegistrationInput
{
    [Required(ErrorMessage = "Registration number is required.")]
    [StringLength(20, ErrorMessage = "Registration number cannot exceed 20 characters.")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [StringLength(60, ErrorMessage = "Make cannot exceed 60 characters.")]
    public string? Make { get; set; }

    [StringLength(60, ErrorMessage = "Model cannot exceed 60 characters.")]
    public string? Model { get; set; }

    [StringLength(40, ErrorMessage = "Colour cannot exceed 40 characters.")]
    public string? Colour { get; set; }
}
