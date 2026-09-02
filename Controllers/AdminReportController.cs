using ArnoxPay.Data;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator + "," + SystemRoles.FinancialOfficer)]
public class AdminReportController(AppDbContext context) : Controller
{
    public async Task<IActionResult> Index() => View(await context.Payments.Include(x => x.Customer!).ThenInclude(x => x.AppUser).Include(x => x.FinancialOfficer!).ThenInclude(x => x.AppUser).Include(x => x.Transaction).OrderByDescending(x => x.SubmittedAtUtc).AsNoTracking().ToListAsync());
}
