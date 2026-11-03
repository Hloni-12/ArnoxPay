using System.Data;
using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
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
    IOptions<FeeSettings> feeOptions,
    ILogger<VehicleController> logger) : Controller
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
    public async Task<IActionResult> Create(VehicleRegistrationInput input)
    {
        var customer = await CurrentCustomerAsync();
        if (customer is null) return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the vehicle details: registration numbers are limited to 20 characters, make and model to 60, and colour to 40.";
            return RedirectToAction(nameof(Index));
        }

        var registrationNumber = input.RegistrationNumber.Trim().ToUpperInvariant();

        // Serializable isolation makes the per-customer vehicle position reliable when two requests arrive together.
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var existingVehicle = await context.Vehicles
                .SingleOrDefaultAsync(x => x.RegistrationNumber == registrationNumber);

            if (existingVehicle is { IsActive: true })
            {
                TempData["Error"] = "Vehicle registration already exists.";
                return RedirectToAction(nameof(Index));
            }

            if (existingVehicle is not null && existingVehicle.CustomerId != customer.Id)
            {
                TempData["Error"] = "This vehicle is registered to another customer. Contact administration if ownership has changed.";
                return RedirectToAction(nameof(Index));
            }

            var existingCount = await context.Vehicles.CountAsync(x => x.CustomerId == customer.Id && x.IsActive);
            var position = existingCount + 1;
            var fee = position == 1 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0m);
            var reactivated = existingVehicle is not null;

            var vehicle = existingVehicle ?? new Vehicle
            {
                CustomerId = customer.Id,
                RegistrationNumber = registrationNumber
            };

            vehicle.Make = input.Make?.Trim();
            vehicle.Model = input.Model?.Trim();
            vehicle.Colour = input.Colour?.Trim();
            vehicle.RegisteredAtUtc = DateTime.UtcNow;
            vehicle.IsActive = true;

            if (!reactivated)
                context.Vehicles.Add(vehicle);

            await context.SaveChangesAsync();
            await tx.CommitAsync();

            if (position == 1)
            {
                TempData["Success"] = reactivated
                    ? $"Vehicle reactivated ({registrationNumber}). Registration fee: R0.00."
                    : $"First vehicle registered ({registrationNumber}). Registration fee: R0.00.";
                return RedirectToAction(nameof(Index));
            }

            // Second and subsequent: require payment of the configured fee via parking/vehicle fee flow.
            TempData["Success"] = $"Vehicle #{position} {(reactivated ? "reactivated" : "registered")} ({registrationNumber}). Applicable fee: {fee:C}. Please complete payment.";
            return RedirectToAction("Checkout", "Payment", new { type = PaymentType.Parking, vehicleId = vehicle.Id });
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Vehicle registration could not be saved for customer {CustomerId} and registration {RegistrationNumber}.", customer.Id, registrationNumber);
            TempData["Error"] = "The vehicle could not be saved. It may have just been registered already; refresh the list and try again.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected vehicle registration failure for customer {CustomerId} and registration {RegistrationNumber}.", customer.Id, registrationNumber);
            TempData["Error"] = "Vehicle registration is temporarily unavailable. Please try again shortly.";
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
