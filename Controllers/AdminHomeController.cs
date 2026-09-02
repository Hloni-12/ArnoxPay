using ArnoxPay.Data;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator)]
public class AdminHomeController(AppDbContext context, UserManager<AppUser> userManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        var model = new DashboardViewModel
        {
            FullName = user is null ? "Administrator" : $"{user.FirstName} {user.LastName}", Role = SystemRoles.Administrator,
            ActiveCustomers = await context.Customers.CountAsync(x => x.AppUser!.IsActive),
            ActiveFinancialOfficers = await context.FinancialOfficers.CountAsync(x => x.IsAuthorized && x.AppUser!.IsActive),
            PendingPayments = await context.Payments.CountAsync(x => x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.AwaitingAuthentication || x.Status == PaymentStatus.Processing),
            AcceptedPayments = await context.Payments.CountAsync(x => x.Status == PaymentStatus.Accepted),
            AcceptedTotal = await context.Payments.Where(x => x.Status == PaymentStatus.Accepted).SumAsync(x => (decimal?)x.Amount) ?? 0,
            RecentPayments = await context.Payments.Include(x => x.Customer!).ThenInclude(x => x.AppUser).OrderByDescending(x => x.SubmittedAtUtc).Take(8).AsNoTracking().ToListAsync()
        }
        ;
        
        try
        {
            ViewData["PendingApplications"] = await context.Set<AccommodationAssignmentRequest>().CountAsync();
        }
        catch { ViewData["PendingApplications"] = 0; }
        try
        {
            ViewData["AvailableUnits"] = await context.Accomodations.CountAsync(x => x.IsActive);
        }
        catch { ViewData["AvailableUnits"] = 0; }
        try
        {
            ViewData["OccupiedUnits"] = await context.Customers.CountAsync(x => x.AccomodationId != null);
        }
        catch { ViewData["OccupiedUnits"] = 0; }

        return View(model);
    }
}
