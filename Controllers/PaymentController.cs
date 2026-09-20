using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

public class PaymentController(AppDbContext context, UserManager<AppUser> userManager, INotificationService notifications, ITransactionService transactions, ICheckoutService checkout) : Controller
{

    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
    public async Task<IActionResult> Dashboard()
    {
        var user = await userManager.GetUserAsync(User);
        var model = new DashboardViewModel
        {
            FullName = user is null ? "Financial Officer" : $"{user.FirstName} {user.LastName}",
            Role = SystemRoles.FinancialOfficer,
            ActiveCustomers = await context.Customers.CountAsync(x => x.AppUser!.IsActive),
            PendingPayments = await context.Payments.CountAsync(x => x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.AwaitingAuthentication || x.Status == PaymentStatus.Processing),
            AcceptedPayments = await context.Payments.CountAsync(x => x.Status == PaymentStatus.Accepted),
            AcceptedTotal = await context.Payments.Where(x => x.Status == PaymentStatus.Accepted).SumAsync(x => (decimal?)x.Amount) ?? 0,
            RecentPayments = await context.Payments
                .Include(x => x.Customer!).ThenInclude(c => c.AppUser)
                .OrderByDescending(x => x.SubmittedAtUtc)
                .Take(8)
                .AsNoTracking()
                .ToListAsync()
        };
        return View(model);
    }

