using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class VehicleController(
    AppDbContext context,
    UserManager<AppUser> userManager,
    IOptions<FeeSettings> feeOptions) : Controller
{
    private readonly FeeSettings _fees = feeOptions.Value;

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

        ViewData["FullName"] = (await userManager.GetUserAsync(User)) is { } u
            ? $"{u.FirstName} {u.LastName}" : "Customer";
        ViewBag.VehicleCount = vehicles.Count;
        ViewBag.ExtraCount = Math.Max(0, vehicles.Count - 1);
        ViewBag.FirstVehicleFee = _fees.FirstVehicleFee;
        ViewBag.SubsequentVehicleFee = _fees.SubsequentVehicleFee;
        ViewBag.NextPosition = vehicles.Count + 1;
        ViewBag.NextFee = vehicles.Count == 0 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0m);
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

        // Use a transaction to prevent two simultaneous registrations receiving the same position.
        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            var exists = await context.Vehicles.AnyAsync(x => x.RegistrationNumber == registrationNumber);
            if (exists)
            {
                TempData["Error"] = "Vehicle registration already exists.";
                return RedirectToAction(nameof(Index));
            }

            var existingCount = await context.Vehicles.CountAsync(x => x.CustomerId == customer.Id && x.IsActive);
            var position = existingCount + 1;
            var fee = position == 1 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0m);

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
            await tx.CommitAsync();

            if (position == 1)
            {
                TempData["Success"] = $"First vehicle registered ({registrationNumber}). Registration fee: R0.00.";
                return RedirectToAction(nameof(Index));
            }

            // Second and subsequent: require payment of the configured fee via parking/vehicle fee flow.
            TempData["Success"] = $"Vehicle #{position} registered ({registrationNumber}). Applicable fee: {fee:C}. Please complete payment.";
            TempData["SecondCarPermit"] = "true";
            TempData["SecondCarReg"] = registrationNumber;
            TempData["VehiclePosition"] = position.ToString();
            TempData["VehicleFee"] = fee.ToString("F2");
            return RedirectToAction("Checkout", "Payment", new { type = PaymentType.Parking });
        }
        catch
        {
            await tx.RollbackAsync();
            TempData["Error"] = "Could not register vehicle. Please try again.";
            return RedirectToAction(nameof(Index));
        }
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
