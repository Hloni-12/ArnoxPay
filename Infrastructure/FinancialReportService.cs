using ArnoxPay.Data;
using ArnoxPay.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Infrastructure;

public interface IFinancialReportService
{
    Task<byte[]> GenerateExcelReportAsync(FinancialReportFilter filter);
    Task<FinancialReportSummary> GetSummaryAsync(FinancialReportFilter filter);
}

public class FinancialReportFilter
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public PaymentStatus? Status { get; set; }
    public PaymentType? PaymentType { get; set; }
    public int? CustomerId { get; set; }
    public int? AccomodationId { get; set; }
    public string? Search { get; set; }
    public string? TransactionReference { get; set; }
}

public class FinancialReportSummary
{
    public decimal TotalAccepted { get; set; }
    public decimal TotalPending { get; set; }
    public decimal TotalRejectedOrFailed { get; set; }
    public int AcceptedCount { get; set; }
    public int PendingCount { get; set; }
    public int RejectedOrFailedCount { get; set; }
    public int UniqueCustomersPaid { get; set; }
    public Dictionary<string, decimal> RevenueByService { get; set; } = new();
    public Dictionary<string, int> CountByService { get; set; } = new();
    public decimal VehicleFeesTotal { get; set; }
    public decimal ElectricityTotal { get; set; }
    public decimal WaterTotal { get; set; }
    public decimal RentTotal { get; set; }
    public decimal OtherTotal { get; set; }
}

public sealed class FinancialReportService(AppDbContext context) : IFinancialReportService
{
    private IQueryable<Payment> BaseQuery(FinancialReportFilter filter)
    {
        var q = context.Payments
            .Include(p => p.Customer!).ThenInclude(c => c.AppUser)
            .Include(p => p.Customer!).ThenInclude(c => c.Accomodation)
            .Include(p => p.FinancialOfficer!).ThenInclude(f => f.AppUser)
            .Include(p => p.Items).ThenInclude(i => i.Accomodation)
            .Include(p => p.Items).ThenInclude(i => i.Vehicle)
            .AsNoTracking()
            .AsQueryable();

        if (filter.FromUtc.HasValue)
            q = q.Where(p => p.SubmittedAtUtc >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            q = q.Where(p => p.SubmittedAtUtc <= filter.ToUtc.Value);
        if (filter.Status.HasValue)
            q = q.Where(p => p.Status == filter.Status.Value);
        if (filter.PaymentType.HasValue)
            q = q.Where(p => p.PaymentType == filter.PaymentType.Value || p.Items.Any(i => i.PaymentType == filter.PaymentType.Value));
        if (filter.CustomerId.HasValue)
            q = q.Where(p => p.CustomerId == filter.CustomerId.Value);
        if (filter.AccomodationId.HasValue)
            q = q.Where(p => p.Customer!.AccomodationId == filter.AccomodationId.Value
                || p.Items.Any(i => i.AccomodationId == filter.AccomodationId.Value));
        if (!string.IsNullOrWhiteSpace(filter.TransactionReference))
            q = q.Where(p => p.TransactionReference != null && p.TransactionReference.Contains(filter.TransactionReference));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            q = q.Where(p =>
                (p.TransactionReference != null && p.TransactionReference.ToLower().Contains(s)) ||
                (p.AccountReference != null && p.AccountReference.ToLower().Contains(s)) ||
                (p.Customer!.AppUser!.FirstName + " " + p.Customer.AppUser.LastName).ToLower().Contains(s) ||
                (p.Customer.UserNumber != null && p.Customer.UserNumber.ToLower().Contains(s)));
        }

        return q.OrderByDescending(p => p.SubmittedAtUtc);
    }

