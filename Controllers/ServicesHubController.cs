using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArnoxPay.Controllers;

/// <summary>
/// Card-based service selection and dedicated purchase flows.
/// Multi-pay uses a session cart, then one combined checkout via ICheckoutService.
/// </summary>
[Authorize(Roles = SystemRoles.Customer)]
[Route("services")]
public class ServicesHubController(
    AppDbContext context,
    UserManager<AppUser> userManager,
    IServiceCartStore cart,
    ICheckoutService checkout,
    IOptions<FeeSettings> feeOptions) : Controller
{
    private readonly FeeSettings _fees = feeOptions.Value;

    private async Task<Customer?> CurrentCustomerAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return null;
        return await context.Customers
            .Include(c => c.Accomodation)
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id);
    }

    private string AccLabel(Customer c) =>
        c.Accomodation is null
            ? "No accommodation assigned"
            : $"{c.Accomodation.Name}, Unit {c.Accomodation.UnitNumber}";

    // ---------- Hub: clickable cards ----------

    [HttpGet("")]
    [HttpGet("hub")]
    public async Task<IActionResult> Hub()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var vehicles = await context.Vehicles.CountAsync(v => v.CustomerId == c.Id && v.IsActive);
        return View(new ServiceHubViewModel
        {
            AccommodationDisplay = AccLabel(c),
            VehicleCount = vehicles,
            CartCount = cart.Count(),
            CartTotal = cart.Total(),
            ElectricityTariff = _fees.ElectricityPricePerKwh,
            WaterTariff = _fees.WaterPricePerKl,
            SubsequentVehicleFee = _fees.SubsequentVehicleFee ?? 0m
        });
    }

    // ---------- Electricity ----------

    [HttpGet("electricity")]
    public async Task<IActionResult> Electricity()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.Accommodation = c.Accomodation;
        ViewBag.AccommodationLabel = AccLabel(c);
        ViewBag.UnitPrice = _fees.ElectricityPricePerKwh;
        ViewBag.MaxUnits = _fees.MaxElectricityUnits;
        ViewBag.UnitOfMeasure = "kWh";
        return View(new UtilityPurchaseForm { AccommodationId = c.AccomodationId });
    }

    [HttpPost("electricity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Electricity(UtilityPurchaseForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.Accommodation = c.Accomodation;
        ViewBag.AccommodationLabel = AccLabel(c);
        ViewBag.UnitPrice = _fees.ElectricityPricePerKwh;
        ViewBag.MaxUnits = _fees.MaxElectricityUnits;
        ViewBag.UnitOfMeasure = "kWh";

        if (_fees.ElectricityPricePerKwh is null)
        {
            ModelState.AddModelError(string.Empty, "Electricity tariff is not configured. Contact administration.");
            return View(form);
        }
        if (form.Quantity <= 0 || form.Quantity > _fees.MaxElectricityUnits)
        {
            ModelState.AddModelError(nameof(form.Quantity), $"Enter between 0.01 and {_fees.MaxElectricityUnits} kWh.");
            return View(form);
        }
        if (c.AccomodationId is null)
        {
            ModelState.AddModelError(string.Empty, "You need an assigned accommodation to purchase electricity.");
            return View(form);
        }

        var unitPrice = _fees.ElectricityPricePerKwh.Value;
        var total = Math.Round(form.Quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        var period = DateTime.UtcNow.ToString("yyyyMM");
        var line = new CartLine
        {
            PaymentType = PaymentType.Electricity,
            Title = "Electricity",
            Subtitle = AccLabel(c),
            Amount = total,
            Quantity = form.Quantity,
            UnitPrice = unitPrice,
            UnitOfMeasure = "kWh",
            AccommodationId = c.AccomodationId,
            AccommodationLabel = AccLabel(c),
            OutstandingChargeKey = $"elec-{c.Id}-{period}-{Guid.NewGuid():N}"[..40],
            Notes = form.Notes
        };
        cart.Add(line);
        TempData["Success"] = $"Electricity added: {form.Quantity:0.####} kWh · {total:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Water ----------

    [HttpGet("water")]
    public async Task<IActionResult> Water()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.Accommodation = c.Accomodation;
        ViewBag.AccommodationLabel = AccLabel(c);
        ViewBag.UnitPrice = _fees.WaterPricePerKl;
        ViewBag.MaxUnits = _fees.MaxWaterUnits;
        ViewBag.UnitOfMeasure = "kL";
        return View(new UtilityPurchaseForm { AccommodationId = c.AccomodationId });
    }

    [HttpPost("water")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Water(UtilityPurchaseForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.Accommodation = c.Accomodation;
        ViewBag.AccommodationLabel = AccLabel(c);
        ViewBag.UnitPrice = _fees.WaterPricePerKl;
        ViewBag.MaxUnits = _fees.MaxWaterUnits;
        ViewBag.UnitOfMeasure = "kL";

        if (_fees.WaterPricePerKl is null)
        {
            ModelState.AddModelError(string.Empty, "Water tariff is not configured. Contact administration.");
            return View(form);
        }
        if (form.Quantity <= 0 || form.Quantity > _fees.MaxWaterUnits)
        {
            ModelState.AddModelError(nameof(form.Quantity), $"Enter between 0.01 and {_fees.MaxWaterUnits} kL.");
            return View(form);
        }
        if (c.AccomodationId is null)
        {
            ModelState.AddModelError(string.Empty, "You need an assigned accommodation to purchase water.");
            return View(form);
        }

        var unitPrice = _fees.WaterPricePerKl.Value;
        var total = Math.Round(form.Quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        var period = DateTime.UtcNow.ToString("yyyyMM");
        cart.Add(new CartLine
        {
            PaymentType = PaymentType.Water,
            Title = "Water",
            Subtitle = AccLabel(c),
            Amount = total,
            Quantity = form.Quantity,
            UnitPrice = unitPrice,
            UnitOfMeasure = "kL",
            AccommodationId = c.AccomodationId,
            AccommodationLabel = AccLabel(c),
            OutstandingChargeKey = $"water-{c.Id}-{period}-{Guid.NewGuid():N}"[..40],
            Notes = form.Notes
        });
        TempData["Success"] = $"Water added: {form.Quantity:0.####} kL · {total:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Rent ----------

    [HttpGet("rent")]
    public async Task<IActionResult> Rent()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.AccommodationLabel = AccLabel(c);
        var period = DateTime.UtcNow.ToString("yyyy-MM");
        return View(new RentPaymentForm
        {
            AccommodationId = c.AccomodationId,
            BillingPeriod = period,
            Amount = 0m
        });
    }

    [HttpPost("rent")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rent(RentPaymentForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.AccommodationLabel = AccLabel(c);
        if (form.Amount <= 0)
        {
            ModelState.AddModelError(nameof(form.Amount), "Enter the rent amount due (greater than R0).");
            return View(form);
        }
        var period = string.IsNullOrWhiteSpace(form.BillingPeriod)
            ? DateTime.UtcNow.ToString("yyyy-MM")
            : form.BillingPeriod.Trim();
        cart.Add(new CartLine
        {
            PaymentType = PaymentType.Rent,
            Title = "Rent",
            Subtitle = $"{AccLabel(c)} · Period {period}",
            Amount = Math.Round(form.Amount, 2, MidpointRounding.AwayFromZero),
            AccommodationId = c.AccomodationId,
            AccommodationLabel = AccLabel(c),
            OutstandingChargeKey = $"rent-{c.Id}-{period}",
            Notes = form.Notes
        });
        TempData["Success"] = $"Rent for {period} added · {form.Amount:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Refuse ----------

    [HttpGet("refuse")]
    public async Task<IActionResult> Refuse()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.AccommodationLabel = AccLabel(c);
        return View(new RentPaymentForm { AccommodationId = c.AccomodationId, Amount = 0m });
    }

    [HttpPost("refuse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refuse(RentPaymentForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        ViewBag.AccommodationLabel = AccLabel(c);
        if (form.Amount <= 0)
        {
            ModelState.AddModelError(nameof(form.Amount), "Enter the refuse amount (greater than R0).");
            return View(form);
        }
        var period = DateTime.UtcNow.ToString("yyyyMM");
        cart.Add(new CartLine
        {
            PaymentType = PaymentType.RefuseCollection,
            Title = "Refuse collection",
            Subtitle = AccLabel(c),
            Amount = Math.Round(form.Amount, 2, MidpointRounding.AwayFromZero),
            AccommodationId = c.AccomodationId,
            AccommodationLabel = AccLabel(c),
            OutstandingChargeKey = $"refuse-{c.Id}-{period}",
            Notes = form.Notes
        });
        TempData["Success"] = $"Refuse charge added · {form.Amount:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Parking (by vehicle) ----------

    [HttpGet("parking")]
    public async Task<IActionResult> Parking()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var vehicles = await context.Vehicles
            .Where(v => v.CustomerId == c.Id && v.IsActive)
            .OrderBy(v => v.RegisteredAtUtc)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.Vehicles = vehicles;
        ViewBag.SubsequentFee = _fees.SubsequentVehicleFee ?? 0m;
        ViewBag.FirstFee = _fees.FirstVehicleFee;
        return View(new ParkingPaymentForm());
    }

    [HttpPost("parking")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Parking(ParkingPaymentForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var vehicles = await context.Vehicles
            .Where(v => v.CustomerId == c.Id && v.IsActive)
            .OrderBy(v => v.RegisteredAtUtc)
            .ToListAsync();
        ViewBag.Vehicles = vehicles;
        ViewBag.SubsequentFee = _fees.SubsequentVehicleFee ?? 0m;
        ViewBag.FirstFee = _fees.FirstVehicleFee;

        var vehicle = vehicles.FirstOrDefault(v => v.Id == form.VehicleId);
        if (vehicle is null)
        {
            ModelState.AddModelError(nameof(form.VehicleId), "Select one of your registered vehicles.");
            return View(form);
        }
        if (form.Amount <= 0)
        {
            ModelState.AddModelError(nameof(form.Amount), "Enter the parking amount greater than R0.");
            return View(form);
        }
        var position = vehicles.FindIndex(v => v.Id == vehicle.Id) + 1;
        var period = DateTime.UtcNow.ToString("yyyyMM");
        cart.Add(new CartLine
        {
            PaymentType = PaymentType.Parking,
            Title = "Parking",
            Subtitle = $"{vehicle.RegistrationNumber} · {(position == 1 ? "First vehicle" : position == 2 ? "Second vehicle" : $"Vehicle #{position}")}",
            Amount = Math.Round(form.Amount, 2, MidpointRounding.AwayFromZero),
            VehicleId = vehicle.Id,
            VehicleRegistration = vehicle.RegistrationNumber,
            VehiclePosition = position,
            OutstandingChargeKey = $"park-{vehicle.Id}-{period}",
            Notes = form.Notes
        });
        TempData["Success"] = $"Parking for {vehicle.RegistrationNumber} added · {form.Amount:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Subsequent vehicle registration fee ----------

    [HttpGet("vehicle-fee")]
    public async Task<IActionResult> VehicleFee()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var vehicles = await context.Vehicles
            .Where(v => v.CustomerId == c.Id && v.IsActive)
            .OrderBy(v => v.RegisteredAtUtc)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.Vehicles = vehicles;
        ViewBag.SubsequentFee = _fees.SubsequentVehicleFee;
        ViewBag.FirstFee = _fees.FirstVehicleFee;
        return View(new VehicleFeePaymentForm());
    }

    [HttpPost("vehicle-fee")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VehicleFee(VehicleFeePaymentForm form, string action)
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var vehicles = await context.Vehicles
            .Where(v => v.CustomerId == c.Id && v.IsActive)
            .OrderBy(v => v.RegisteredAtUtc)
            .ToListAsync();
        ViewBag.Vehicles = vehicles;
        ViewBag.SubsequentFee = _fees.SubsequentVehicleFee;
        ViewBag.FirstFee = _fees.FirstVehicleFee;

        var vehicle = vehicles.FirstOrDefault(v => v.Id == form.VehicleId);
        if (vehicle is null)
        {
            ModelState.AddModelError(nameof(form.VehicleId), "Select a vehicle.");
            return View(form);
        }
        var position = vehicles.FindIndex(v => v.Id == vehicle.Id) + 1;
        if (position <= 1)
        {
            ModelState.AddModelError(string.Empty, "The first registered vehicle has no additional registration fee (R0.00).");
            return View(form);
        }
        if (_fees.SubsequentVehicleFee is null)
        {
            ModelState.AddModelError(string.Empty, "Subsequent vehicle fee is not configured. Contact administration.");
            return View(form);
        }
        var fee = _fees.SubsequentVehicleFee.Value;
        if (fee <= 0)
        {
            ModelState.AddModelError(string.Empty, "No subsequent vehicle fee is due.");
            return View(form);
        }
        var period = DateTime.UtcNow.ToString("yyyyMM");
        cart.Add(new CartLine
        {
            PaymentType = PaymentType.Parking,
            Title = "Vehicle registration fee",
            Subtitle = $"{vehicle.RegistrationNumber} · {(position == 2 ? "Second vehicle" : $"Vehicle #{position}")}",
            Amount = fee,
            Quantity = 1,
            UnitPrice = fee,
            UnitOfMeasure = "fee",
            VehicleId = vehicle.Id,
            VehicleRegistration = vehicle.RegistrationNumber,
            VehiclePosition = position,
            OutstandingChargeKey = $"vfee-{vehicle.Id}-{period}",
            Notes = form.Notes
        });
        TempData["Success"] = $"Vehicle registration fee for {vehicle.RegistrationNumber} added · {fee:C}";
        return action == "checkout"
            ? RedirectToAction(nameof(Cart))
            : RedirectToAction(nameof(Hub));
    }

    // ---------- Cart review (card summaries, not a selection table) ----------

    [HttpGet("cart")]
    public IActionResult Cart()
    {
        var lines = cart.GetLines();
        ViewBag.Total = lines.Sum(x => x.Amount);
        return View(lines);
    }

    [HttpPost("cart/remove")]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveLine(string lineId)
    {
        cart.Remove(lineId);
        TempData["Success"] = "Item removed from checkout.";
        return RedirectToAction(nameof(Cart));
    }

    [HttpPost("cart/clear")]
    [ValidateAntiForgeryToken]
    public IActionResult ClearCart()
    {
        cart.Clear();
        TempData["Success"] = "Checkout cleared.";
        return RedirectToAction(nameof(Hub));
    }

    [HttpPost("cart/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckoutCart()
    {
        var c = await CurrentCustomerAsync();
        if (c is null) return Challenge();
        var lines = cart.GetLines();
        if (lines.Count == 0)
        {
            TempData["Error"] = "Your checkout is empty. Choose a service first.";
            return RedirectToAction(nameof(Hub));
        }

        var items = lines.Select(l => new CheckoutLineItem
        {
            PaymentType = l.PaymentType,
            Amount = l.Amount,
            Description = string.IsNullOrWhiteSpace(l.Subtitle) ? l.Title : $"{l.Title} — {l.Subtitle}",
            VehicleId = l.VehicleId,
            VehicleRegistration = l.VehicleRegistration,
            OutstandingChargeKey = l.OutstandingChargeKey,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            UnitOfMeasure = l.UnitOfMeasure,
            AccommodationId = l.AccommodationId
        }).ToList();

        try
        {
            var idem = $"cart-{c.Id}-{string.Join("-", lines.Select(x => x.LineId))}";
            if (idem.Length > 64) idem = idem[..64];
            var payment = await checkout.CreateMultiCheckoutAsync(c.Id, items, null, idem);
            cart.Clear();
            return RedirectToAction("Details", "Payment", new { id = payment.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Cart));
        }
    }
}
