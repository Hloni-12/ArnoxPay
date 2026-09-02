using ArnoxPay.Models;
using Microsoft.AspNetCore.Identity;

namespace ArnoxPay.Data;

/// <summary>
/// Seeds Identity roles and the initial administrator account only.
/// Does NOT create the database or run migrations.
/// Create ArnoxPayDB in SSMS and apply schema (EF migrations or SQL script) before running the web app.
/// </summary>
public static class AdministratorAccountSeeder
{
    public const string DefaultAdminEmail = "administration@ufs.ac.za";
    public const string DefaultAdminPassword = "Admin@12345";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        try
        {
            await CreateRolesAsync(roleManager);
            await CreateAdministratorAsync(userManager);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
            "AdministratorAccountSeeder failed. " +
            "Ensure database ArnoxPayDB already exists on SQL Server, " +
            "tables were created (migrations or SQL script), and the connection string is correct. " +
            $"Inner error: {ex.Message}", ex);
        }
    }

    private static async Task CreateRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var roleName in SystemRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            continue;

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Unable to create role '{roleName}': {errors}");
            }
        }
    }

    private static async Task CreateAdministratorAsync(UserManager<AppUser> userManager)
    {
        var admin = await userManager.FindByEmailAsync(DefaultAdminEmail);
        if (admin != null)
        {
            if (!await userManager.IsInRoleAsync(admin, SystemRoles.Administrator))
            {
                var roleResult = await userManager.AddToRoleAsync(admin, SystemRoles.Administrator);
                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Unable to assign Administrator role: {errors}");
                }
            }
            return;
        }

        admin = new AppUser
        {
            UserName = DefaultAdminEmail,
            Email = DefaultAdminEmail,
            EmailConfirmed = true,
            FirstName = "ArnoxPay",
            LastName = "Administrator",
            IsActive = true,
            IsStaffCreation = true
        }
        ;

        var createResult = await userManager.CreateAsync(admin, DefaultAdminPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Unable to create administrator account: {errors}");
        }

        var addRoleResult = await userManager.AddToRoleAsync(admin, SystemRoles.Administrator);
        if (!addRoleResult.Succeeded)
        {
            var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Unable to assign Administrator role: {errors}");
        }
    }
}