    public async Task<FinancialReportSummary> GetSummaryAsync(FinancialReportFilter filter)
    {
        var payments = await BaseQuery(filter).ToListAsync();
        var summary = new FinancialReportSummary();

        var accepted = payments.Where(p => p.Status == PaymentStatus.Accepted).ToList();
        var pending = payments.Where(p => p.Status is PaymentStatus.Pending or PaymentStatus.AwaitingAuthentication or PaymentStatus.Processing).ToList();
        var rejected = payments.Where(p => p.Status is PaymentStatus.Rejected or PaymentStatus.Failed or PaymentStatus.Cancelled).ToList();

        summary.TotalAccepted = accepted.Sum(p => p.Amount);
        summary.AcceptedCount = accepted.Count;
        summary.TotalPending = pending.Sum(p => p.Amount);
        summary.PendingCount = pending.Count;
        summary.TotalRejectedOrFailed = rejected.Sum(p => p.Amount);
        summary.RejectedOrFailedCount = rejected.Count;
        summary.UniqueCustomersPaid = accepted.Select(p => p.CustomerId).Distinct().Count();

        foreach (var p in accepted)
        {
            foreach (var item in p.Items)
            {
                var key = item.PaymentType.ToString();
                if (!summary.RevenueByService.ContainsKey(key))
                {
                    summary.RevenueByService[key] = 0;
                    summary.CountByService[key] = 0;
                }
                summary.RevenueByService[key] += item.Amount;
                summary.CountByService[key]++;

                switch (item.PaymentType)
                {
                    case PaymentType.Electricity:
                        summary.ElectricityTotal += item.Amount;
                        break;
                    case PaymentType.Water:
                        summary.WaterTotal += item.Amount;
                        break;
                    case PaymentType.Rent:
                        summary.RentTotal += item.Amount;
                        break;
                    case PaymentType.Parking:
                        summary.VehicleFeesTotal += item.Amount;
                        break;
                    default:
                        summary.OtherTotal += item.Amount;
                        break;
                }
            }
            if (p.Items.Count == 0)
            {
                var key = p.PaymentType.ToString();
                if (!summary.RevenueByService.ContainsKey(key))
                {
                    summary.RevenueByService[key] = 0;
                    summary.CountByService[key] = 0;
                }
                summary.RevenueByService[key] += p.Amount;
                summary.CountByService[key]++;
            }
        }

        return summary;
    }

