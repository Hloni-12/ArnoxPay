using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class VehicleController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    private async Task<Customer?> CurrentCustomerAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return null;
        return await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customer = await CurrentCustomerAsync();
        if (customer is null) return Challenge();

        var vehicles = await context.Vehicles
        .Where(x => x.CustomerId == customer.Id && x.IsActive)
        .OrderBy(x => x.RegisteredAtUtc)
        .AsNoTracking()
        .ToListAsync();

        ViewData["FullName"] = (await userManager.GetUserAsync(User)) is {
        }
        u
        ? $"{u.FirstName} {u.LastName}" : "Customer";
        ViewBag.VehicleCount = vehicles.Count;
        ViewBag.ExtraCount = Math.Max(0, vehicles.Count - 1);
        return View(vehicles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string registrationNumber, string? make, string? model, string? colour)
    {
        var customer = await CurrentCustomerAsync();
        if (customer is null) return Challenge();

        registrationNumber = (registrationNumber ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            TempData["Error"] = "Registration number is required.";
            return RedirectToAction(nameof(Index));
        }

        var exists = await context.Vehicles.AnyAsync(x => x.RegistrationNumber == registrationNumber);
        if (exists)
        {
            TempData["Error"] = "Vehicle registration already exists.";
            return RedirectToAction(nameof(Index));
        }

        var existingCount = await context.Vehicles.CountAsync(x => x.CustomerId == customer.Id && x.IsActive);

        context.Vehicles.Add(new Vehicle
        {
            CustomerId = customer.Id,
            RegistrationNumber = registrationNumber,
            Make = make?.Trim(),
            Model = model?.Trim(),
            Colour = colour?.Trim(),
            RegisteredAtUtc = DateTime.UtcNow,
            IsActive = true
        });
        await context.SaveChangesAsync();

        // First vehicle: existing parking rules (included). Second vehicle: requires second-car parking permit payment.
        if (existingCount >= 1)
        {
            TempData["Success"] = "Second vehicle registered (" + registrationNumber + "). Please complete payment for the second-car parking permit.";
            TempData["SecondCarPermit"] = "true";
            TempData["SecondCarReg"] = registrationNumber;
            return RedirectToAction("Checkout", "Payment", new { type = PaymentType.Parking });
        }

        TempData["Success"] = "Vehicle registered successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id)
    {
        var customer = await CurrentCustomerAsync();
        if (customer is null) return Challenge();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customer.Id);
        if (vehicle is null) return NotFound();

        var hasActivePermit = await context.ParkingPermits.AnyAsync(x =>
        x.VehicleId == vehicle.Id &&
        (x.Status == ParkingPermitStatus.Active || x.Status == ParkingPermitStatus.PendingApproval));
        if (hasActivePermit)
        {
            TempData["Error"] = "Cannot remove a vehicle with an active or pending permit.";
            return RedirectToAction(nameof(Index));
        }

        vehicle.IsActive = false;
        await context.SaveChangesAsync();
        TempData["Success"] = "Vehicle removed.";
        return RedirectToAction(nameof(Index));
    }
}
