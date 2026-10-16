using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Controllers.Api;

/// <summary>
/// REST API for the ArnoxPay mobile client. Uses the same Azure SQL / SQLite database
/// and business rules as the web application.
/// Authentication: pass X-User-Id header after login (userId returned by /api/auth/login).
/// For production, replace with JWT bearer tokens.
/// </summary>
[ApiController]
[Route("api/mobile")]
public class MobileApiController(
    AppDbContext context,
    UserManager<AppUser> userManager,
    ICheckoutService checkout,
    ICampusAccountService campusAccounts,
    IOptions<FeeSettings> feeOptions) : ControllerBase
{
    private readonly FeeSettings _fees = feeOptions.Value;

    private async Task<(AppUser? user, Customer? customer, string? role)> ResolveUserAsync()
    {
        var userId = Request.Headers["X-User-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId)) return (null, null, null);
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return (null, null, null);
        var roles = await userManager.GetRolesAsync(user);
        var role = roles.Contains(SystemRoles.Administrator) ? SystemRoles.Administrator
            : roles.Contains(SystemRoles.FinancialOfficer) ? SystemRoles.FinancialOfficer
            : SystemRoles.Customer;
        var customer = await context.Customers
            .Include(c => c.Accomodation)
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id);
        return (user, customer, role);
    }

    // ---------- Profile / dashboard ----------

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var (user, customer, role) = await ResolveUserAsync();
        if (user is null) return Unauthorized(new { success = false, message = "Not authenticated." });

        return Ok(new
        {
            success = true,
            userId = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            role,
            customerId = customer?.Id,
            userNumber = customer?.UserNumber,
            category = customer?.Category.ToString(),
            accommodation = customer?.Accomodation is null ? null : new
            {
                id = customer.Accomodation.Id,
                name = customer.Accomodation.Name,
                unitNumber = customer.Accomodation.UnitNumber,
                address = customer.Accomodation.Address
            }
        });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var (user, customer, role) = await ResolveUserAsync();
        if (user is null) return Unauthorized(new { success = false, message = "Not authenticated." });

        if (role is SystemRoles.FinancialOfficer or SystemRoles.Administrator)
        {
            var accepted = context.Payments.Where(p => p.Status == PaymentStatus.Accepted);
            var pending = context.Payments.Where(p =>
                p.Status == PaymentStatus.Pending ||
                p.Status == PaymentStatus.AwaitingAuthentication ||
                p.Status == PaymentStatus.Processing);
            var rejected = context.Payments.Where(p =>
                p.Status == PaymentStatus.Rejected || p.Status == PaymentStatus.Failed);

            return Ok(new
            {
                success = true,
                role,
                acceptedCount = await accepted.CountAsync(),
                acceptedTotal = await accepted.SumAsync(p => (decimal?)p.Amount) ?? 0,
                pendingCount = await pending.CountAsync(),
                pendingTotal = await pending.SumAsync(p => (decimal?)p.Amount) ?? 0,
                rejectedCount = await rejected.CountAsync(),
                rejectedTotal = await rejected.SumAsync(p => (decimal?)p.Amount) ?? 0,
                activeCustomers = await context.Customers.CountAsync(c => c.AppUser!.IsActive)
            });
        }

        if (customer is null) return NotFound(new { success = false, message = "Customer profile not found." });

        var q = context.Payments.Where(p => p.CustomerId == customer.Id);
        return Ok(new
        {
            success = true,
            role,
            acceptedCount = await q.CountAsync(p => p.Status == PaymentStatus.Accepted),
            acceptedTotal = await q.Where(p => p.Status == PaymentStatus.Accepted).SumAsync(p => (decimal?)p.Amount) ?? 0,
            pendingCount = await q.CountAsync(p =>
                p.Status == PaymentStatus.Pending ||
                p.Status == PaymentStatus.AwaitingAuthentication ||
                p.Status == PaymentStatus.Processing),
            accommodation = customer.Accomodation is null ? null : new
            {
                name = customer.Accomodation.Name,
                unitNumber = customer.Accomodation.UnitNumber
            }
        });
    }

    // ---------- Fees / tariffs ----------

    [HttpGet("fees")]
    [AllowAnonymous]
    public IActionResult Fees()
    {
        return Ok(new
        {
            success = true,
            electricityPricePerKwh = _fees.ElectricityPricePerKwh,
            waterPricePerKl = _fees.WaterPricePerKl,
            firstVehicleFee = _fees.FirstVehicleFee,
            subsequentVehicleFee = _fees.SubsequentVehicleFee,
            parkingPermitApplicationFee = _fees.ParkingPermitApplicationFee,
            maxElectricityUnits = _fees.MaxElectricityUnits,
            maxWaterUnits = _fees.MaxWaterUnits,
            currency = "ZAR"
        });
    }

    // ---------- Payments ----------

    [HttpGet("payments")]
    public async Task<IActionResult> Payments()
    {
        var (user, customer, role) = await ResolveUserAsync();
        if (user is null) return Unauthorized(new { success = false, message = "Not authenticated." });

        IQueryable<Payment> q = context.Payments
            .Include(p => p.Items)
            .Include(p => p.Customer!).ThenInclude(c => c.AppUser)
            .AsNoTracking()
            .OrderByDescending(p => p.SubmittedAtUtc);

        if (role == SystemRoles.Customer)
        {
            if (customer is null) return NotFound();
            q = q.Where(p => p.CustomerId == customer.Id);
        }

        var list = await q.Take(100).ToListAsync();
        return Ok(new
        {
            success = true,
            payments = list.Select(p => new
            {
                id = p.Id,
                reference = p.TransactionReference,
                type = p.PaymentType.ToString(),
                amount = p.Amount,
                status = p.Status.ToString(),
                method = p.PaymentMethod,
                submittedAtUtc = p.SubmittedAtUtc,
                customerName = p.Customer?.AppUser != null
                    ? $"{p.Customer.AppUser.FirstName} {p.Customer.AppUser.LastName}" : null,
                items = p.Items.Select(i => new
                {
                    type = i.PaymentType.ToString(),
                    amount = i.Amount,
                    description = i.Description,
                    quantity = i.Quantity,
                    unitPrice = i.UnitPrice,
                    unitOfMeasure = i.UnitOfMeasure,
                    vehicleRegistration = i.VehicleRegistration
                })
            })
        });
    }

    public record UtilityPaymentRequest(string Service, decimal Quantity, int? AccommodationId, string? Notes);

    [HttpPost("payments/utility")]
    public async Task<IActionResult> CreateUtilityPayment([FromBody] UtilityPaymentRequest req)
    {
        var (user, customer, role) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (!Enum.TryParse<PaymentType>(req.Service, true, out var type) ||
            (type != PaymentType.Electricity && type != PaymentType.Water))
            return BadRequest(new { success = false, message = "Service must be Electricity or Water." });

        decimal? unitPrice = type == PaymentType.Electricity ? _fees.ElectricityPricePerKwh : _fees.WaterPricePerKl;
        if (unitPrice is null)
            return BadRequest(new { success = false, message = $"{type} tariff is not configured." });

        var max = type == PaymentType.Electricity ? _fees.MaxElectricityUnits : _fees.MaxWaterUnits;
        if (req.Quantity <= 0 || req.Quantity > max)
            return BadRequest(new { success = false, message = $"Quantity must be between 0.01 and {max}." });

        var unit = type == PaymentType.Electricity ? "kWh" : "kL";
        var total = Math.Round(req.Quantity * unitPrice.Value, 2);
        var accId = req.AccommodationId ?? customer.AccomodationId;

        var payment = await checkout.CreateCheckoutAsync(
            customer.Id, type, total,
            accountReference: $"{type}-{customer.UserNumber}",
            notes: req.Notes);

        var item = payment.Items.FirstOrDefault();
        if (item != null)
        {
            item.Quantity = req.Quantity;
            item.UnitPrice = unitPrice;
            item.UnitOfMeasure = unit;
            item.AccomodationId = accId;
            item.Description = $"{type}: {req.Quantity} {unit} @ {unitPrice:C}/{unit}";
            await context.SaveChangesAsync();
        }

        return Ok(new
        {
            success = true,
            paymentId = payment.Id,
            reference = payment.TransactionReference,
            amount = payment.Amount,
            status = payment.Status.ToString(),
            quantity = req.Quantity,
            unitPrice,
            unitOfMeasure = unit,
            total
        });
    }

    // ---------- Vehicles ----------

    [HttpGet("vehicles")]
    public async Task<IActionResult> Vehicles()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var vehicles = await context.Vehicles
            .Where(v => v.CustomerId == customer.Id && v.IsActive)
            .OrderBy(v => v.RegisteredAtUtc)
            .AsNoTracking()
            .ToListAsync();

        var count = vehicles.Count;
        return Ok(new
        {
            success = true,
            vehicleCount = count,
            nextPosition = count + 1,
            nextFee = count == 0 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0),
            vehicles = vehicles.Select((v, i) => new
            {
                id = v.Id,
                registrationNumber = v.RegistrationNumber,
                make = v.Make,
                model = v.Model,
                colour = v.Colour,
                position = i + 1,
                positionLabel = i == 0 ? "First vehicle" : i == 1 ? "Second vehicle" : $"Vehicle #{i + 1}",
                registeredAtUtc = v.RegisteredAtUtc
            })
        });
    }

    public record RegisterVehicleRequest(string RegistrationNumber, string? Make, string? Model, string? Colour);

    [HttpPost("vehicles")]
    public async Task<IActionResult> RegisterVehicle([FromBody] RegisterVehicleRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var reg = (req.RegistrationNumber ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(reg))
            return BadRequest(new { success = false, message = "Registration number is required." });

        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            if (await context.Vehicles.AnyAsync(v => v.RegistrationNumber == reg))
                return Conflict(new { success = false, message = "Vehicle registration already exists." });

            var existingCount = await context.Vehicles.CountAsync(v => v.CustomerId == customer.Id && v.IsActive);
            var position = existingCount + 1;
            var fee = position == 1 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0);

            var vehicle = new Vehicle
            {
                CustomerId = customer.Id,
                RegistrationNumber = reg,
                Make = req.Make?.Trim(),
                Model = req.Model?.Trim(),
                Colour = req.Colour?.Trim(),
                RegisteredAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            context.Vehicles.Add(vehicle);
            await context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                success = true,
                vehicleId = vehicle.Id,
                registrationNumber = vehicle.RegistrationNumber,
                position,
                positionLabel = position == 1 ? "First vehicle" : position == 2 ? "Second vehicle" : $"Vehicle #{position}",
                fee,
                paymentRequired = fee > 0,
                message = fee > 0
                    ? $"Vehicle #{position} registered. Applicable fee: {fee:C}."
                    : "First vehicle registered. Registration fee: R0.00."
            });
        }
        catch
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Could not register vehicle." });
        }
    }

    // ---------- Parking permits (free application) ----------

    [HttpGet("permits")]
    public async Task<IActionResult> Permits()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var permits = await context.ParkingPermits
            .Include(p => p.Vehicle)
            .Where(p => p.CustomerId == customer.Id)
            .OrderByDescending(p => p.AppliedAtUtc)
            .AsNoTracking()
            .ToListAsync();

        return Ok(new
        {
            success = true,
            applicationFee = 0m,
            permits = permits.Select(p => new
            {
                id = p.Id,
                permitNumber = p.PermitNumber,
                permitType = p.PermitType,
                status = p.Status.ToString(),
                fee = p.Fee,
                vehicleRegistration = p.Vehicle?.RegistrationNumber,
                appliedAtUtc = p.AppliedAtUtc,
                issueDate = p.IssueDate,
                expiryDate = p.ExpiryDate
            })
        });
    }

    public record ApplyPermitRequest(int VehicleId, string? PermitType);

    [HttpPost("permits")]
    public async Task<IActionResult> ApplyPermit([FromBody] ApplyPermitRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v =>
            v.Id == req.VehicleId && v.CustomerId == customer.Id && v.IsActive);
        if (vehicle is null)
            return BadRequest(new { success = false, message = "Select a valid registered vehicle." });

        var pendingOrActive = await context.ParkingPermits.AnyAsync(p =>
            p.VehicleId == req.VehicleId &&
            (p.Status == ParkingPermitStatus.Active || p.Status == ParkingPermitStatus.PendingApproval));
        if (pendingOrActive)
            return Conflict(new { success = false, message = "This vehicle already has an active or pending permit." });

        var number = $"AP-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(10000, 99999)}";
        var permit = new ParkingPermit
        {
            PermitNumber = number,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            PermitType = string.IsNullOrWhiteSpace(req.PermitType) ? "Student annual" : req.PermitType.Trim(),
            IssueDate = DateTime.UtcNow.Date,
            ExpiryDate = DateTime.UtcNow.Date.AddYears(1),
            Status = ParkingPermitStatus.PendingApproval,
            Fee = 0m,
            AppliedAtUtc = DateTime.UtcNow
        };
        context.ParkingPermits.Add(permit);
        await context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            permitId = permit.Id,
            permitNumber = permit.PermitNumber,
            fee = 0m,
            amountPayable = 0m,
            status = permit.Status.ToString(),
            message = "Permit application submitted free of charge (R0.00). Status: Pending approval."
        });

    }
        // ---------- Payment detail & amount-based payments ----------

        [HttpGet("payments/{id:int}")]
        public async Task<IActionResult> PaymentDetail(int id)
        {
            var (user, customer, _) = await ResolveUserAsync();
            if (user is null || customer is null)
                return Unauthorized(new { success = false, message = "Customer authentication required." });

            var payment = await context.Payments
                .Include(p => p.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.CustomerId == customer.Id);

            if (payment is null)
                return NotFound(new { success = false, message = "Payment not found." });

            return Ok(new
            {
                success = true,
                payment = new
                {
                    id = payment.Id,
                    reference = payment.TransactionReference,
                    type = payment.PaymentType.ToString(),
                    amount = payment.Amount,
                    status = payment.Status.ToString(),
                    notes = payment.Notes,
                    submittedAtUtc = payment.SubmittedAtUtc,
                    completedAtUtc = payment.CompletedAtUtc,
                    items = payment.Items.Select(i => new
                    {
                        description = i.Description,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                        amount = i.Amount
                    })
                }
            });
        }
    
    public record AmountPaymentRequest(string Service, decimal Amount, string? Notes);

    [HttpPost("payments/amount")]
    public async Task<IActionResult> CreateAmountPayment([FromBody] AmountPaymentRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than R0." });

        var service = (req.Service ?? "").Trim();
        PaymentType type;
        if (service.Contains("rent", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Rent;
        else if (service.Contains("refuse", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.RefuseCollection;
        else if (service.Contains("park", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Parking;
        else if (service.Contains("electric", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Electricity;
        else if (service.Contains("water", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Water;
        else if (!Enum.TryParse(service.Replace(" ", ""), true, out type))
            return BadRequest(new { success = false, message = "Unknown service type. Use Rent, Refuse, Parking, Electricity or Water." });

        var amount = Math.Round(req.Amount, 2);
        var payment = await checkout.CreateCheckoutAsync(
            customer.Id, type, amount,
            accountReference: $"{type}-{customer.UserNumber}",
            notes: req.Notes);

        // Status remains Pending / AwaitingAuthentication until Financial Officer accepts — same as website.
        return Ok(new
        {
            success = true,
            paymentId = payment.Id,
            reference = payment.TransactionReference,
            transactionReference = payment.TransactionReference,
            amount = payment.Amount,
            status = payment.Status.ToString(),
            type = payment.PaymentType.ToString(),
            message = "Payment submitted and is pending review."
        });
    }

    // ---------- Accommodations ----------

    [HttpGet("accommodations")]
    public async Task<IActionResult> Accommodations()
    {
        var (user, _, _) = await ResolveUserAsync();
        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        var list = await context.Accomodations
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .AsNoTracking()
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                unitNumber = a.UnitNumber,
                address = a.Address,
                type = a.AccommodationType.ToString(),
                capacity = a.Capacity,
                description = a.Description
            })
            .ToListAsync();

        return Ok(new { success = true, accommodations = list, items = list, data = list });
    }

    [HttpGet("accommodations/{id:int}")]
    public async Task<IActionResult> AccommodationDetails(int id)
    {
        var (user, _, _) = await ResolveUserAsync();
        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        var a = await context.Accomodations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);
        if (a is null)
            return NotFound(new { success = false, message = "Accommodation not found." });

        return Ok(new
        {
            success = true,
            id = a.Id,
            name = a.Name,
            unitNumber = a.UnitNumber,
            address = a.Address,
            type = a.AccommodationType.ToString(),
            capacity = a.Capacity,
            description = a.Description
        });
    }

    public record AccommodationRequestBody(int AccommodationId, string? Note);

    [HttpPost("accommodations/request")]
    public async Task<IActionResult> RequestAccommodation([FromBody] AccommodationRequestBody req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var acc = await context.Accomodations.FirstOrDefaultAsync(a => a.Id == req.AccommodationId && a.IsActive);
        if (acc is null)
            return BadRequest(new { success = false, message = "Select a valid accommodation." });

        var pending = await context.AccommodationAssignmentRequests.AnyAsync(r =>
            r.CustomerId == customer.Id &&
            r.AccomodationId == req.AccommodationId &&
            r.Status == AccommodationAssignmentStatus.Pending);
        if (pending)
            return Conflict(new { success = false, message = "You already have a pending request for this unit." });

        var entity = new AccommodationAssignmentRequest
        {
            CustomerId = customer.Id,
            AccomodationId = acc.Id,
            Status = AccommodationAssignmentStatus.Pending,
            CustomerNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            RequestedAtUtc = DateTime.UtcNow
        };
        context.AccommodationAssignmentRequests.Add(entity);
        await context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            requestId = entity.Id,
            status = entity.Status.ToString(),
            message = "Accommodation request submitted. Status: Pending."
        });
    }

    [HttpGet("accommodations/requests")]
    public async Task<IActionResult> AccommodationRequests()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var list = await context.AccommodationAssignmentRequests
            .Include(r => r.Accomodation)
            .Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.RequestedAtUtc)
            .AsNoTracking()
            .Select(r => new
            {
                id = r.Id,
                status = r.Status.ToString(),
                customerNote = r.CustomerNote,
                administratorNote = r.AdministratorNote,
                requestedAtUtc = r.RequestedAtUtc,
                reviewedAtUtc = r.ReviewedAtUtc,
                accommodationId = r.AccomodationId,
                accommodationName = r.Accomodation != null ? r.Accomodation.Name : null,
                unitNumber = r.Accomodation != null ? r.Accomodation.UnitNumber : null
            })
            .ToListAsync();

        return Ok(new { success = true, requests = list, items = list, data = list });
    }

    // ---------- Campus account ----------

    [HttpGet("campus-balance")]
    public async Task<IActionResult> CampusBalance()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var balance = await campusAccounts.GetBalanceAsync(customer.Id);
        return Ok(new { success = true, balance, currency = "ZAR" });
    }

    public record FundCampusRequest(decimal Amount, string? FundingSource);

    [HttpPost("campus-balance/fund")]
    public async Task<IActionResult> FundCampus([FromBody] FundCampusRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than R0." });

        var (ok, message, transfer) = await campusAccounts.DepositAsync(
            customer.Id, Math.Round(req.Amount, 2), req.FundingSource ?? "Mobile app");

        if (!ok)
            return BadRequest(new { success = false, message });

        var balance = await campusAccounts.GetBalanceAsync(customer.Id);
        return Ok(new
        {
            success = true,
            message,
            balance,
            transferId = transfer?.Id
        });
    }

    // ---------- Issues ----------

    [HttpGet("issues")]
    public async Task<IActionResult> Issues()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var list = await context.AccommodationIssues
            .Where(i => i.CustomerId == customer.Id)
            .OrderByDescending(i => i.ReportedAtUtc)
            .AsNoTracking()
            .Select(i => new
            {
                id = i.Id,
                title = i.Title,
                category = i.Category.ToString(),
                status = i.Status.ToString(),
                description = i.Description,
                reportedAtUtc = i.ReportedAtUtc
            })
            .ToListAsync();

        return Ok(new { success = true, issues = list, items = list, data = list });
    }

    public record ReportIssueRequest(string Title, string? Category, string Description);

    [HttpPost("issues")]
    public async Task<IActionResult> ReportIssue([FromBody] ReportIssueRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (customer.AccomodationId is null)
            return BadRequest(new { success = false, message = "You must be assigned to accommodation before reporting issues." });

        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(new { success = false, message = "Title and description are required." });

        var cat = AccommodationIssueCategory.Other;
        if (!string.IsNullOrWhiteSpace(req.Category))
            Enum.TryParse(req.Category.Replace(" ", ""), true, out cat);

        var issue = new AccommodationIssue
        {
            CustomerId = customer.Id,
            AccomodationId = customer.AccomodationId.Value,
            Title = req.Title.Trim(),
            Description = req.Description.Trim(),
            Category = cat,
            Status = AccommodationIssueStatus.Reported,
            ReportedAtUtc = DateTime.UtcNow
        };
        context.AccommodationIssues.Add(issue);
        await context.SaveChangesAsync();

        return Ok(new { success = true, issueId = issue.Id, status = issue.Status.ToString(), message = "Issue reported." });
    }

    // ---------- Reviews ----------

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var list = await context.ServiceReviews
            .Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .AsNoTracking()
            .Select(r => new
            {
                id = r.Id,
                rating = r.Rating,
                category = r.Category,
                comment = r.Comment,
                createdAtUtc = r.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(new { success = true, reviews = list, items = list, data = list });
    }

    public record SubmitReviewRequest(int Rating, string? Category, string Comment);

    [HttpPost("reviews")]
    public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest req)
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { success = false, message = "Rating must be between 1 and 5." });
        if (string.IsNullOrWhiteSpace(req.Comment))
            return BadRequest(new { success = false, message = "Comment is required." });

        var review = new ServiceReview
        {
            CustomerId = customer.Id,
            Rating = req.Rating,
            Category = string.IsNullOrWhiteSpace(req.Category) ? "General" : req.Category.Trim(),
            Comment = req.Comment.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        context.ServiceReviews.Add(review);
        await context.SaveChangesAsync();

        return Ok(new { success = true, reviewId = review.Id, message = "Review submitted." });
    }

    // ---------- Notifications ----------

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications()
    {
        var (user, customer, _) = await ResolveUserAsync();
        if (user is null || customer is null)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var list = await context.Notifications
            .Where(n => n.CustomerId == customer.Id)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(100)
            .AsNoTracking()
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                message = n.Message,
                isRead = n.IsRead,
                createdAtUtc = n.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(new { success = true, notifications = list, items = list, data = list });
    }

    // ---------- Change password ----------

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? ConfirmPassword);

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var (user, _, _) = await ResolveUserAsync();
        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (string.IsNullOrEmpty(req.CurrentPassword) || string.IsNullOrEmpty(req.NewPassword))
            return BadRequest(new { success = false, message = "Current and new password are required." });

        if (req.ConfirmPassword != null && req.NewPassword != req.ConfirmPassword)
            return BadRequest(new { success = false, message = "Passwords do not match." });

        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new
            {
                success = false,
                message = string.Join(" ", result.Errors.Select(e => e.Description))
            });

        return Ok(new { success = true, message = "Password changed." });
    }
}

