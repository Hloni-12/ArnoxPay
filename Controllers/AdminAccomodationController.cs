using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator)]
public class AdminAccomodationController(AppDbContext context, IFileStorageService fileStorage, INotificationService notifications) : Controller
{
    public async Task<IActionResult> List() => View(await context.Accomodations.Include(x => x.Images).Include(x => x.Residents).OrderBy(x => x.Name).AsNoTracking().ToListAsync());
    [HttpGet]
    public async Task<IActionResult> AddUpdate(int id = 0)
    {
        if (id == 0) return View(new AccomodationEditModel());
        var item = await context.Accomodations.FindAsync(id);
        if (item is null) return NotFound();
        return View(new AccomodationEditModel { Id = item.Id, Name = item.Name, UnitNumber = item.UnitNumber, Address = item.Address, AccommodationType = item.AccommodationType, Capacity = item.Capacity, Description = item.Description, IsActive = item.IsActive, RoomType = item.RoomType, MonthlyRent = item.MonthlyRent });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUpdate(AccomodationEditModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (model.Id == 0) context.Accomodations.Add(new Accomodation { Name = model.Name, UnitNumber = model.UnitNumber, Address = model.Address, AccommodationType = model.AccommodationType, Capacity = model.Capacity, Description = model.Description, IsActive = model.IsActive, RoomType = model.RoomType, MonthlyRent = model.MonthlyRent });
        else {
            var item = await context.Accomodations.FindAsync(model.Id);
            if (item is null) return NotFound();
            item.Name = model.Name;
            item.UnitNumber = model.UnitNumber;
            item.Address = model.Address;
            item.AccommodationType = model.AccommodationType;
            item.Capacity = model.Capacity;
            item.Description = model.Description;
            item.RoomType = model.RoomType;
            item.MonthlyRent = model.MonthlyRent;
            item.IsActive = model.IsActive;
        }
        await context.SaveChangesAsync();
        TempData["Success"] = "Accomodation saved.";
        return RedirectToAction(nameof(List));
    }
    public async Task<IActionResult> Images(int id) {
        var item = await context.Accomodations.Include(x => x.Images).FirstOrDefaultAsync(x => x.Id == id);
        return item is null ? NotFound() : View(item);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImage(AccomodationImageUploadModel model)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Images), new { id = model.AccomodationId });
        var item = await context.Accomodations.Include(x => x.Images).FirstOrDefaultAsync(x => x.Id == model.AccomodationId);
        if (item is null) return NotFound();
        try {
            var path = await fileStorage.SaveAccommodationImageAsync(model.ImageFile!);
            if (model.IsPrimary) foreach (var image in item.Images) image.IsPrimary = false;
            item.Images.Add(new AccomodationImage { ImagePath = path, Caption = model.Caption, IsPrimary = model.IsPrimary || !item.Images.Any() });
            await context.SaveChangesAsync();
            TempData["Success"] = "Accommodation image uploaded.";
        }
        catch (InvalidOperationException ex) {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Images), new { id = model.AccomodationId });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int id)
    {
        var image = await context.AccomodationImages.FindAsync(id);
        if (image is null) return NotFound();
        var accommodationId = image.AccomodationId;
        fileStorage.DeleteFile(image.ImagePath);
        context.AccomodationImages.Remove(image);
        await context.SaveChangesAsync();
        TempData["Success"] = "Accommodation image deleted.";
        return RedirectToAction(nameof(Images), new { id = accommodationId });
    }
    public async Task<IActionResult> AssignmentRequests() => View(await context.AccommodationAssignmentRequests.Include(x => x.Customer!).ThenInclude(x => x.AppUser).Include(x => x.Accomodation).OrderByDescending(x => x.RequestedAtUtc).AsNoTracking().ToListAsync());
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewAssignment(AccommodationAssignmentReviewModel model)
    {
        var request = await context.AccommodationAssignmentRequests.Include(x => x.Customer).Include(x => x.Accomodation!).ThenInclude(x => x.Residents).FirstOrDefaultAsync(x => x.Id == model.Id);
        if (request?.Customer is null || request.Accomodation is null) return NotFound();
        if (request.Status != AccommodationAssignmentStatus.Pending) {
            TempData["Error"] = "Request has already been reviewed.";
            return RedirectToAction(nameof(AssignmentRequests));
        }
        if (model.Status == AccommodationAssignmentStatus.Approved && request.Accomodation.Residents.Count >= request.Accomodation.Capacity) {
            TempData["Error"] = "This accomodation has reached its capacity.";
            return RedirectToAction(nameof(AssignmentRequests));
        }
        if (model.Status == AccommodationAssignmentStatus.Approved)
        {
            request.AdministratorNote = "Your residence application has been approved.";
        }
        else if (model.Status is AccommodationAssignmentStatus.Rejected or AccommodationAssignmentStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(model.AdministratorNote))
            {
                TempData["Error"] = "A rejection reason is required.";
                return RedirectToAction(nameof(AssignmentRequests));
            }
            request.AdministratorNote = model.AdministratorNote.Trim();
        }
        else
        {
            TempData["Error"] = "Unsupported decision.";
            return RedirectToAction(nameof(AssignmentRequests));
        }
        request.Status = model.Status;
        request.ReviewedAtUtc = DateTime.UtcNow;
        if (model.Status == AccommodationAssignmentStatus.Approved) request.Customer.AccomodationId = request.AccomodationId;
        await context.SaveChangesAsync();
        var customerMessage = model.Status == AccommodationAssignmentStatus.Approved
            ? "Your residence application has been approved."
            : request.AdministratorNote!;
        await notifications.CreateAsync(
            request.CustomerId,
            model.Status == AccommodationAssignmentStatus.Approved
                ? "Residence application approved"
                : "Residence application rejected",
            customerMessage);
        TempData["Success"] = "Accommodation request reviewed.";
        return RedirectToAction(nameof(AssignmentRequests));
    }
    public async Task<IActionResult> Issues() => View(await context.AccommodationIssues.Include(x => x.Accomodation).Include(x => x.Customer!).ThenInclude(x => x.AppUser).OrderBy(x => x.Status).ThenByDescending(x => x.ReportedAtUtc).AsNoTracking().ToListAsync());
    [HttpGet]
    public async Task<IActionResult> UpdateIssue(int id) {
        var issue = await context.AccommodationIssues.FindAsync(id);
        if (issue is null) return NotFound();
        return View(new AccommodationIssueUpdateModel { Id = issue.Id, Status = issue.Status, ResolutionNote = issue.ResolutionNote });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateIssue(AccommodationIssueUpdateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var issue = await context.AccommodationIssues.FindAsync(model.Id);
        if (issue is null) return NotFound();
        issue.Status = model.Status;
        issue.ResolutionNote = model.ResolutionNote;
        issue.ResolvedAtUtc = model.Status is AccommodationIssueStatus.Resolved or AccommodationIssueStatus.Closed ? DateTime.UtcNow : null;
        await context.SaveChangesAsync();
        await notifications.CreateAsync(issue.CustomerId, "Accommodation issue updated", $"Your issue '{issue.Title}' is now marked {issue.Status}.");
        TempData["Success"] = "Issue status updated.";
        return RedirectToAction(nameof(Issues));
    }
}
