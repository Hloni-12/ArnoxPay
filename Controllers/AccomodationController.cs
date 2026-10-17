using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

public class AccomodationController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    private async Task<Customer?> CurrentCustomer()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? null : await context.Customers.Include(c => c.Accomodation).FirstOrDefaultAsync(c => c.AppUserId == user.Id);
    }

    [AllowAnonymous]
    public async Task<IActionResult> List(string? search, AccommodationType? type, string? roomType, bool availableOnly = false)
    {
        var q = context.Accomodations.Include(a => a.Images).Include(a => a.Residents)
            .Where(a => a.IsActive).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(a => a.Name.Contains(term) || a.Address.Contains(term) || a.UnitNumber.Contains(term));
        }
        if (type.HasValue) q = q.Where(a => a.AccommodationType == type.Value);
        if (!string.IsNullOrWhiteSpace(roomType) && Enum.TryParse<AccommodationRoomType>(roomType, true, out var parsed)) q = q.Where(a => a.RoomType == parsed);
        var accommodations = await q.OrderBy(a => a.Name).ToListAsync();
        var customer = User.Identity?.IsAuthenticated == true && User.IsInRole(SystemRoles.Customer) ? await CurrentCustomer() : null;
        var pendingIds = customer is null ? new List<int>() : await context.AccommodationAssignmentRequests.Where(x => x.CustomerId == customer.Id && x.Status == AccommodationAssignmentStatus.Pending).Select(x => x.AccomodationId).ToListAsync();
        var models = accommodations.Select(a => new AccommodationDetailsViewModel { Accomodation=a, OccupiedPlaces=a.Residents.Count, IsCurrentResident=customer?.AccomodationId==a.Id, HasPendingRequest=pendingIds.Contains(a.Id) }).Where(x => !availableOnly || x.OccupiedPlaces < x.Accomodation.Capacity).ToList();
        ViewBag.Search=search; ViewBag.Type=type; ViewBag.RoomType=roomType; ViewBag.AvailableOnly=availableOnly;
        return View(models);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var a = await context.Accomodations.Include(x=>x.Images).Include(x=>x.Residents).AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id&&x.IsActive);
        if(a is null) return NotFound();
        var customer = User.Identity?.IsAuthenticated == true && User.IsInRole(SystemRoles.Customer) ? await CurrentCustomer() : null;
        var pending = customer is not null && await context.AccommodationAssignmentRequests.AnyAsync(x=>x.CustomerId==customer.Id && x.Status==AccommodationAssignmentStatus.Pending);
        return View(new AccommodationDetailsViewModel { Accomodation=a, OccupiedPlaces=a.Residents.Count, IsCurrentResident=customer?.AccomodationId==a.Id, HasPendingRequest=pending });
    }

    [Authorize(Roles=SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestAssignment(AccommodationAssignmentRequestCreateModel model)
    {
        var customer=await CurrentCustomer(); if(customer is null) return NotFound();
        if(customer.AccomodationId.HasValue || await context.AccommodationAssignmentRequests.AnyAsync(x=>x.CustomerId==customer.Id&&x.Status==AccommodationAssignmentStatus.Pending)) { TempData["Error"]="You already have a residence or pending request."; return RedirectToAction(nameof(List)); }
        var a=await context.Accomodations.Include(x=>x.Residents).FirstOrDefaultAsync(x=>x.Id==model.AccomodationId&&x.IsActive);
        if(a is null || a.Residents.Count>=a.Capacity) { TempData["Error"]="This accommodation is not available."; return RedirectToAction(nameof(List)); }
        context.AccommodationAssignmentRequests.Add(new AccommodationAssignmentRequest { CustomerId=customer.Id, AccomodationId=a.Id, CustomerNote=string.IsNullOrWhiteSpace(model.CustomerNote)?null:model.CustomerNote.Trim(), Status=AccommodationAssignmentStatus.Pending, RequestedAtUtc=DateTime.UtcNow });
        await context.SaveChangesAsync(); TempData["Success"]="Your accommodation request was submitted."; return RedirectToAction(nameof(MyRequests));
    }

    [Authorize(Roles=SystemRoles.Customer)]
    public async Task<IActionResult> MyRequests()
    {
        var c=await CurrentCustomer(); if(c is null)return NotFound();
        return View(await context.AccommodationAssignmentRequests.Where(x=>x.CustomerId==c.Id).Include(x=>x.Accomodation).OrderByDescending(x=>x.RequestedAtUtc).AsNoTracking().ToListAsync());
    }
}
