using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

/// <summary>
/// Customer accommodation browse and residence requests — Azure SQL / EF Core only (no Firebase).
/// </summary>
[Authorize(Roles = SystemRoles.Customer)]
public class AccomodationController(
    AppDbContext context,
    UserManager<AppUser> userManager) : Controller
{
    private async Task<Customer?> CurrentCustomer()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return null;
        return await context.Customers
            .Include(c => c.Accomodation)
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id);
    }

    public async Task<IActionResult> List()
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var pendingIds = await context.AccommodationAssignmentRequests
            .Where(x => x.CustomerId == customer.Id && x.Status == AccommodationAssignmentStatus.Pending)
            .Select(x => x.AccomodationId)
            .ToListAsync();

        var accommodations = await context.Accomodations
            .Include(x => x.Images)
            .Include(x => x.Residents)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync();

        var viewModels = accommodations.Select(a => new AccommodationDetailsViewModel
        {
            Accomodation = a,
            OccupiedPlaces = a.Residents?.Count ?? 0,
            IsCurrentResident = customer.AccomodationId == a.Id,
            HasPendingRequest = pendingIds.Contains(a.Id)
        }).ToList();

        ViewBag.PendingAccommodationIds = pendingIds;
        return View(viewModels);
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var accommodation = await context.Accomodations
            .Include(x => x.Images)
            .Include(x => x.Residents)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);

        if (accommodation is null) return NotFound();

        var hasPendingForThis = await context.AccommodationAssignmentRequests.AnyAsync(x =>
            x.CustomerId == customer.Id &&
            x.AccomodationId == id &&
            x.Status == AccommodationAssignmentStatus.Pending);

        var hasAnyPending = await context.AccommodationAssignmentRequests.AnyAsync(x =>
            x.CustomerId == customer.Id &&
            x.Status == AccommodationAssignmentStatus.Pending);

        return View(new AccommodationDetailsViewModel
        {
            Accomodation = accommodation,
            OccupiedPlaces = accommodation.Residents?.Count ?? 0,
            IsCurrentResident = customer.AccomodationId == accommodation.Id,
            HasPendingRequest = hasPendingForThis || hasAnyPending
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAssignment(AccommodationAssignmentRequestCreateModel model)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        if (customer.AccomodationId.HasValue)
        {
            TempData["Error"] = "You are already assigned to an accommodation.";
            return RedirectToAction(nameof(List));
        }

        if (await context.AccommodationAssignmentRequests.AnyAsync(x =>
            x.CustomerId == customer.Id &&
            x.Status == AccommodationAssignmentStatus.Pending))
        {
            TempData["Error"] = "You already have a pending accommodation request.";
            return RedirectToAction(nameof(List));
        }

        var accommodation = await context.Accomodations
            .Include(x => x.Residents)
            .FirstOrDefaultAsync(x => x.Id == model.AccomodationId && x.IsActive);

        if (accommodation is null)
        {
            TempData["Error"] = "Selected accommodation was not found or is inactive.";
            return RedirectToAction(nameof(List));
        }

        if (accommodation.Residents.Count >= accommodation.Capacity)
        {
            TempData["Error"] = "This accommodation has reached its capacity.";
            return RedirectToAction(nameof(Details), new { id = model.AccomodationId });
        }

        context.AccommodationAssignmentRequests.Add(new AccommodationAssignmentRequest
        {
            CustomerId = customer.Id,
            AccomodationId = accommodation.Id,
            CustomerNote = string.IsNullOrWhiteSpace(model.CustomerNote) ? null : model.CustomerNote.Trim(),
            Status = AccommodationAssignmentStatus.Pending,
            RequestedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        TempData["Success"] = $"Residence request for {accommodation.Name} (Unit {accommodation.UnitNumber}) submitted. An administrator will review it.";
        return RedirectToAction(nameof(MyRequests));
    }

    public async Task<IActionResult> MyRequests()
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();

        var requests = await context.AccommodationAssignmentRequests
            .Where(x => x.CustomerId == customer.Id)
            .Include(x => x.Accomodation)
            .OrderByDescending(x => x.RequestedAtUtc)
            .AsNoTracking()
            .ToListAsync();

        return View(requests);
    }
}
