using ArnoxPay.Models;
using Microsoft.AspNetCore.Identity;

namespace ArnoxPay.Infrastructure;

/// <summary>
/// Restricts accounts created by the administrator as staff accounts
/// to approved institutional email domains.
/// </summary>
public class StaffEmailDomainValidator : IUserValidator<AppUser>
{
    private static readonly string[] AllowedStaffDomains =
    ["ufs.ac.za"];

    public Task<IdentityResult> ValidateAsync(
    UserManager<AppUser> manager,
    AppUser user)
    {
        // Normal customer/student accounts are not checked here.
        if (!user.IsStaffCreation)
        return Task.FromResult(IdentityResult.Success);

        var errors = new List<IdentityError>();
        var email = user.Email?.Trim().ToLowerInvariant()
        ?? string.Empty;

        var validDomain = AllowedStaffDomains.Any(domain =>
        email.EndsWith($"@{domain}",
        StringComparison.OrdinalIgnoreCase));

        if (!validDomain)
        {
            errors.Add(new IdentityError
            {
                Code = "StaffEmailDomainError",
                Description =
                "Administrator and staff accounts must use an approved UFS institutional email address."
            }
            );
        }

        return Task.FromResult(
        errors.Count == 0
        ? IdentityResult.Success
        : IdentityResult.Failed(errors.ToArray()));
    }
}
