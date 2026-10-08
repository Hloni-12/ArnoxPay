using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

public class AccountController(
    AppDbContext context,
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    SignInManager<AppUser> signInManager,
    INotificationService notifications) : Controller
{

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var loginId = model.LoginId.Trim();
        AppUser? user = null;

        if (loginId.Contains('@', StringComparison.Ordinal))
        {
            // Admin / Financial Officer (or customer email)
            user = await userManager.FindByEmailAsync(loginId);
        }
        else
        {
            // Customer login via Student/Staff number (Identity UserName)
            user = await userManager.FindByNameAsync(loginId);
            if (user is null)
            {
                var customer = await context.Customers.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.UserNumber == loginId);
                if (customer is not null)
                    user = await userManager.FindByIdAsync(customer.AppUserId);
            }
        }

        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Student/Staff number or email, or password is incorrect, or the account is inactive.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Student/Staff number or email, or password is incorrect.");
            return View(model);
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(SystemRoles.Administrator)) return RedirectToAction("Index", "AdminHome");
        if (roles.Contains(SystemRoles.FinancialOfficer)) return RedirectToAction("Dashboard", "Payment");
        return LocalRedirect(model.ReturnUrl ?? Url.Action("Index", "Customer")!);
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Register()
    {
        if (!await context.Roles.AnyAsync())
        {
            TempData["Error"] = "Initial administrator setup must be completed before customer registration.";
            return RedirectToAction(nameof(InitialAdmin));
        }
        return View(new RegisterModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!await context.Roles.AnyAsync())
        {
            TempData["Error"] = "Initial administrator setup must be completed before customer registration.";
            return RedirectToAction(nameof(InitialAdmin));
        }

        if (model.Category is null)
            ModelState.AddModelError(nameof(model.Category), "Please select whether you are a Student or Staff member.");

        var numberLabel = model.Category == CustomerCategory.Staff ? "Staff Number" : "Student Number";

        if (!string.IsNullOrWhiteSpace(model.UserNumber))
        {
            var number = model.UserNumber.Trim();

            if (model.Category == CustomerCategory.Staff)
            {
                // Staff Number: exactly 7 digits, must start with 0 (e.g. 0123456) — strict
                if (!System.Text.RegularExpressions.Regex.IsMatch(number, @"^0\d{6}$"))
                {
                    ModelState.AddModelError(nameof(model.UserNumber),
                        "Staff Number is invalid");
                }
            }
            else if (model.Category == CustomerCategory.Student)
            {
                // Student Number: exactly 10 digits
                if (!System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d{10}$"))
                {
                    ModelState.AddModelError(nameof(model.UserNumber),
                        "Student Number is invalid");
                }
            }

            var existsNumber = await context.Customers.AnyAsync(c => c.UserNumber == number);
            if (existsNumber)
                ModelState.AddModelError(nameof(model.UserNumber), $"This {numberLabel} is already registered.");

            var existsUserName = await userManager.FindByNameAsync(number);
            if (existsUserName is not null)
                ModelState.AddModelError(nameof(model.UserNumber), $"This {numberLabel} is already registered.");
        }

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var email = model.Email.Trim();

            if (model.Category == CustomerCategory.Staff)
            {
                // Staff email: letters only before @, must end exactly with @ufs.ac.za
                // Examples: sefakol@ufs.ac.za, sefakoLP@ufs.ac.za
                if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[A-Za-z]+@ufs\.ac\.za$"))
                {
                    ModelState.AddModelError(
                        nameof(model.Email),
                        "Incorrect Email");
                }
            }
            else if (model.Category == CustomerCategory.Student)
            {
                // Student email: digits only before @, must end exactly with @ufs4life.ac.za
                if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^\d+@ufs4life\.ac\.za$"))
                {
                    ModelState.AddModelError(
                        nameof(model.Email),
                        "Incorrect Email");
                }
            }

            var existsEmail = await userManager.FindByEmailAsync(email);
            if (existsEmail is not null)
            {
                ModelState.AddModelError(
                    nameof(model.Email),
                    "This email address is already registered.");
            }
        }

        // IMPORTANT: re-check ModelState after all custom validation above.
        // Without this, errors added by AddModelError were being ignored and
        // the account was created anyway (this was the bug).
        if (!ModelState.IsValid) return View(model);

        var firstName = model.Name.Trim();
        var lastName = model.Surname.Trim();
        var fullName = $"{firstName} {lastName}".Trim();

        var userNumber = model.UserNumber.Trim();
        var registeredEmail = model.Email.Trim();

        var user = new AppUser
        {
            UserName = userNumber, // login with User Number
            Email = registeredEmail,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = false
        };

        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await userManager.AddToRoleAsync(user, SystemRoles.Customer);

        var customer = new Customer
        {
            AppUserId = user.Id,
            Category = model.Category!.Value,
            UserNumber = userNumber
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        try
        {
            await notifications.CreateAsync(
                customer.Id,
                "Registration successful",
                $"Welcome to ArnoxPay, {fullName}. Your {model.Category} account ({numberLabel} {userNumber}) is ready. You can manage payments, accommodation, and permits from your dashboard.");
        }
        catch
        {
            // in-app notification is best-effort at registration
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Customer account created successfully. A confirmation has been sent to your registered email when email is enabled.";
        return RedirectToAction("Index", "Customer");
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> InitialAdmin()
    {
        if (await context.Users.AnyAsync()) return RedirectToAction(nameof(Login));
        return View(new AdministratorSetupModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InitialAdmin(AdministratorSetupModel model)
    {
        if (await context.Users.AnyAsync()) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) return View(model);
        foreach (var role in SystemRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    return View(model);
                }
            }
        }
        var admin = new AppUser
        {
            UserName = model.Email,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            FirstName = model.FirstName,
            LastName = model.LastName,
            IsStaffCreation = true
        };
        var create = await userManager.CreateAsync(admin, model.Password);
        if (!create.Succeeded)
        {
            foreach (var error in create.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await userManager.AddToRoleAsync(admin, SystemRoles.Administrator);
        await signInManager.SignInAsync(admin, false);
        TempData["Success"] = "Initial administrator created. You can now add financial officers.";
        return RedirectToAction("Index", "AdminHome");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}