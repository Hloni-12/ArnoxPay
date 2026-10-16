using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator + "," + SystemRoles.FinancialOfficer)]
public class AdminReportController(
    AppDbContext context,
    IFinancialReportService reportService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        DateTime? from,
        DateTime? to,
        PaymentStatus? status,
        PaymentType? paymentType,
        int? customerId,
        int? accommodationId,
        string? search,
        string? transactionReference)
    {
        var filter = new FinancialReportFilter
        {
            FromUtc = from?.ToUniversalTime(),
            ToUtc = to.HasValue ? to.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime() : null,
            Status = status,
            PaymentType = paymentType,
            CustomerId = customerId,
            AccomodationId = accommodationId,
            Search = search,
            TransactionReference = transactionReference
        };

        var summary = await reportService.GetSummaryAsync(filter);

        var payments = await context.Payments
            .Include(p => p.Customer!).ThenInclude(c => c.AppUser)
            .Include(p => p.Customer!).ThenInclude(c => c.Accomodation)
            .Include(p => p.FinancialOfficer!).ThenInclude(f => f.AppUser)
            .Include(p => p.Items)
            .AsNoTracking()
            .Where(p =>
                (!filter.FromUtc.HasValue || p.SubmittedAtUtc >= filter.FromUtc) &&
                (!filter.ToUtc.HasValue || p.SubmittedAtUtc <= filter.ToUtc) &&
                (!filter.Status.HasValue || p.Status == filter.Status) &&
                (!filter.PaymentType.HasValue || p.PaymentType == filter.PaymentType || p.Items.Any(i => i.PaymentType == filter.PaymentType)) &&
                (!filter.CustomerId.HasValue || p.CustomerId == filter.CustomerId) &&
                (string.IsNullOrWhiteSpace(filter.Search) ||
                    (p.TransactionReference != null && p.TransactionReference.Contains(filter.Search)) ||
                    (p.Customer!.UserNumber != null && p.Customer.UserNumber.Contains(filter.Search)) ||
                    ((p.Customer.AppUser!.FirstName + " " + p.Customer.AppUser.LastName).Contains(filter.Search))) &&
                (string.IsNullOrWhiteSpace(filter.TransactionReference) ||
                    (p.TransactionReference != null && p.TransactionReference.Contains(filter.TransactionReference)))
            )
            .OrderByDescending(p => p.SubmittedAtUtc)
            .Take(500)
            .ToListAsync();

        ViewBag.Summary = summary;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Status = status;
        ViewBag.PaymentType = paymentType;
        ViewBag.Search = search;
        ViewBag.TransactionReference = transactionReference;
        ViewBag.StatusList = new SelectList(Enum.GetValues<PaymentStatus>());
        ViewBag.PaymentTypeList = new SelectList(Enum.GetValues<PaymentType>());
        ViewBag.Customers = new SelectList(
            await context.Customers.Include(c => c.AppUser).OrderBy(c => c.UserNumber).AsNoTracking().ToListAsync(),
            "Id",
            "UserNumber");
        ViewBag.Accommodations = new SelectList(
            await context.Accomodations.OrderBy(a => a.Name).AsNoTracking().ToListAsync(),
            "Id",
            "Name");

        return View(payments);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadExcel(
        DateTime? from,
        DateTime? to,
        PaymentStatus? status,
        PaymentType? paymentType,
        int? customerId,
        int? accommodationId,
        string? search,
        string? transactionReference)
    {
        var filter = new FinancialReportFilter
        {
            FromUtc = from?.ToUniversalTime(),
            ToUtc = to.HasValue ? to.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime() : null,
            Status = status,
            PaymentType = paymentType,
            CustomerId = customerId,
            AccomodationId = accommodationId,
            Search = search,
            TransactionReference = transactionReference
        };

        var bytes = await reportService.GenerateExcelReportAsync(filter);
        var fileName = $"ArnoxPay_Financial_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
