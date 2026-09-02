using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class AccomodationController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> List()
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();
        var pendingIds = await context.AccommodationAssignmentRequests.Where(x => x.CustomerId == customer.Id && x.Status == AccommodationAssignmentStatus.Pending).Select(x => x.AccomodationId).ToListAsync();
        var items = await context.Accomodations.Where(x => x.IsActive).Include(x => x.Images).Include(x => x.Residents).OrderBy(x => x.Name).AsNoTracking().ToListAsync();
        return View(items.Select(x => new AccommodationDetailsViewModel { Accomodation = x, OccupiedPlaces = x.Residents.Count, IsCurrentResident = customer.AccomodationId == x.Id, HasPendingRequest = pendingIds.Contains(x.Id) }).ToList());
    }
    public async Task<IActionResult> Details(int id)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();
        var accommodation = await context.Accomodations.Include(x => x.Images).Include(x => x.Residents).FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (accommodation is null) return NotFound();
        var pending = await context.AccommodationAssignmentRequests.AnyAsync(x => x.CustomerId == customer.Id && x.AccomodationId == id && x.Status == AccommodationAssignmentStatus.Pending);
        return View(new AccommodationDetailsViewModel { Accomodation = accommodation, OccupiedPlaces = accommodation.Residents.Count, IsCurrentResident = customer.AccomodationId == id, HasPendingRequest = pending });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAssignment(AccommodationAssignmentRequestCreateModel model)
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();
        if (customer.AccomodationId.HasValue) {
            TempData["Error"] = "You are already assigned to an accomodation.";
            return RedirectToAction(nameof(List));
        }
        if (await context.AccommodationAssignmentRequests.AnyAsync(x => x.CustomerId == customer.Id && x.Status == AccommodationAssignmentStatus.Pending)) {
            TempData["Error"] = "You already have a pending accomodation request.";
            return RedirectToAction(nameof(List));
        }
        var accommodation = await context.Accomodations.Include(x => x.Residents).FirstOrDefaultAsync(x => x.Id == model.AccomodationId && x.IsActive);
        if (accommodation is null) return NotFound();
        if (accommodation.Residents.Count >= accommodation.Capacity) {
            TempData["Error"] = "This accomodation is currently full.";
            return RedirectToAction(nameof(List));
        }
        context.AccommodationAssignmentRequests.Add(new AccommodationAssignmentRequest { CustomerId = customer.Id, AccomodationId = accommodation.Id, CustomerNote = model.CustomerNote });
        await context.SaveChangesAsync();
        TempData["Success"] = "Your accomodation request has been submitted for administrator review.";
        return RedirectToAction(nameof(MyRequests));
    }
    public async Task<IActionResult> MyRequests() {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();
        return View(await context.AccommodationAssignmentRequests.Where(x => x.CustomerId == customer.Id).Include(x => x.Accomodation).OrderByDescending(x => x.RequestedAtUtc).AsNoTracking().ToListAsync());
    }
    private async Task<Customer?> CurrentCustomer() {
        var user = await userManager.GetUserAsync(User);
        return user is null ? null : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
    }
}
