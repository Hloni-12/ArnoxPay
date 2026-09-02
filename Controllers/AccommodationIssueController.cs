using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Customer)]
public class AccommodationIssueController(AppDbContext context, UserManager<AppUser> userManager, INotificationService notificationService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var customer = await CurrentCustomer();
        if (customer is null) return NotFound();
        return View(await context.AccommodationIssues.Where(x => x.CustomerId == customer.Id).Include(x => x.Accomodation).OrderByDescending(x => x.ReportedAtUtc).AsNoTracking().ToListAsync());
    }
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var customer = await CurrentCustomer();
        if (customer?.AccomodationId is null)
        {
            TempData["Error"] = "You can report an issue after you have been assigned to an accomodation.";
            return RedirectToAction("List", "Accomodation");
        }
        return View(new AccommodationIssueCreateModel { AccomodationId = customer.AccomodationId.Value });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccommodationIssueCreateModel model)
    {
        var customer = await CurrentCustomer();
        if (customer?.AccomodationId is null || customer.AccomodationId != model.AccomodationId) return Forbid();
        if (!ModelState.IsValid) return View(model);
        var issue = new AccommodationIssue
        {
            AccomodationId = model.AccomodationId,
            CustomerId = customer.Id,
            Category = model.Category,
            Title = model.Title,
            Description = model.Description
        }
        ;
        context.AccommodationIssues.Add(issue);
        await context.SaveChangesAsync();
        await notificationService.CreateAsync(customer.Id, "Accommodation issue reported", "Your maintenance issue was sent to the administrator for review.");
        TempData["Success"] = "Accommodation issue reported.";
        return RedirectToAction(nameof(Index));
    }
    private async Task<Customer?> CurrentCustomer()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? null : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == user.Id);
    }
}