    [Authorize(Roles = SystemRoles.Customer)]
    public async Task<IActionResult> MyPayments()
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        return View(await context.Payments.Where(x => x.CustomerId == c.Id).OrderByDescending(x => x.SubmittedAtUtc).AsNoTracking().ToListAsync());
    }

    // ========== Customer checkout lifecycle ==========

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public IActionResult AddUpdate(PaymentType? type = null) => RedirectToAction(nameof(Checkout), new { type });

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public IActionResult Checkout(PaymentType? type = null)
    {
        var model = new PaymentCreateModel();
        if (type.HasValue)
            model.PaymentType = type.Value;
        return View(model);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(PaymentCreateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var c = await CurrentCustomer();
        if (c is null) return NotFound();

        var payment = await checkout.CreateCheckoutAsync(
            c.Id, model.PaymentType, model.Amount, model.AccountReference, model.Notes);

        await notifications.CreateAsync(
            c.Id,
            "Payment created",
            $"Checkout started for {payment.PaymentType} ({payment.TransactionReference}). Authentication is still required.");

        return RedirectToAction(nameof(Details), new { id = payment.Id });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        if (payment.Status is PaymentStatus.Accepted or PaymentStatus.Failed or PaymentStatus.Rejected)
            return RedirectToAction(nameof(Result), new { id });
        ViewBag.Gateway = HttpContext.RequestServices.GetRequiredService<IPaymentGateway>();
        return View(payment);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(int id, string paymentMethod)
    {
        var payment = await LoadOwnPayment(id, track: true);
        if (payment is null) return NotFound();
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            ModelState.AddModelError(string.Empty, "Select a payment method.");
            ViewBag.Gateway = HttpContext.RequestServices.GetRequiredService<IPaymentGateway>();
            return View(payment);
        }
        await checkout.SetPaymentMethodAsync(payment, paymentMethod.Trim());
        return RedirectToAction(nameof(Confirm), new { id });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> Confirm(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        if (string.IsNullOrWhiteSpace(payment.PaymentMethod))
            return RedirectToAction(nameof(Details), new { id });
        ViewBag.Gateway = HttpContext.RequestServices.GetRequiredService<IPaymentGateway>();
        return View(payment);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> Authenticate(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        if (payment.Status != PaymentStatus.AwaitingAuthentication)
            return RedirectToAction(nameof(Result), new { id });
        ViewBag.Gateway = HttpContext.RequestServices.GetRequiredService<IPaymentGateway>();
        return View(payment);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Authenticate(int id, string action)
    {
        var payment = await LoadOwnPayment(id, track: true);
        if (payment is null) return NotFound();

        var confirm = string.Equals(action, "confirm", StringComparison.OrdinalIgnoreCase);
        var result = await checkout.AuthenticateAsync(payment, confirm);
        if (!result.Success)
            return RedirectToAction(nameof(Result), new { id });

        return RedirectToAction(nameof(Processing), new { id });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> Processing(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        if (payment.Status == PaymentStatus.AwaitingAuthentication)
            return RedirectToAction(nameof(Authenticate), new { id });
        if (payment.Status is PaymentStatus.Accepted or PaymentStatus.Failed or PaymentStatus.Rejected)
            return RedirectToAction(nameof(Result), new { id });
        return View(payment);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteProcessing(int id)
    {
        var payment = await LoadOwnPayment(id, track: true);
        if (payment is null) return NotFound();
        if (payment.Status == PaymentStatus.Processing)
        {
            var result = await checkout.ProcessAsync(payment);
            if (result.Success && payment.Status == PaymentStatus.Pending)
            {
                await notifications.CreateAsync(
                    payment.CustomerId,
                    "Payment pending review",
                    $"Your {payment.PaymentType} payment ({payment.TransactionReference}) was received and is awaiting Financial Officer review. It is not marked as paid yet.");
            }
            else if (!result.Success)
            {
                await notifications.CreateAsync(
                    payment.CustomerId,
                    "Payment failed",
                    $"Your {payment.PaymentType} payment could not be completed. {result.Message}");
            }
        }
        return RedirectToAction(nameof(Result), new { id });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> Result(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        return View(payment);
    }

    [Authorize(Roles = SystemRoles.Customer)]
    public async Task<IActionResult> Submitted(int id) => RedirectToAction(nameof(Result), new { id });

    private async Task<Payment?> LoadOwnPayment(int id, bool track = false)
    {
        var c = await CurrentCustomer();
        if (c is null) return null;
        var q = track ? context.Payments.AsQueryable() : context.Payments.AsNoTracking();
        return await q.Include(x => x.Customer).ThenInclude(x => x!.AppUser)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == c.Id);
    }


    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
    public async Task<IActionResult> Index(PaymentStatus? status)
    {
        var query = context.Payments.Include(x => x.Customer!).ThenInclude(x => x.AppUser).Include(x => x.FinancialOfficer!).ThenInclude(x => x.AppUser).OrderByDescending(x => x.SubmittedAtUtc).AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        ViewBag.SelectedStatus = status;
        return View(await query.ToListAsync());
    }
    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(PaymentReviewModel model)
    {
        if (!ModelState.IsValid || model.Status == PaymentStatus.Pending) return RedirectToAction(nameof(Index));
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var officer = await context.FinancialOfficers.FirstOrDefaultAsync(x => x.AppUserId == user.Id && x.IsAuthorized);
        var payment = await context.Payments.FindAsync(model.PaymentId);
        if (payment is null) return NotFound();
        // Administrators may review without a Financial Officer profile
        if (officer is null && !User.IsInRole(SystemRoles.Administrator)) return Forbid();
        if (payment.Status != PaymentStatus.Pending)
        {
            TempData["Error"] = "Only payments pending Financial Officer review can be accepted or rejected.";
            return RedirectToAction(nameof(Index));
        }

        payment.Status = model.Status;
        if (officer is not null) payment.FinancialOfficerId = officer.Id;
        if (!string.IsNullOrWhiteSpace(model.OfficerReference))
            payment.OfficerReference = model.OfficerReference;
        payment.ReviewedAtUtc = DateTime.UtcNow;

        if (model.Status == PaymentStatus.Accepted)
        {
            payment.CompletedAtUtc = DateTime.UtcNow;
            payment.FailureReason = null;
            await context.SaveChangesAsync();
            if (officer is not null)
                await transactions.CreateForAcceptedPaymentAsync(payment, officer.Id);
            else
            {
                // Admin approval without FO profile — still create ledger entry
                var exists = await context.Transactions.AnyAsync(x => x.PaymentId == payment.Id);
                if (!exists)
                {
                    context.Transactions.Add(new Transaction
                    {
                        PaymentId = payment.Id,
                        Amount = payment.Amount,
                        Status = TransactionStatus.Completed,
                        TransactionReference = payment.TransactionReference ?? $"AP-{payment.Id:D6}",
                        CreatedAtUtc = DateTime.UtcNow,
                        CompletedAtUtc = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                }
            }
            await notifications.CreateAsync(
                payment.CustomerId,
                "Payment approved",
                $"Your {payment.PaymentType} payment ({payment.TransactionReference}) has been approved successfully. You can now view your receipt.");
            TempData["Success"] = "Payment accepted and marked as PAID.";
        }
        else if (model.Status == PaymentStatus.Rejected)
        {
            payment.CompletedAtUtc = DateTime.UtcNow;
            payment.FailureReason = string.IsNullOrWhiteSpace(model.OfficerReference)
                ? "Payment was not accepted by the Financial Officer."
                : model.OfficerReference;
            await context.SaveChangesAsync();
            await notifications.CreateAsync(
                payment.CustomerId,
                "Payment rejected",
                $"Your {payment.PaymentType} payment ({payment.TransactionReference}) was not accepted. Reason: {payment.FailureReason}");
            TempData["Success"] = "Payment rejected.";
        }
        else
        {
            TempData["Error"] = "Invalid review action.";
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
    public async Task<IActionResult> Customers() => View(await context.Customers.Include(x => x.AppUser).OrderBy(x => x.AppUser!.LastName).AsNoTracking().ToListAsync());
    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator), HttpGet]
    public async Task<IActionResult> ResetPassword(int id) {
        var c = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (c?.AppUser is null) return NotFound();
        ViewBag.Customer = c;
        return View();
    }
    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string newPassword, string confirmPassword)
    {
        var c = await context.Customers.Include(x => x.AppUser).FirstOrDefaultAsync(x => x.Id == id);
        if (c?.AppUser is null) return NotFound();
        if (newPassword != confirmPassword || newPassword.Length < 8) {
            ModelState.AddModelError(string.Empty, "Passwords must match and contain at least eight characters.");
            ViewBag.Customer = c;
            return View();
        }
        var token = await userManager.GeneratePasswordResetTokenAsync(c.AppUser);
        var result = await userManager.ResetPasswordAsync(c.AppUser, token, newPassword);
        if (!result.Succeeded) {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.Customer = c;
            return View();
        }
        TempData["Success"] = "Customer password reset. Share it securely.";
        return RedirectToAction(nameof(Customers));
    }

    [Authorize]
    public async Task<IActionResult> Receipt(int id)
    {
        var payment = await context.Payments
            .Include(x => x.Customer).ThenInclude(c => c!.AppUser)
            .Include(x => x.Transaction)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (payment is null) return NotFound();

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (User.IsInRole(SystemRoles.Customer))
        {
            var customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.AppUserId == user.Id);
            if (customer is null || payment.CustomerId != customer.Id) return Forbid();
        }

        if (payment.Status != PaymentStatus.Accepted)
            return RedirectToAction(nameof(Result), new { id });

        return View(payment);
    }

    private async Task<Customer?> CurrentCustomer() {
        var u = await userManager.GetUserAsync(User);
        return u is null ? null : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == u.Id);
    }
}
