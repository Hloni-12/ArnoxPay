using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ArnoxPay.Models;

// This follows the custom identity-user naming pattern used by the reference architecture.
public class AppUser : IdentityUser
{
    [Required, StringLength(80)]
    public string FirstName { get; set; }
    = string.Empty;

    [Required, StringLength(80)]
    public string LastName { get; set; }
    = string.Empty;

    public bool IsActive { get; set; }
    = true;
    public DateTime CreatedAtUtc { get; set; }
    = DateTime.UtcNow;

    public Customer? Customer { get; set; }
    public FinancialOfficer? FinancialOfficer { get; set; }

    // Transient flag only — never persisted. Set by a controller right before
    // userManager.CreateAsync(...) to tell StaffEmailDomainValidator that this
    // account is an Admin/FinancialOfficer account, not a Customer one, so the
    // institutional-domain rule should be enforced for it.
    [NotMapped]
    public bool IsStaffCreation { get; set; }
}
