using System.Data;
using System.Globalization;
using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Controllers.Api;

/// <summary>
/// REST API for the ArnoxPay mobile client.
///
/// Mobile endpoints authenticate using short-lived, protected bearer tokens.
/// The token is issued only after ASP.NET Identity validates the credentials;
/// clients must never supply a user ID as an authentication credential.
/// </summary>
[ApiController]
[Route("api/mobile")]
public class MobileApiController(
    AppDbContext context,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ICheckoutService checkout,
    ICampusAccountService campusAccounts,
    IFinancialReportService reports,
    IOptions<FeeSettings> feeOptions,
    MobileTokenService mobileTokens,
    ILogger<MobileApiController> logger) : ControllerBase
{
    private readonly FeeSettings _fees = feeOptions.Value;

    private const string ResidenceApprovalMessage =
        "Your residence application has been approved.";

    private static readonly NumberFormatInfo ZarNumberFormat = CreateZarFormat();

    private static NumberFormatInfo CreateZarFormat()
    {
        var format = (NumberFormatInfo)CultureInfo
            .GetCultureInfo("en-ZA").NumberFormat.Clone();

        format.NumberDecimalSeparator = ",";
        format.NumberGroupSeparator = " ";
        return format;
    }

    private static string FormatZar(decimal amount) =>
        "R" + amount.ToString("N2", ZarNumberFormat);

    private async Task<(AppUser? user, Customer? customer, string? role)> ResolveUserAsync()
    {
        if (!mobileTokens.TryReadBearer(Request.Headers.Authorization.ToString(), out var token) || token is null)
            return (null, null, null);

        var user = await userManager.FindByIdAsync(token.UserId);

        if (user is null || !user.IsActive ||
            !string.Equals(await userManager.GetSecurityStampAsync(user), token.SecurityStamp, StringComparison.Ordinal))
            return (null, null, null);

        var roles = await userManager.GetRolesAsync(user);
        string? role = roles.Contains(SystemRoles.Administrator)
            ? SystemRoles.Administrator
            : roles.Contains(SystemRoles.FinancialOfficer)
                ? SystemRoles.FinancialOfficer
                : roles.Contains(SystemRoles.Customer)
                    ? SystemRoles.Customer
                    : null;

        if (role is null)
            return (null, null, null);

        var customer = await context.Customers
            .Include(c => c.Accomodation)
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id);

        return (user, customer, role);
    }

    private static bool IsStaffRole(string? role) =>
        role is SystemRoles.FinancialOfficer or SystemRoles.Administrator;

    private static bool IsAdminRole(string? role) =>
        role == SystemRoles.Administrator;

    // ---------- Profile / dashboard ----------

    /// <summary>
    /// Exchanges a valid mobile bearer token for the normal Identity cookie in the app's WebView.
    /// The token is accepted only in the Authorization header and is never placed in the URL.
    /// </summary>
    [HttpGet("web-session")]
    public async Task<IActionResult> CreateWebSession([FromQuery] string? returnUrl = null)
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Pragma"] = "no-cache";

        var (user, _, role) = await ResolveUserAsync();
        if (user is null || role is null)
            return Unauthorized(new { success = false, message = "Your mobile session is invalid or expired. Please sign in again." });

        var defaultPath = role switch
        {
            SystemRoles.Administrator => "/adminhome/index/",
            SystemRoles.FinancialOfficer => "/payment/dashboard/",
            _ => "/customer/index/"
        };
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : defaultPath;

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(target);
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

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
                corridor = customer.Accomodation.Corridor,
                roomNumber = customer.Accomodation.RoomNumber,
                address = customer.Accomodation.Address
            }
        });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (IsStaffRole(role))
        {
            var accepted = context.Payments.Where(p => p.Status == PaymentStatus.Accepted);
            var pending = context.Payments.Where(p =>
                p.Status == PaymentStatus.Pending ||
                p.Status == PaymentStatus.AwaitingAuthentication ||
                p.Status == PaymentStatus.Processing);
            var rejected = context.Payments.Where(p =>
                p.Status == PaymentStatus.Rejected ||
                p.Status == PaymentStatus.Failed);

            return Ok(new
            {
                success = true,
                role,
                acceptedCount = await accepted.CountAsync(),
                acceptedTotal = await accepted.SumAsync(p => (decimal?)p.Amount) ?? 0m,
                pendingCount = await pending.CountAsync(),
                pendingTotal = await pending.SumAsync(p => (decimal?)p.Amount) ?? 0m,
                rejectedCount = await rejected.CountAsync(),
                rejectedTotal = await rejected.SumAsync(p => (decimal?)p.Amount) ?? 0m,
                activeCustomers = await context.Customers.CountAsync(c =>
                    c.AppUser != null && c.AppUser.IsActive)
            });
        }

        if (customer is null)
            return NotFound(new { success = false, message = "Customer profile not found." });

        var q = context.Payments.Where(p => p.CustomerId == customer.Id);

        return Ok(new
        {
            success = true,
            role,
            acceptedCount = await q.CountAsync(p => p.Status == PaymentStatus.Accepted),
            acceptedTotal = await q
                .Where(p => p.Status == PaymentStatus.Accepted)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m,
            pendingCount = await q.CountAsync(p =>
                p.Status == PaymentStatus.Pending ||
                p.Status == PaymentStatus.AwaitingAuthentication ||
                p.Status == PaymentStatus.Processing),
            accommodation = customer.Accomodation is null ? null : new
            {
                name = customer.Accomodation.Name,
                unitNumber = customer.Accomodation.UnitNumber,
                corridor = customer.Accomodation.Corridor,
                roomNumber = customer.Accomodation.RoomNumber
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

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (role == SystemRoles.Customer && customer is null)
            return NotFound(new { success = false, message = "Customer profile not found." });

        IQueryable<Payment> query = context.Payments
            .Include(p => p.Items)
            .Include(p => p.Customer!).ThenInclude(c => c.AppUser)
            .AsNoTracking();

        if (role == SystemRoles.Customer)
            query = query.Where(p => p.CustomerId == customer!.Id);

        var list = await query
            .OrderByDescending(p => p.SubmittedAtUtc)
            .Take(100)
            .ToListAsync();

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
                customerName = p.Customer?.AppUser is not null
                    ? $"{p.Customer.AppUser.FirstName} {p.Customer.AppUser.LastName}"
                    : null,
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

    [HttpGet("payments/{id:int}")]
    public async Task<IActionResult> PaymentDetail(int id)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (role == SystemRoles.Customer && customer is null)
            return NotFound(new { success = false, message = "Customer profile not found." });

        IQueryable<Payment> query = context.Payments
            .Include(p => p.Items)
            .AsNoTracking()
            .Where(p => p.Id == id);

        if (role == SystemRoles.Customer)
            query = query.Where(p => p.CustomerId == customer!.Id);
        else if (!IsStaffRole(role))
            return Forbid();

        var payment = await query.FirstOrDefaultAsync();

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

    public record UtilityPaymentRequest(
        string Service,
        decimal Quantity,
        int? AccommodationId,
        string? Notes);

    [HttpPost("payments/utility")]
    public async Task<IActionResult> CreateUtilityPayment([FromBody] UtilityPaymentRequest req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null ||
            !Enum.TryParse<PaymentType>(req.Service, true, out var type) ||
            (type != PaymentType.Electricity && type != PaymentType.Water))
        {
            return BadRequest(new
            {
                success = false,
                message = "Service must be Electricity or Water."
            });
        }

        decimal? unitPrice = type == PaymentType.Electricity
            ? _fees.ElectricityPricePerKwh
            : _fees.WaterPricePerKl;

        if (unitPrice is null)
            return BadRequest(new { success = false, message = $"{type} tariff is not configured." });

        var max = type == PaymentType.Electricity
            ? _fees.MaxElectricityUnits
            : _fees.MaxWaterUnits;

        if (req.Quantity <= 0 || req.Quantity > max)
            return BadRequest(new
            {
                success = false,
                message = $"Quantity must be greater than 0 and no more than {max}."
            });

        var unit = type == PaymentType.Electricity ? "kWh" : "kL";
        var total = Math.Round(req.Quantity * unitPrice.Value, 2, MidpointRounding.AwayFromZero);
        var accommodationId = req.AccommodationId ?? customer.AccomodationId;

        // If an accommodation ID was supplied, ensure it belongs to this customer
        // or matches the customer's current assigned accommodation.
        if (accommodationId.HasValue &&
            accommodationId != customer.AccomodationId)
        {
            return BadRequest(new
            {
                success = false,
                message = "The selected accommodation is not assigned to your account."
            });
        }

        var payment = await checkout.CreateCheckoutAsync(
            customer.Id,
            type,
            total,
            accountReference: $"{type}-{customer.UserNumber}",
            notes: req.Notes);

        var item = payment.Items.FirstOrDefault();

        if (item is not null)
        {
            item.Quantity = req.Quantity;
            item.UnitPrice = unitPrice;
            item.UnitOfMeasure = unit;
            item.AccomodationId = accommodationId;
            item.Description =
                $"{type}: {req.Quantity} {unit} @ {FormatZar(unitPrice.Value)}/{unit}";

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
            total,
            currency = "ZAR"
        });
    }

    public record AmountPaymentRequest(string Service, decimal Amount, string? Notes);

    [HttpPost("payments/amount")]
    public async Task<IActionResult> CreateAmountPayment([FromBody] AmountPaymentRequest req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null || string.IsNullOrWhiteSpace(req.Service))
            return BadRequest(new { success = false, message = "Service is required." });

        if (req.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than R0,00." });

        var service = req.Service.Trim();
        PaymentType type;

        if (service.Equals("rent", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Rent;
        else if (service.Equals("refuse", StringComparison.OrdinalIgnoreCase) ||
                 service.Equals("refusecollection", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.RefuseCollection;
        else if (service.Equals("parking", StringComparison.OrdinalIgnoreCase))
            type = PaymentType.Parking;
        else if (service.Equals("electricity", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                success = false,
                message = "Use the utility payment endpoint for electricity so units and tariffs can be validated."
            });
        else if (service.Equals("water", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                success = false,
                message = "Use the utility payment endpoint for water so units and tariffs can be validated."
            });
        else if (!Enum.TryParse(service.Replace(" ", ""), true, out type))
            return BadRequest(new
            {
                success = false,
                message = "Unknown service type. Use Rent, Refuse or Parking."
            });

        var amount = Math.Round(req.Amount, 2, MidpointRounding.AwayFromZero);

        var payment = await checkout.CreateCheckoutAsync(
            customer.Id,
            type,
            amount,
            accountReference: $"{type}-{customer.UserNumber}",
            notes: req.Notes);

        return Ok(new
        {
            success = true,
            paymentId = payment.Id,
            reference = payment.TransactionReference,
            transactionReference = payment.TransactionReference,
            amount = payment.Amount,
            status = payment.Status.ToString(),
            type = payment.PaymentType.ToString(),
            currency = "ZAR",
            message = "Payment submitted and is pending review."
        });
    }

    // ---------- Vehicles ----------

    [HttpGet("vehicles")]
    public async Task<IActionResult> Vehicles()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
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
            nextFee = count == 0 ? _fees.FirstVehicleFee : (_fees.SubsequentVehicleFee ?? 0m),
            currency = "ZAR",
            vehicles = vehicles.Select((v, i) => new
            {
                id = v.Id,
                registrationNumber = v.RegistrationNumber,
                make = v.Make,
                model = v.Model,
                colour = v.Colour,
                position = i + 1,
                positionLabel = i == 0 ? "First vehicle"
                    : i == 1 ? "Second vehicle"
                    : $"Vehicle #{i + 1}",
                registeredAtUtc = v.RegisteredAtUtc
            })
        });
    }

    [HttpPost("vehicles")]
    public async Task<IActionResult> RegisterVehicle([FromBody] VehicleRegistrationInput? req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null || !ModelState.IsValid)
            return BadRequest(new { success = false, message = "Check the vehicle details and field lengths." });

        var reg = req.RegistrationNumber.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(reg))
            return BadRequest(new { success = false, message = "Registration number is required." });

        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var existingVehicle = await context.Vehicles
                .SingleOrDefaultAsync(v => v.RegistrationNumber == reg);

            if (existingVehicle is { IsActive: true })
                return Conflict(new { success = false, message = "Vehicle registration already exists." });

            if (existingVehicle is not null && existingVehicle.CustomerId != customer.Id)
                return Conflict(new { success = false, message = "This vehicle is registered to another customer. Contact administration if ownership has changed." });

            var existingCount = await context.Vehicles.CountAsync(
                v => v.CustomerId == customer.Id && v.IsActive);

            var position = existingCount + 1;
            var fee = position == 1
                ? _fees.FirstVehicleFee
                : (_fees.SubsequentVehicleFee ?? 0m);
            var reactivated = existingVehicle is not null;

            var vehicle = existingVehicle ?? new Vehicle
            {
                CustomerId = customer.Id,
                RegistrationNumber = reg
            };

            vehicle.Make = req.Make?.Trim();
            vehicle.Model = req.Model?.Trim();
            vehicle.Colour = req.Colour?.Trim();
            vehicle.RegisteredAtUtc = DateTime.UtcNow;
            vehicle.IsActive = true;

            if (!reactivated)
                context.Vehicles.Add(vehicle);

            await context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                success = true,
                vehicleId = vehicle.Id,
                registrationNumber = vehicle.RegistrationNumber,
                position,
                positionLabel = position == 1 ? "First vehicle"
                    : position == 2 ? "Second vehicle"
                    : $"Vehicle #{position}",
                fee,
                currency = "ZAR",
                paymentRequired = fee > 0,
                reactivated,
                message = fee > 0
                    ? $"Vehicle #{position} {(reactivated ? "reactivated" : "registered")}. Applicable fee: {FormatZar(fee)}."
                    : reactivated ? "Vehicle reactivated. Registration fee: R0,00." : "Vehicle registered. Registration fee: R0,00."
            });
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Mobile vehicle registration could not be saved for customer {CustomerId} and registration {RegistrationNumber}.", customer.Id, reg);
            return StatusCode(500, new
            {
                success = false,
                message = "The vehicle could not be saved. It may have just been registered already; refresh and try again."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected mobile vehicle registration failure for customer {CustomerId} and registration {RegistrationNumber}.", customer.Id, reg);
            return StatusCode(500, new
            {
                success = false,
                message = "Vehicle registration is temporarily unavailable. Please try again shortly."
            });
        }
    }

    // ---------- Parking permits ----------

    [HttpGet("permits")]
    public async Task<IActionResult> Permits()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
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
            currency = "ZAR",
            permits = permits.Select(p => new
            {
                id = p.Id,
                permitNumber = p.PermitNumber,
                permitType = p.PermitType,
                requiresAccessibleParking = p.RequiresAccessibleParking,
                status = p.Status.ToString(),
                fee = p.Fee,
                vehicleRegistration = p.Vehicle?.RegistrationNumber,
                appliedAtUtc = p.AppliedAtUtc,
                issueDate = p.IssueDate,
                expiryDate = p.ExpiryDate
            })
        });
    }

    public record ApplyPermitRequest(int VehicleId, string? PermitType, bool RequiresAccessibleParking = false);

    [HttpPost("permits")]
    public async Task<IActionResult> ApplyPermit([FromBody] ApplyPermitRequest req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null)
            return BadRequest(new { success = false, message = "Permit request is required." });

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v =>
            v.Id == req.VehicleId &&
            v.CustomerId == customer.Id &&
            v.IsActive);

        if (vehicle is null)
            return BadRequest(new { success = false, message = "Select a valid registered vehicle." });

        var pendingOrActive = await context.ParkingPermits.AnyAsync(p =>
            p.VehicleId == req.VehicleId &&
            (p.Status == ParkingPermitStatus.Active ||
             p.Status == ParkingPermitStatus.PendingApproval));

        if (pendingOrActive)
            return Conflict(new
            {
                success = false,
                message = "This vehicle already has an active or pending permit."
            });

        var number = $"AP-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(10000, 99999)}";

        var permit = new ParkingPermit
        {
            PermitNumber = number,
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            PermitType = string.IsNullOrWhiteSpace(req.PermitType)
                ? "Student annual"
                : req.PermitType.Trim(),
            RequiresAccessibleParking = req.RequiresAccessibleParking,
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
            requiresAccessibleParking = permit.RequiresAccessibleParking,
            fee = 0m,
            amountPayable = 0m,
            currency = "ZAR",
            status = permit.Status.ToString(),
            message = "Permit application submitted free of charge (R0,00). Status: Pending approval."
        });
    }

    // ---------- Accommodations ----------

    [AllowAnonymous, HttpGet("accommodations")]
    public async Task<IActionResult> Accommodations(
        string? search = null,
        AccommodationType? type = null,
        string? roomType = null,
        bool availableOnly = false)
    {
        var query = context.Accomodations
            .Include(a => a.Images)
            .Include(a => a.Residents)
            .Where(a => a.IsActive)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Name.Contains(term) || a.Address.Contains(term) || a.UnitNumber.Contains(term));
        }
        if (type.HasValue)
            query = query.Where(a => a.AccommodationType == type.Value);
        if (!string.IsNullOrWhiteSpace(roomType) && Enum.TryParse<AccommodationRoomType>(roomType, true, out var parsedRoomType))
            query = query.Where(a => a.RoomType == parsedRoomType);

        var properties = await query.OrderBy(a => a.Name).ToListAsync();
        var list = properties.Select(a => new
        {
            id = a.Id,
            name = a.Name,
            unitNumber = a.UnitNumber,
            address = a.Address,
            type = a.AccommodationType.ToString(),
            roomType = a.RoomType?.ToString(),
            monthlyRent = a.MonthlyRent,
            capacity = a.Capacity,
            occupiedPlaces = a.Residents.Count,
            availablePlaces = Math.Max(0, a.Capacity - a.Residents.Count),
            description = a.Description,
            images = a.Images.OrderByDescending(i => i.IsPrimary).Select(i => new
            {
                imagePath = i.ImagePath,
                caption = i.Caption,
                isPrimary = i.IsPrimary
            }).ToList()
        }).Where(a => !availableOnly || a.availablePlaces > 0).ToList();

        return Ok(new { success = true, accommodations = list, items = list, data = list });
    }

    [AllowAnonymous, HttpGet("accommodations/{id:int}")]
    public async Task<IActionResult> AccommodationDetails(int id)
    {
        var accommodation = await context.Accomodations
            .Include(a => a.Images)
            .Include(a => a.Residents)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive);

        if (accommodation is null)
            return NotFound(new { success = false, message = "Accommodation not found." });

        return Ok(new
        {
            success = true,
            id = accommodation.Id,
            name = accommodation.Name,
            unitNumber = accommodation.UnitNumber,
            address = accommodation.Address,
            type = accommodation.AccommodationType.ToString(),
            roomType = accommodation.RoomType?.ToString(),
            monthlyRent = accommodation.MonthlyRent,
            capacity = accommodation.Capacity,
            occupiedPlaces = accommodation.Residents.Count,
            availablePlaces = Math.Max(0, accommodation.Capacity - accommodation.Residents.Count),
            description = accommodation.Description,
            images = accommodation.Images.OrderByDescending(i => i.IsPrimary).Select(i => new
            {
                imagePath = i.ImagePath,
                caption = i.Caption,
                isPrimary = i.IsPrimary
            }).ToList()
        });
    }

    public record AccommodationRequestBody(int AccommodationId, string? Note, bool RequiresAccessibleRoom = false);

    [HttpPost("accommodations/request")]
    public async Task<IActionResult> RequestAccommodation([FromBody] AccommodationRequestBody req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null)
            return BadRequest(new { success = false, message = "Accommodation request is required." });

        var accommodation = await context.Accomodations.FirstOrDefaultAsync(
            a => a.Id == req.AccommodationId && a.IsActive);

        if (accommodation is null)
            return BadRequest(new { success = false, message = "Select a valid accommodation." });

        if (customer.AccomodationId.HasValue)
            return Conflict(new { success = false, message = "You already have a residence assigned." });

        var pending = await context.AccommodationAssignmentRequests.AnyAsync(r =>
            r.CustomerId == customer.Id &&
            r.Status == AccommodationAssignmentStatus.Pending);

        if (pending)
            return Conflict(new
            {
                success = false,
                message = "You already have a pending accommodation request."
            });

        var occupiedPlaces = await context.Customers.CountAsync(c => c.AccomodationId == accommodation.Id);
        if (occupiedPlaces >= accommodation.Capacity)
            return Conflict(new { success = false, message = "This accommodation is no longer available." });

        var entity = new AccommodationAssignmentRequest
        {
            CustomerId = customer.Id,
            AccomodationId = accommodation.Id,
            Status = AccommodationAssignmentStatus.Pending,
            CustomerNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            RequiresAccessibleRoom = req.RequiresAccessibleRoom,
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
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
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
                requiresAccessibleRoom = r.RequiresAccessibleRoom,
                administratorNote = r.AdministratorNote,
                decisionMessage = r.Status == AccommodationAssignmentStatus.Approved
                    ? (r.AdministratorNote ?? ResidenceApprovalMessage)
                    : (r.Status == AccommodationAssignmentStatus.Rejected ||
                       r.Status == AccommodationAssignmentStatus.Cancelled
                        ? r.AdministratorNote
                        : null),
                requestedAtUtc = r.RequestedAtUtc,
                reviewedAtUtc = r.ReviewedAtUtc,
                accommodationId = r.AccomodationId,
                accommodationName = r.Accomodation != null ? r.Accomodation.Name : null,
                unitNumber = r.Accomodation != null ? r.Accomodation.UnitNumber : null,
                address = r.Accomodation != null ? r.Accomodation.Address : null
            })
            .ToListAsync();

        return Ok(new { success = true, requests = list, items = list, data = list });
    }

    // ---------- Admin: review accommodation requests ----------

    [HttpGet("accommodations/requests/all")]
    public async Task<IActionResult> AllAccommodationRequests()
    {
        var (user, _, role) = await ResolveUserAsync();

        if (user is null || !IsAdminRole(role))
            return Forbid();

        var list = await context.AccommodationAssignmentRequests
            .Include(r => r.Accomodation)
            .Include(r => r.Customer!).ThenInclude(c => c.AppUser)
            .OrderByDescending(r => r.RequestedAtUtc)
            .AsNoTracking()
            .Select(r => new
            {
                id = r.Id,
                status = r.Status.ToString(),
                customerNote = r.CustomerNote,
                requiresAccessibleRoom = r.RequiresAccessibleRoom,
                administratorNote = r.AdministratorNote,
                decisionMessage = r.Status == AccommodationAssignmentStatus.Approved
                    ? (r.AdministratorNote ?? ResidenceApprovalMessage)
                    : r.AdministratorNote,
                requestedAtUtc = r.RequestedAtUtc,
                reviewedAtUtc = r.ReviewedAtUtc,
                accommodationId = r.AccomodationId,
                accommodationName = r.Accomodation != null ? r.Accomodation.Name : null,
                unitNumber = r.Accomodation != null ? r.Accomodation.UnitNumber : null,
                address = r.Accomodation != null ? r.Accomodation.Address : null,
                customerName = r.Customer != null && r.Customer.AppUser != null
                    ? (r.Customer.AppUser.FirstName + " " + r.Customer.AppUser.LastName).Trim()
                    : null,
                userNumber = r.Customer != null ? r.Customer.UserNumber : null
            })
            .ToListAsync();

        return Ok(new { success = true, requests = list, items = list, data = list });
    }

    [HttpPost("accommodations/requests/{id:int}/approve")]
    public async Task<IActionResult> ApproveAccommodationRequest(int id)
    {
        var (user, _, role) = await ResolveUserAsync();

        if (user is null || !IsAdminRole(role))
            return Forbid();

        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = await context.AccommodationAssignmentRequests
            .Include(x => x.Customer)
            .Include(x => x.Accomodation!).ThenInclude(x => x.Residents)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (request?.Customer is null || request.Accomodation is null)
            return NotFound(new { success = false, message = "Request not found." });

        if (request.Status != AccommodationAssignmentStatus.Pending)
            return Conflict(new { success = false, message = "Request has already been reviewed." });

        if (request.Accomodation.Residents.Count >= request.Accomodation.Capacity)
            return BadRequest(new
            {
                success = false,
                message = "This accommodation has reached its capacity."
            });

        var now = DateTime.UtcNow;

        request.Status = AccommodationAssignmentStatus.Approved;
        request.AdministratorNote = ResidenceApprovalMessage;
        request.ReviewedAtUtc = now;
        request.Customer.AccomodationId = request.AccomodationId;

        context.Notifications.Add(new Notification
        {
            CustomerId = request.CustomerId,
            Title = "Residence application approved",
            Message = ResidenceApprovalMessage,
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            success = true,
            status = request.Status.ToString(),
            message = ResidenceApprovalMessage,
            administratorNote = request.AdministratorNote
        });
    }

    public record RejectAccommodationRequestBody(string Reason, bool Cancel = false);

    [HttpPost("accommodations/requests/{id:int}/reject")]
    public async Task<IActionResult> RejectAccommodationRequest(
        int id,
        [FromBody] RejectAccommodationRequestBody req)
    {
        var (user, _, role) = await ResolveUserAsync();

        if (user is null || !IsAdminRole(role))
            return Forbid();

        var reason = (req?.Reason ?? "").Trim();

        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new
            {
                success = false,
                message = "A rejection or cancellation reason is required."
            });

        await using var transaction = await context.Database.BeginTransactionAsync();

        var request = await context.AccommodationAssignmentRequests
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (request?.Customer is null)
            return NotFound(new { success = false, message = "Request not found." });

        if (request.Status != AccommodationAssignmentStatus.Pending)
            return Conflict(new { success = false, message = "Request has already been reviewed." });

        var now = DateTime.UtcNow;
        request.Status = req!.Cancel
            ? AccommodationAssignmentStatus.Cancelled
            : AccommodationAssignmentStatus.Rejected;
        request.AdministratorNote = reason;
        request.ReviewedAtUtc = now;

        context.Notifications.Add(new Notification
        {
            CustomerId = request.CustomerId,
            Title = req.Cancel
                ? "Residence application cancelled"
                : "Residence application rejected",
            Message = reason,
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            success = true,
            status = request.Status.ToString(),
            administratorNote = request.AdministratorNote,
            message = reason
        });
    }

    /// <summary>Customer cancels their own pending residence application.</summary>
    [HttpDelete("accommodations/requests/{id:int}")]
    public async Task<IActionResult> CancelMyAccommodationRequest(int id)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var request = await context.AccommodationAssignmentRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerId == customer.Id);

        if (request is null)
            return NotFound(new { success = false, message = "Request not found." });

        if (request.Status != AccommodationAssignmentStatus.Pending)
            return Conflict(new
            {
                success = false,
                message = "Only pending applications can be cancelled."
            });

        request.Status = AccommodationAssignmentStatus.Cancelled;
        request.AdministratorNote = "Cancelled by customer.";
        request.ReviewedAtUtc = DateTime.UtcNow;

        context.Notifications.Add(new Notification
        {
            CustomerId = customer.Id,
            Title = "Residence application cancelled",
            Message = "You cancelled your residence application.",
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            status = request.Status.ToString(),
            message = "Application cancelled."
        });
    }

    // ---------- Campus account ----------

    [HttpGet("campus-balance")]
    public async Task<IActionResult> CampusBalance()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        var balance = await campusAccounts.GetBalanceAsync(customer.Id);

        return Ok(new { success = true, balance, currency = "ZAR" });
    }

    public record FundCampusRequest(decimal Amount, string? FundingSource);

    [HttpPost("campus-balance/fund")]
    public async Task<IActionResult> FundCampus([FromBody] FundCampusRequest req)
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null || req.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than R0,00." });

        var (ok, message, transfer) = await campusAccounts.DepositAsync(
            customer.Id,
            Math.Round(req.Amount, 2, MidpointRounding.AwayFromZero),
            string.IsNullOrWhiteSpace(req.FundingSource) ? "Mobile app" : req.FundingSource.Trim());

        if (!ok)
            return BadRequest(new { success = false, message });

        var balance = await campusAccounts.GetBalanceAsync(customer.Id);

        return Ok(new
        {
            success = true,
            message,
            balance,
            currency = "ZAR",
            transferId = transfer?.Id
        });
    }

    // ---------- Issues ----------

    [HttpGet("issues")]
    public async Task<IActionResult> Issues()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
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
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (customer.AccomodationId is null)
            return BadRequest(new
            {
                success = false,
                message = "You must be assigned to accommodation before reporting issues."
            });

        if (req is null ||
            string.IsNullOrWhiteSpace(req.Title) ||
            string.IsNullOrWhiteSpace(req.Description))
        {
            return BadRequest(new
            {
                success = false,
                message = "Title and description are required."
            });
        }

        var category = AccommodationIssueCategory.Other;

        if (!string.IsNullOrWhiteSpace(req.Category) &&
            !Enum.TryParse(req.Category.Replace(" ", ""), true, out category))
        {
            return BadRequest(new { success = false, message = "Invalid issue category." });
        }

        var issue = new AccommodationIssue
        {
            CustomerId = customer.Id,
            AccomodationId = customer.AccomodationId.Value,
            Title = req.Title.Trim(),
            Description = req.Description.Trim(),
            Category = category,
            Status = AccommodationIssueStatus.Reported,
            ReportedAtUtc = DateTime.UtcNow
        };

        context.AccommodationIssues.Add(issue);
        await context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            issueId = issue.Id,
            status = issue.Status.ToString(),
            message = "Issue reported."
        });
    }

    // ---------- Reviews ----------

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews()
    {
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
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
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null || customer is null || role != SystemRoles.Customer)
            return Unauthorized(new { success = false, message = "Customer authentication required." });

        if (req is null || req.Rating < 1 || req.Rating > 5)
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
        var (user, customer, role) = await ResolveUserAsync();

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        IQueryable<Notification> query = context.Notifications.AsNoTracking();

        if (role == SystemRoles.Customer)
        {
            if (customer is null)
                return NotFound(new { success = false, message = "Customer profile not found." });

            query = query.Where(n => n.CustomerId == customer.Id);
        }
        else if (!IsStaffRole(role))
        {
            return Forbid();
        }

        var list = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(100)
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

    public record ChangePasswordRequest(
        string CurrentPassword,
        string NewPassword,
        string? ConfirmPassword);

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var (user, _, _) = await ResolveUserAsync();

        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (req is null ||
            string.IsNullOrWhiteSpace(req.CurrentPassword) ||
            string.IsNullOrWhiteSpace(req.NewPassword))
        {
            return BadRequest(new
            {
                success = false,
                message = "Current and new password are required."
            });
        }

        if (req.ConfirmPassword is not null && req.NewPassword != req.ConfirmPassword)
            return BadRequest(new { success = false, message = "Passwords do not match." });

        var result = await userManager.ChangePasswordAsync(
            user,
            req.CurrentPassword,
            req.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                success = false,
                message = string.Join(" ", result.Errors.Select(e => e.Description))
            });
        }

        return Ok(new { success = true, message = "Password changed." });
    }

    // ---------- Excel reports ----------

    /// <summary>
    /// Financial Officer and Administrator Excel download.
    /// Query parameters: from, to (yyyy-MM-dd), status, paymentType, customerId.
    /// </summary>
    [HttpGet("reports/excel")]
    public async Task<IActionResult> DownloadExcel(
        DateTime? from,
        DateTime? to,
        PaymentStatus? status,
        PaymentType? paymentType,
        int? customerId)
    {
        var (user, _, role) = await ResolveUserAsync();

        if (user is null || !IsStaffRole(role))
            return Forbid();

        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest(new
            {
                success = false,
                message = "The start date cannot be after the end date."
            });

        var filter = new FinancialReportFilter
        {
            FromUtc = from?.ToUniversalTime(),
            ToUtc = to.HasValue
                ? to.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime()
                : null,
            Status = status,
            PaymentType = paymentType,
            CustomerId = customerId
        };

        var bytes = await reports.GenerateExcelReportAsync(filter);
        var fileName = $"ArnoxPay_Financial_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }


    // ---------- Payment review (FO / Admin) — mirrors PaymentController.Review ----------

    public record PaymentReviewRequest(string Decision, string? OfficerReference);

    /// <summary>
    /// Accept or reject a pending payment. Same rules as the web FO review action.
    /// Decision: "Accepted" or "Rejected".
    /// </summary>
    [HttpPost("payments/{id:int}/review")]
    public async Task<IActionResult> ReviewPayment(int id, [FromBody] PaymentReviewRequest req)
    {
        var (user, _, role) = await ResolveUserAsync();
        if (user is null || !IsStaffRole(role))
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (req is null || string.IsNullOrWhiteSpace(req.Decision))
            return BadRequest(new { success = false, message = "Decision is required (Accepted or Rejected)." });

        var decision = req.Decision.Trim();
        if (!decision.Equals("Accepted", StringComparison.OrdinalIgnoreCase) &&
            !decision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Decision must be Accepted or Rejected." });

        var officer = await context.FinancialOfficers
            .FirstOrDefaultAsync(x => x.AppUserId == user.Id && x.IsAuthorized);

        if (officer is null && role != SystemRoles.Administrator)
            return Forbid();

        var payment = await context.Payments
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment is null)
            return NotFound(new { success = false, message = "Payment not found." });

        if (payment.Status != PaymentStatus.Pending)
            return BadRequest(new { success = false, message = "Only pending payments can be accepted or rejected." });

        var accepted = decision.Equals("Accepted", StringComparison.OrdinalIgnoreCase);
        payment.Status = accepted ? PaymentStatus.Accepted : PaymentStatus.Rejected;
        if (officer is not null) payment.FinancialOfficerId = officer.Id;
        if (!string.IsNullOrWhiteSpace(req.OfficerReference))
            payment.OfficerReference = req.OfficerReference.Trim();
        payment.ReviewedAtUtc = DateTime.UtcNow;
        payment.CompletedAtUtc = DateTime.UtcNow;

        if (accepted)
        {
            payment.FailureReason = null;
            foreach (var item in payment.Items) item.IsPaid = true;

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
            }

            context.Notifications.Add(new Notification
            {
                CustomerId = payment.CustomerId,
                Title = "Payment approved",
                Message = $"Your {payment.PaymentType} payment ({payment.TransactionReference}) has been approved successfully.",
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            payment.FailureReason = string.IsNullOrWhiteSpace(req.OfficerReference)
                ? "Payment was not accepted by the Financial Officer."
                : req.OfficerReference.Trim();
            context.Notifications.Add(new Notification
            {
                CustomerId = payment.CustomerId,
                Title = "Payment rejected",
                Message = $"Your {payment.PaymentType} payment ({payment.TransactionReference}) was not accepted. Reason: {payment.FailureReason}",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            message = accepted ? "Payment accepted." : "Payment rejected.",
            paymentId = payment.Id,
            status = payment.Status.ToString()
        });
    }

    // ---------- Announcements ----------

    [HttpGet("announcements")]
    public async Task<IActionResult> Announcements()
    {
        var (user, _, role) = await ResolveUserAsync();
        if (user is null)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        var query = context.Announcements.AsNoTracking()
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.PublishedAtUtc);

        // Audience filter: All, or role-specific if set
        var list = await query.Take(50).ToListAsync();
        var filtered = list.Where(a =>
            string.IsNullOrWhiteSpace(a.Audience) ||
            a.Audience.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            (role != null && a.Audience.Contains(role, StringComparison.OrdinalIgnoreCase))).ToList();

        return Ok(new
        {
            success = true,
            announcements = filtered.Select(a => new
            {
                id = a.Id,
                title = a.Title,
                body = a.Body,
                audience = a.Audience,
                priority = a.Priority,
                deadlineUtc = a.DeadlineUtc,
                publishedAtUtc = a.PublishedAtUtc
            })
        });
    }

    // ---------- Services catalogue (from FeeSettings — same source as web ServicesHub) ----------

    [HttpGet("services")]
    [AllowAnonymous]
    public IActionResult ServicesCatalogue()
    {
        return Ok(new
        {
            success = true,
            currency = "ZAR",
            services = new object[]
            {
                new { key = "Electricity", name = "Electricity", unit = "kWh", pricePerUnit = _fees.ElectricityPricePerKwh ?? 0m, maxUnits = _fees.MaxElectricityUnits },
                new { key = "Water", name = "Water", unit = "kL", pricePerUnit = _fees.WaterPricePerKl ?? 0m, maxUnits = _fees.MaxWaterUnits },
                new { key = "Refuse", name = "Refuse", unit = "month", pricePerUnit = 0m, maxUnits = 1 },
                new { key = "Rent", name = "Rent", unit = "month", pricePerUnit = 0m, maxUnits = 1 },
                new { key = "Parking", name = "Parking permit", unit = "application", pricePerUnit = _fees.ParkingPermitApplicationFee, maxUnits = 1 },
                new { key = "Vehicle", name = "Additional vehicle fee", unit = "vehicle", pricePerUnit = _fees.SubsequentVehicleFee ?? 0m, maxUnits = 10 }
            },
            fees = new
            {
                electricityPricePerKwh = _fees.ElectricityPricePerKwh,
                waterPricePerKl = _fees.WaterPricePerKl,
                firstVehicleFee = _fees.FirstVehicleFee,
                subsequentVehicleFee = _fees.SubsequentVehicleFee,
                parkingPermitApplicationFee = _fees.ParkingPermitApplicationFee
            }
        });
    }

    // ---------- Admin: customer list (read-only for mobile) ----------

    [HttpGet("admin/customers")]
    public async Task<IActionResult> AdminCustomers()
    {
        var (user, _, role) = await ResolveUserAsync();
        if (user is null || role != SystemRoles.Administrator)
            return Unauthorized(new { success = false, message = "Administrator authentication required." });

        var customers = await context.Customers
            .Include(c => c.AppUser)
            .AsNoTracking()
            .OrderBy(c => c.AppUser!.LastName)
            .Take(200)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            customers = customers.Select(c => new
            {
                id = c.Id,
                userNumber = c.UserNumber,
                category = c.Category.ToString(),
                name = c.AppUser is null ? null : $"{c.AppUser.FirstName} {c.AppUser.LastName}",
                email = c.AppUser?.Email,
                isActive = c.AppUser?.IsActive ?? false
            })
        });
    }

    // ---------- Admin aliases used by Android ----------

    [HttpGet("admin/assignment-requests")]
    public Task<IActionResult> AdminAssignmentRequestsAlias() => AllAccommodationRequests();

    [HttpPost("admin/assignment-requests/{id:int}/review")]
    public async Task<IActionResult> AdminReviewAssignment(int id, [FromBody] ReviewAssignmentBody body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Status))
            return BadRequest(new { success = false, message = "Status is required." });

        var status = body.Status.Trim();
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return await ApproveAccommodationRequest(id);
        }

        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return await RejectAccommodationRequest(id, new RejectAccommodationRequestBody(
                body.AdministratorNote ?? "Rejected by administrator.",
                status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));
        }

        return BadRequest(new { success = false, message = "Status must be Approved or Rejected." });
    }

    [HttpGet("admin/payments/audit")]
    public async Task<IActionResult> AdminPaymentsAudit()
    {
        var (user, _, role) = await ResolveUserAsync();
        if (user is null || (!IsAdminRole(role) && role != SystemRoles.FinancialOfficer))
            return Forbid();

        var list = await context.Payments
            .Include(p => p.Customer!).ThenInclude(c => c.AppUser)
            .OrderByDescending(p => p.SubmittedAtUtc)
            .Take(300)
            .AsNoTracking()
            .Select(p => new
            {
                id = p.Id,
                transactionReference = p.TransactionReference,
                amount = p.Amount,
                status = p.Status.ToString(),
                paymentType = p.PaymentType.ToString(),
                paymentMethod = p.PaymentMethod,
                submittedAtUtc = p.SubmittedAtUtc,
                reviewedAtUtc = p.ReviewedAtUtc,
                customerName = p.Customer != null && p.Customer.AppUser != null
                    ? (p.Customer.AppUser.FirstName + " " + p.Customer.AppUser.LastName).Trim()
                    : null,
                userNumber = p.Customer != null ? p.Customer.UserNumber : null,
                failureReason = p.FailureReason,
                officerReference = p.OfficerReference
            })
            .ToListAsync();

        return Ok(new { success = true, payments = list, items = list, data = list });
    }

    [HttpGet("admin/accommodations")]
    public async Task<IActionResult> AdminListAccommodations()
    {
        var (user, _, role) = await ResolveUserAsync();
        if (user is null || !IsAdminRole(role))
            return Forbid();

        var list = await context.Accomodations
            .OrderBy(a => a.Name)
            .AsNoTracking()
            .Select(a => new
            {
                id = a.Id,
                name = a.Name,
                unitNumber = a.UnitNumber,
                type = a.AccommodationType.ToString(),
                roomType = a.RoomType != null ? a.RoomType.ToString() : null,
                capacity = a.Capacity,
                monthlyRent = a.MonthlyRent,
                address = a.Address,
                isActive = a.IsActive,
                description = a.Description
            })
            .ToListAsync();

        return Ok(new { success = true, accommodations = list, items = list });
    }

    public sealed class ReviewAssignmentBody
    {
        public string Status { get; set; } = "";
        public string? AdministratorNote { get; set; }
    }

}