    public async Task<byte[]> GenerateExcelReportAsync(FinancialReportFilter filter)
    {
        var payments = await BaseQuery(filter).ToListAsync();
        var summary = await GetSummaryAsync(filter);

        using var workbook = new XLWorkbook();
        var detail = workbook.Worksheets.Add("Transactions");
        var sumSheet = workbook.Worksheets.Add("Financial Summary");

        // Title
        detail.Cell(1, 1).Value = "ArnoxPay — Financial Payment Report";
        detail.Cell(1, 1).Style.Font.Bold = true;
        detail.Cell(1, 1).Style.Font.FontSize = 16;
        detail.Range(1, 1, 1, 10).Merge();

        var periodFrom = filter.FromUtc?.ToString("yyyy-MM-dd") ?? "All time";
        var periodTo = filter.ToUtc?.ToString("yyyy-MM-dd") ?? "Present";
        detail.Cell(2, 1).Value = $"Reporting period: {periodFrom} to {periodTo}";
        detail.Cell(3, 1).Value = $"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        detail.Cell(4, 1).Value = "Note: This is an application-generated record of payment status. It is not independent proof that funds have settled in a university bank account.";

        // Headers
        var headers = new[]
        {
            "Transaction ID", "Payment Reference", "Customer Name", "Student/Staff Number",
            "Customer Type", "Accommodation", "Unit Identifier", "Service / Fee Type",
            "Vehicle Registration", "Units Purchased", "Price Per Unit", "Amount Before Fees",
            "Additional Fees", "Total Amount", "Payment Method", "Payment Status",
            "Initiated (UTC)", "Accepted/Rejected (UTC)", "Financial Officer", "External/Provider Ref"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = detail.Cell(6, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6efd");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 7;
        foreach (var p in payments)
        {
            var items = p.Items.Count > 0 ? p.Items.ToList() : new List<PaymentItem>
            {
                new() { PaymentType = p.PaymentType, Amount = p.Amount, Description = p.Notes }
            };

            foreach (var item in items)
            {
                var customerName = p.Customer?.AppUser != null
                    ? $"{p.Customer.AppUser.FirstName} {p.Customer.AppUser.LastName}"
                    : "";
                var userNumber = p.Customer?.UserNumber ?? "";
                var category = p.Customer?.Category.ToString() ?? "";
                var accName = item.Accomodation?.Name
                    ?? p.Customer?.Accomodation?.Name ?? "";
                var unitId = item.Accomodation?.UnitNumber
                    ?? p.Customer?.Accomodation?.UnitNumber ?? "";
                var officer = p.FinancialOfficer?.AppUser != null
                    ? $"{p.FinancialOfficer.AppUser.FirstName} {p.FinancialOfficer.AppUser.LastName}"
                    : "";

                detail.Cell(row, 1).Value = p.Id;
                detail.Cell(row, 2).Value = p.TransactionReference ?? "";
                detail.Cell(row, 3).Value = customerName;
                detail.Cell(row, 4).Value = userNumber;
                detail.Cell(row, 5).Value = category;
                detail.Cell(row, 6).Value = accName;
                detail.Cell(row, 7).Value = unitId;
                detail.Cell(row, 8).Value = item.PaymentType.ToString();
                detail.Cell(row, 9).Value = item.VehicleRegistration ?? item.Vehicle?.RegistrationNumber ?? "";
                detail.Cell(row, 10).Value = item.Quantity.HasValue ? (double)item.Quantity.Value : (double?)null;
                detail.Cell(row, 11).Value = item.UnitPrice.HasValue ? (double)item.UnitPrice.Value : (double?)null;
                detail.Cell(row, 12).Value = item.Quantity.HasValue && item.UnitPrice.HasValue
                    ? (double)(item.Quantity.Value * item.UnitPrice.Value)
                    : (double)item.Amount;
                detail.Cell(row, 13).Value = 0d; // additional fees placeholder
                detail.Cell(row, 14).Value = (double)item.Amount;
                detail.Cell(row, 15).Value = p.PaymentMethod ?? "";
                detail.Cell(row, 16).Value = p.Status.ToString();
                detail.Cell(row, 17).Value = p.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss");
                detail.Cell(row, 18).Value = p.ReviewedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss")
                    ?? p.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                detail.Cell(row, 19).Value = officer;
                detail.Cell(row, 20).Value = p.OfficerReference ?? p.TransactionReference ?? "";

                // Currency format
                detail.Cell(row, 11).Style.NumberFormat.Format = "R #,##0.00";
                detail.Cell(row, 12).Style.NumberFormat.Format = "R #,##0.00";
                detail.Cell(row, 13).Style.NumberFormat.Format = "R #,##0.00";
                detail.Cell(row, 14).Style.NumberFormat.Format = "R #,##0.00";

                row++;
            }
        }

        detail.Columns().AdjustToContents();

        // Summary sheet
        sumSheet.Cell(1, 1).Value = "ArnoxPay — Financial Summary";
        sumSheet.Cell(1, 1).Style.Font.Bold = true;
        sumSheet.Cell(1, 1).Style.Font.FontSize = 16;
        sumSheet.Cell(2, 1).Value = $"Reporting period: {periodFrom} to {periodTo}";
        sumSheet.Cell(3, 1).Value = $"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";

        sumSheet.Cell(5, 1).Value = "Category";
        sumSheet.Cell(5, 2).Value = "Amount (ZAR)";
        sumSheet.Cell(5, 3).Value = "Transaction Count";
        sumSheet.Range(5, 1, 5, 3).Style.Font.Bold = true;
        sumSheet.Range(5, 1, 5, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6efd");
        sumSheet.Range(5, 1, 5, 3).Style.Font.FontColor = XLColor.White;

        int srow = 6;
        sumSheet.Cell(srow, 1).Value = "Total accepted (collected)";
        sumSheet.Cell(srow, 2).Value = (double)summary.TotalAccepted;
        sumSheet.Cell(srow, 3).Value = summary.AcceptedCount;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Total pending approval";
        sumSheet.Cell(srow, 2).Value = (double)summary.TotalPending;
        sumSheet.Cell(srow, 3).Value = summary.PendingCount;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Total rejected / failed / cancelled";
        sumSheet.Cell(srow, 2).Value = (double)summary.TotalRejectedOrFailed;
        sumSheet.Cell(srow, 3).Value = summary.RejectedOrFailedCount;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Unique customers with accepted payments";
        sumSheet.Cell(srow, 3).Value = summary.UniqueCustomersPaid;
        srow += 2;

        sumSheet.Cell(srow, 1).Value = "Revenue by service (accepted only)";
        sumSheet.Cell(srow, 1).Style.Font.Bold = true;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Electricity";
        sumSheet.Cell(srow, 2).Value = (double)summary.ElectricityTotal;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Water";
        sumSheet.Cell(srow, 2).Value = (double)summary.WaterTotal;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Rent";
        sumSheet.Cell(srow, 2).Value = (double)summary.RentTotal;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Vehicle / Parking related fees";
        sumSheet.Cell(srow, 2).Value = (double)summary.VehicleFeesTotal;
        srow++;
        sumSheet.Cell(srow, 1).Value = "Other services";
        sumSheet.Cell(srow, 2).Value = (double)summary.OtherTotal;
        srow += 2;

        foreach (var kv in summary.RevenueByService.OrderBy(x => x.Key))
        {
            sumSheet.Cell(srow, 1).Value = kv.Key;
            sumSheet.Cell(srow, 2).Value = (double)kv.Value;
            sumSheet.Cell(srow, 3).Value = summary.CountByService.GetValueOrDefault(kv.Key);
            srow++;
        }

        sumSheet.Column(2).Style.NumberFormat.Format = "R #,##0.00";
        sumSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
