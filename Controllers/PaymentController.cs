using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ArnoxPay.Controllers;

public class PaymentController(AppDbContext context, UserManager<AppUser> userManager, INotificationService notifications, ITransactionService transactions, ICheckoutService checkout, ICampusAccountService campusAccounts) : Controller
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
                .Include(x => x.Items)
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
        return View(await context.Payments
            .Include(x => x.Items)
            .Where(x => x.CustomerId == c.Id)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .AsNoTracking()
            .ToListAsync());
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
        // Prefill when redirected after registering a second vehicle (second-car parking permit)
        if (TempData["SecondCarPermit"] as string == "true")
        {
            model.PaymentType = PaymentType.Parking;
            model.Notes = "Second-car parking permit" + (TempData["SecondCarReg"] is string reg ? $" for {reg}" : "");
            if (TempData["Success"] != null)
                TempData.Keep("Success");
            TempData.Keep("SecondCarPermit");
        }
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
        var method = paymentMethod.Trim();
        if (string.Equals(method, "Mastercard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "Card/Bank Payment", StringComparison.OrdinalIgnoreCase))
        {
            await checkout.SetPaymentMethodAsync(payment, "Mastercard");
            return RedirectToAction(nameof(CardPayment), new { id });
        }
        if (string.Equals(method, "Campus Account", StringComparison.OrdinalIgnoreCase))
        {
            var balance = await campusAccounts.GetBalanceAsync(payment.CustomerId);
            if (balance < payment.Amount)
            {
                TempData["Error"] = $"Insufficient funds. Your campus account balance is R{balance:N2}, but you need R{payment.Amount:N2}.";
                return RedirectToAction(nameof(FundCampusAccount), new { returnPaymentId = id });
            }
            await checkout.SetPaymentMethodAsync(payment, "Campus Account");
            return RedirectToAction(nameof(Confirm), new { id });
        }
        await checkout.SetPaymentMethodAsync(payment, method);
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
            // Debit campus account before finalising process if that method was chosen
            if (string.Equals(payment.PaymentMethod, "Campus Account", StringComparison.OrdinalIgnoreCase))
            {
                var (ok, msg) = await campusAccounts.DebitAsync(
                    payment.CustomerId, payment.Amount, payment.Id, payment.TransactionReference ?? payment.Id.ToString());
                if (!ok)
                {
                    await checkout.MarkFailedAsync(payment, msg);
                    await notifications.CreateAsync(payment.CustomerId, "Payment failed", msg);
                    return RedirectToAction(nameof(Result), new { id });
                }
            }

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
            .Include(x => x.Items)
            .Include(x => x.Transaction)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == c.Id);
    }


    [Authorize(Roles = SystemRoles.FinancialOfficer + "," + SystemRoles.Administrator)]
    public async Task<IActionResult> Index(PaymentStatus? status)
    {
        var query = context.Payments
            .Include(x => x.Customer!).ThenInclude(x => x.AppUser)
            .Include(x => x.FinancialOfficer!).ThenInclude(x => x.AppUser)
            .Include(x => x.Items)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .AsNoTracking()
            .AsQueryable();
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
            // Mark line items as paid so the same charge cannot be paid twice
            var items = await context.PaymentItems.Where(i => i.PaymentId == payment.Id).ToListAsync();
            foreach (var item in items) item.IsPaid = true;
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
            .Include(x => x.Items)
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


    // ---- Multi-service / multi-vehicle checkout (additive) ----

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> SelectServices()
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        var period = DateTime.UtcNow.ToString("yyyyMM");
        var vehicles = await context.Vehicles.Where(v => v.CustomerId == c.Id && v.IsActive).AsNoTracking().ToListAsync();
        var model = new MultiCheckoutViewModel
        {
            Charges =
            [
                new SelectableCharge { PaymentType = PaymentType.Rent, DisplayName = "Rent", Amount = 0m, OutstandingChargeKey = $"rent-{c.Id}-{period}" },
                new SelectableCharge { PaymentType = PaymentType.Electricity, DisplayName = "Electricity", Amount = 0m, OutstandingChargeKey = $"elec-{c.Id}-{period}" },
                new SelectableCharge { PaymentType = PaymentType.Water, DisplayName = "Water", Amount = 0m, OutstandingChargeKey = $"water-{c.Id}-{period}" },
                new SelectableCharge { PaymentType = PaymentType.RefuseCollection, DisplayName = "Refuse", Amount = 0m, OutstandingChargeKey = $"refuse-{c.Id}-{period}" },
            ]
        };
        foreach (var v in vehicles)
        {
            model.Charges.Add(new SelectableCharge
            {
                PaymentType = PaymentType.Parking,
                DisplayName = $"Parking - {v.RegistrationNumber}",
                Amount = 0m,
                VehicleId = v.Id,
                VehicleRegistration = v.RegistrationNumber,
                OutstandingChargeKey = $"park-{v.Id}-{period}"
            });
        }
        var paid = await context.PaymentItems
            .Where(pi => pi.Payment!.CustomerId == c.Id && pi.IsPaid)
            .Select(pi => pi.OutstandingChargeKey).ToListAsync();
        foreach (var ch in model.Charges)
            if (paid.Contains(ch.OutstandingChargeKey)) ch.AlreadyPaid = true;
        return View(model);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectServices(MultiCheckoutViewModel model)
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        var items = new List<CheckoutLineItem>();
        if (model.Charges != null)
        {
            foreach (var ch in model.Charges.Where(x => x.Selected && !x.AlreadyPaid && x.Amount > 0))
            {
                items.Add(new CheckoutLineItem
                {
                    PaymentType = ch.PaymentType,
                    Amount = ch.Amount,
                    Description = ch.DisplayName,
                    VehicleId = ch.VehicleId,
                    VehicleRegistration = ch.VehicleRegistration,
                    OutstandingChargeKey = ch.OutstandingChargeKey
                });
            }
        }
        if (items.Count == 0)
        {
            var selectedNoAmount = model.Charges != null && model.Charges.Any(x => x.Selected && !x.AlreadyPaid && x.Amount <= 0);
            ModelState.AddModelError(string.Empty,
                selectedNoAmount
                    ? "Enter an amount greater than R0 for each selected payment."
                    : "Select at least one payment and enter the amount.");
            return View(model);
        }
        try
        {
            var payment = await checkout.CreateMultiCheckoutAsync(c.Id, items, model.Notes, model.IdempotencyKey);
            return RedirectToAction(nameof(Details), new { id = payment.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> CardPayment(int id)
    {
        var payment = await LoadOwnPayment(id);
        if (payment is null) return NotFound();
        return View(new CardPaymentFormModel
        {
            PaymentId = payment.Id,
            Amount = payment.Amount,
            TransactionReference = payment.TransactionReference
        });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CardPayment(CardPaymentFormModel model)
    {
        var payment = await LoadOwnPayment(model.PaymentId, track: true);
        if (payment is null) return NotFound();
        var serverAmount = payment.Amount; // never trust browser amount
        var v = CardPaymentValidator.Validate(model.CardholderName, model.CardNumber, model.ExpiryMonth, model.ExpiryYear, model.Cvv, serverAmount);
        if (!v.IsValid)
        {
            ModelState.AddModelError(string.Empty, v.ErrorMessage ?? "Payment could not be processed");
            model.Amount = serverAmount;
            model.TransactionReference = payment.TransactionReference;
            model.CardNumber = "";
            model.Cvv = "";
            return View(model);
        }
        await checkout.SetPaymentMethodAsync(payment, "Mastercard");
        await checkout.SetMaskedCardAsync(payment, v.MaskedNumber!);
        return RedirectToAction(nameof(Confirm), new { id = payment.Id });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> CampusBalance()
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        var account = await campusAccounts.GetOrCreateAsync(c.Id);
        var history = await context.CampusAccountTransfers
            .Where(t => t.CampusAccountId == account.Id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(50)
            .AsNoTracking()
            .ToListAsync();
        var user = await userManager.GetUserAsync(User);
        ViewBag.Balance = account.Balance;
        ViewBag.CustomerName = user is null ? c.UserNumber : $"{user.FirstName} {user.LastName}".Trim();
        return View(history);
    }

    [Authorize(Roles = SystemRoles.Customer), HttpGet]
    public async Task<IActionResult> FundCampusAccount(int? returnPaymentId = null)
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        var balance = await campusAccounts.GetBalanceAsync(c.Id);
        decimal required = 0;
        if (returnPaymentId.HasValue)
        {
            var pay = await LoadOwnPayment(returnPaymentId.Value);
            if (pay is not null) required = pay.Amount;
        }
        return View(new CampusFundViewModel
        {
            CurrentBalance = balance,
            RequiredAmount = required,
            ReturnPaymentId = returnPaymentId,
            TransferAmount = required > balance ? required - balance : 100m
        });
    }

    [Authorize(Roles = SystemRoles.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FundCampusAccount(CampusFundViewModel model)
    {
        var c = await CurrentCustomer();
        if (c is null) return NotFound();
        if (model.TransferAmount <= 0)
        {
            ModelState.AddModelError(string.Empty, "Payment amount is invalid");
            model.CurrentBalance = await campusAccounts.GetBalanceAsync(c.Id);
            return View(model);
        }
        string? source = "Manual top-up";
        if (!string.IsNullOrWhiteSpace(model.CardNumber))
        {
            var v = CardPaymentValidator.Validate(model.CardholderName, model.CardNumber, model.ExpiryMonth, model.ExpiryYear, model.Cvv, model.TransferAmount);
            if (!v.IsValid)
            {
                ModelState.AddModelError(string.Empty, v.ErrorMessage ?? "Payment could not be processed");
                model.CurrentBalance = await campusAccounts.GetBalanceAsync(c.Id);
                model.CardNumber = null; model.Cvv = null;
                return View(model);
            }
            source = v.MaskedNumber;
        }
        var (ok, msg, _) = await campusAccounts.DepositAsync(c.Id, model.TransferAmount, source);
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, msg);
            model.CurrentBalance = await campusAccounts.GetBalanceAsync(c.Id);
            return View(model);
        }
        TempData["Success"] = msg;
        if (model.ReturnPaymentId.HasValue)
            return RedirectToAction(nameof(Details), new { id = model.ReturnPaymentId.Value });
        return RedirectToAction(nameof(FundCampusAccount));
    }


    private async Task<Customer?> CurrentCustomer() {
        var u = await userManager.GetUserAsync(User);
        return u is null ? null : await context.Customers.FirstOrDefaultAsync(x => x.AppUserId == u.Id);
    }
}
