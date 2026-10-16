using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class CustomerController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers
            .Include(x => x.Accomodation)
            .FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();

        var query = context.Payments.Where(x => x.CustomerId == customer.Id);

        var acceptedTotal = await query.Where(x => x.Status == PaymentStatus.Accepted).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var pendingCount = await query.CountAsync(x => x.Status == PaymentStatus.Pending);

        ViewData["AccommodationName"] = customer.Accomodation?.Name;
        ViewData["AccommodationUnit"] = customer.Accomodation?.UnitNumber;

        var firstName = (user.FirstName ?? string.Empty).Trim();
        var lastName = (user.LastName ?? string.Empty).Trim();
        var fullName = $"{firstName} {lastName}".Trim();

        return View(new DashboardViewModel
        {
            FullName = fullName,
            FirstName = firstName,
            Role = SystemRoles.Customer,
            PendingPayments = pendingCount,
            AcceptedPayments = await query.CountAsync(x => x.Status == PaymentStatus.Accepted),
            AcceptedTotal = acceptedTotal,
            RecentPayments = await query.OrderByDescending(x => x.SubmittedAtUtc).Take(8).AsNoTracking().ToListAsync()
        });
    }

    /// <summary>Redirect to Vehicle controller for real DB vehicle management.</summary>
    public IActionResult Vehicles() => RedirectToAction("Index", "Vehicle");

    /// <summary>Parking map removed — use Vehicles and payment for parking fees.</summary>
    public IActionResult Parking() => RedirectToAction("Index", "Vehicle");

    public async Task<IActionResult> Permit()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();

        ViewData["FullName"] = $"{user.FirstName} {user.LastName}";
        ViewData["Email"] = user.Email;

        var permits = await context.ParkingPermits
        .Include(x => x.Vehicle)
        .Where(x => x.CustomerId == customer.Id)
        .OrderByDescending(x => x.AppliedAtUtc)
        .AsNoTracking()
        .ToListAsync();

        return View(permits);
    }

    [HttpGet]
    public async Task<IActionResult> ApplyPermit()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();

        var vehicles = await context.Vehicles
        .Where(x => x.CustomerId == customer.Id && x.IsActive)
        .AsNoTracking()
        .ToListAsync();
        ViewBag.Vehicles = vehicles;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyPermit(int vehicleId, string permitType, decimal fee)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(x => x.Id == vehicleId && x.CustomerId == customer.Id && x.IsActive);
        if (vehicle is null)
        {
            TempData["Error"] = "Select a valid registered vehicle.";
            return RedirectToAction(nameof(ApplyPermit));
        }

        var pendingOrActive = await context.ParkingPermits.AnyAsync(x =>
        x.VehicleId == vehicleId &&
        (x.Status == ParkingPermitStatus.Active || x.Status == ParkingPermitStatus.PendingApproval));
        if (pendingOrActive)
        {
            TempData["Error"] = "This vehicle already has an active or pending permit.";
            return RedirectToAction(nameof(Permit));
        }

        // Parking permit applications are free of charge. Do not create a payment transaction.
        fee = 0m;
        var number = $"AP-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(10000, 99999)}";
        context.ParkingPermits.Add(new ParkingPermit
        {
            PermitNumber = number,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            PermitType = string.IsNullOrWhiteSpace(permitType) ? "Student annual" : permitType.Trim(),
            IssueDate = DateTime.UtcNow.Date,
            ExpiryDate = DateTime.UtcNow.Date.AddYears(1),
            Status = ParkingPermitStatus.PendingApproval,
            Fee = 0m,
            AppliedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        TempData["Success"] = "Permit application submitted free of charge (R0.00). Status: Pending approval. An authorised officer will review it.";
        return RedirectToAction(nameof(Permit));
    }

    [HttpGet]
    public async Task<IActionResult> Reviews()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();
        ViewData["FullName"] = $"{user.FirstName} {user.LastName}";
        List<ServiceReview> myReviews = [];
        try
        {
            myReviews = await context.ServiceReviews
            .Where(x => x.CustomerId == customer.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync();
        }
        catch {
        }
        return View(myReviews);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reviews(int rating, string category, string comment)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var customer = await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
        if (customer is null) return NotFound();
        if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(comment))
        {
            TempData["Error"] = "Please provide a rating (1–5) and a short comment.";
            return RedirectToAction(nameof(Reviews));
        }
        try
        {
            context.ServiceReviews.Add(new ServiceReview
            {
                CustomerId = customer.Id,
                Rating = rating,
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
                Comment = comment.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            }
            );
            await context.SaveChangesAsync();
            TempData["Success"] = "Thank you — your rating and review were submitted.";
        } catch
        {
            TempData["Error"] = "Could not save review. Ensure the ServiceReviews table exists (apply EF migrations to ArnoxPayDB on SQL Server).";
        }
        return RedirectToAction(nameof(Reviews));
    }
}
