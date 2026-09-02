using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

/// <summary>
/// Financial officers and administrators view customer ratings and reviews.
/// Customers submit reviews via CustomerController.Reviews.
/// </summary>
[Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
public class ReviewsController(AppDbContext context) : Controller
{
    public async Task<IActionResult> Index()
    {
        List<ServiceReview> reviews = [];
        try
        {
            reviews = await context.ServiceReviews
            .Include(x => x.Customer!).ThenInclude(x => x.AppUser)
            .OrderByDescending(x => x.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync();
        } catch
        {
            // Migration not applied yet
            ViewBag.MigrationNeeded = true;
        }
        return View(reviews);
    }
}
