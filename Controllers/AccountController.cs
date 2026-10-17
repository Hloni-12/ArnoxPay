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
    INotificationService notifications,
    MobileTokenService mobileTokens) : Controller
{
    // =====================================================================
    //  SHARED LOGIN LOGIC (used by both website and Android API)
    // =====================================================================
    private async Task<(AppUser? user, string? error)> ValidateLoginAsync(string loginId, string password)
    {
        loginId = (loginId ?? "").Trim();

        if (string.IsNullOrEmpty(loginId) || string.IsNullOrEmpty(password))
            return (null, "Login ID and password are required.");

        AppUser? user = null;

        if (loginId.Contains('@', StringComparison.Ordinal))
        {
            // Admin / Financial Officer / Customer email
            user = await userManager.FindByEmailAsync(loginId);
        }
        else
        {
            // Customer login via Student/Staff number
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
            return (null, "Student/Staff number or email, or password is incorrect, or the account is inactive.");

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        if (!result.Succeeded)
            return (null, "Student/Staff number or email, or password is incorrect.");

        return (user, null);
    }

    // =====================================================================
    //  WEBSITE (MVC) ACTIONS
    // =====================================================================

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var (user, error) = await ValidateLoginAsync(model.LoginId, model.Password);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            return View(model);
        }

        // Create the cookie session for the website
        await signInManager.SignInAsync(user, isPersistent: false);

        var roles = await userManager.GetRolesAsync(user);

        if (roles.Contains(SystemRoles.Administrator))
            return RedirectToAction("Index", "AdminHome");

        if (roles.Contains(SystemRoles.FinancialOfficer))
            return RedirectToAction("Dashboard", "Payment");

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
                if (!System.Text.RegularExpressions.Regex.IsMatch(number, @"^0\d{6}$"))
                    ModelState.AddModelError(nameof(model.UserNumber), "Staff Number is invalid");
            }
            else if (model.Category == CustomerCategory.Student)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d{10}$"))
                    ModelState.AddModelError(nameof(model.UserNumber), "Student Number is invalid");
            }

            if (await context.Customers.AnyAsync(c => c.UserNumber == number))
                ModelState.AddModelError(nameof(model.UserNumber), $"This {numberLabel} is already registered.");

            if (await userManager.FindByNameAsync(number) is not null)
                ModelState.AddModelError(nameof(model.UserNumber), $"This {numberLabel} is already registered.");
        }

        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var email = model.Email.Trim();

            if (model.Category == CustomerCategory.Staff)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[A-Za-z]+@ufs\.ac\.za$"))
                    ModelState.AddModelError(nameof(model.Email), "Incorrect Email");
            }
            else if (model.Category == CustomerCategory.Student)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^\d+@ufs4life\.ac\.za$"))
                    ModelState.AddModelError(nameof(model.Email), "Incorrect Email");
            }

            if (await userManager.FindByEmailAsync(email) is not null)
                ModelState.AddModelError(nameof(model.Email), "This email address is already registered.");
        }

        // Re-check after all custom validation
        if (!ModelState.IsValid)
            return View(model);

        var firstName = model.Name.Trim();
        var lastName = model.Surname.Trim();
        var fullName = $"{firstName} {lastName}".Trim();
        var userNumber = model.UserNumber.Trim();
        var registeredEmail = model.Email.Trim();

        var user = new AppUser
        {
            UserName = userNumber,
            Email = registeredEmail,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = false
        };

        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
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
            // best-effort notification
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = "Customer account created successfully. A confirmation has been sent to your registered email when email is enabled.";
        return RedirectToAction("Index", "Customer");
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> InitialAdmin()
    {
        if (await context.Users.AnyAsync())
            return RedirectToAction(nameof(Login));

        return View(new AdministratorSetupModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InitialAdmin(AdministratorSetupModel model)
    {
        if (await context.Users.AnyAsync())
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        foreach (var role in SystemRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);
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
            foreach (var error in create.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
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

    // =====================================================================
    //  JSON API FOR ANDROID APP
    // =====================================================================

    public record ApiLoginRequest(string LoginId, string Password);

    public record ApiRegisterRequest(
        string Category,
        string UserNumber,
        string Name,
        string Surname,
        string Email,
        string Password,
        string ConfirmPassword);

    [AllowAnonymous, HttpPost("api/auth/login")]
    public async Task<IActionResult> ApiLogin([FromBody] ApiLoginRequest req)
    {
        var (user, error) = await ValidateLoginAsync(req.LoginId, req.Password);

        if (user is null)
            return Unauthorized(new { success = false, message = error });

        var roles = await userManager.GetRolesAsync(user);

        var role = roles.Contains(SystemRoles.Administrator) ? SystemRoles.Administrator
                 : roles.Contains(SystemRoles.FinancialOfficer) ? SystemRoles.FinancialOfficer
                 : SystemRoles.Customer;

        var token = mobileTokens.Issue(user.Id, await userManager.GetSecurityStampAsync(user));

        return Ok(new
        {
            success = true,
            role,
            accessToken = token.AccessToken,
            tokenType = "Bearer",
            expiresAtUtc = token.ExpiresAtUtc,
            name = user.FirstName,
            surname = user.LastName
        });
    }

    [AllowAnonymous, HttpPost("api/auth/register")]
    public async Task<IActionResult> ApiRegister([FromBody] ApiRegisterRequest req)
    {
        if (!await context.Roles.AnyAsync())
            return BadRequest(new { success = false, message = "Initial administrator setup must be completed first." });

        if (!Enum.TryParse<CustomerCategory>(req.Category, true, out var category))
            return BadRequest(new { success = false, message = "Please select Student or Staff." });

        var number = (req.UserNumber ?? "").Trim();
        var email = (req.Email ?? "").Trim();
        var label = category == CustomerCategory.Staff ? "Staff Number" : "Student Number";

        if (category == CustomerCategory.Staff && !System.Text.RegularExpressions.Regex.IsMatch(number, @"^0\d{6}$"))
            return BadRequest(new { success = false, message = "Staff Number is invalid" });

        if (category == CustomerCategory.Student && !System.Text.RegularExpressions.Regex.IsMatch(number, @"^\d{10}$"))
            return BadRequest(new { success = false, message = "Student Number is invalid" });

        if (category == CustomerCategory.Staff && !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[A-Za-z]+@ufs\.ac\.za$"))
            return BadRequest(new { success = false, message = "Incorrect Email" });

        if (category == CustomerCategory.Student && !System.Text.RegularExpressions.Regex.IsMatch(email, @"^\d+@ufs4life\.ac\.za$"))
            return BadRequest(new { success = false, message = "Incorrect Email" });

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Surname))
            return BadRequest(new { success = false, message = "Name and surname are required." });

        if (req.Password != req.ConfirmPassword)
            return BadRequest(new { success = false, message = "Passwords do not match." });

        if (await context.Customers.AnyAsync(c => c.UserNumber == number)
            || await userManager.FindByNameAsync(number) is not null)
            return Conflict(new { success = false, message = $"This {label} is already registered." });

        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { success = false, message = "This email address is already registered." });

        var user = new AppUser
        {
            UserName = number,
            Email = email,
            FirstName = req.Name.Trim(),
            LastName = req.Surname.Trim(),
            EmailConfirmed = false
        };

        var created = await userManager.CreateAsync(user, req.Password);
        if (!created.Succeeded)
            return BadRequest(new
            {
                success = false,
                message = string.Join(" ", created.Errors.Select(e => e.Description))
            });

        await userManager.AddToRoleAsync(user, SystemRoles.Customer);

        context.Customers.Add(new Customer
        {
            AppUserId = user.Id,
            Category = category,
            UserNumber = number
        });

        await context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            role = SystemRoles.Customer,
            userId = user.Id,
            name = user.FirstName,
            surname = user.LastName
        });
    }
}
