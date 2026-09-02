using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArnoxPay.Controllers;

[Authorize(Roles = SystemRoles.Administrator)]
public class AdminParkingController(AppDbContext context, INotificationService notifications) : Controller
{
    public async Task<IActionResult> Index()
    {
        var areas = await context.ParkingAreas.Include(x => x.Bays).AsNoTracking().ToListAsync();
        ViewBag.TotalBays = await context.ParkingBays.CountAsync();
        ViewBag.Available = await context.ParkingBays.CountAsync(x => x.Status == ParkingBayStatus.Available);
        ViewBag.Occupied = await context.ParkingBays.CountAsync(x => x.Status == ParkingBayStatus.Occupied);
        ViewBag.PendingPermits = await context.ParkingPermits.CountAsync(x => x.Status == ParkingPermitStatus.PendingApproval);
        return View(areas);
    }

    [HttpGet]
    public IActionResult AddArea() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddArea(string name, string? location, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || capacity < 1)
        {
            TempData["Error"] = "Name and capacity (at least 1) are required.";
            return View();
        }
        var area = new ParkingArea {
            Name = name.Trim(), Location = location?.Trim(), Capacity = capacity, Status = ParkingAreaStatus.Open
        }
        ;
        context.ParkingAreas.Add(area);
        await context.SaveChangesAsync();

        // Create bays 01..capacity as Available
        for (int i = 1; i <= capacity; i++)
        {
            context.ParkingBays.Add(new ParkingBay
            {
                ParkingAreaId = area.Id,
                BayNumber = i.ToString("00"),
                BayType = "Standard",
                Status = ParkingBayStatus.Available
            }
            );
        }
        await context.SaveChangesAsync();
        TempData["Success"] = $"Parking area '{area.Name}' created with {capacity} bays.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Permits()
    {
        var list = await context.ParkingPermits
        .Include(x => x.Customer!).ThenInclude(x => x.AppUser)
        .Include(x => x.Vehicle)
        .OrderByDescending(x => x.AppliedAtUtc)
        .AsNoTracking()
        .ToListAsync();
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewPermit(int id, string decision, string? note)
    {
        var permit = await context.ParkingPermits.FindAsync(id);
        if (permit is null) return NotFound();
        if (permit.Status != ParkingPermitStatus.PendingApproval)
        {
            TempData["Error"] = "Permit is not pending approval.";
            return RedirectToAction(nameof(Permits));
        }
        if (string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase))
        {
            permit.Status = ParkingPermitStatus.Active;
            permit.IssueDate = DateTime.UtcNow.Date;
            if (permit.ExpiryDate < permit.IssueDate) permit.ExpiryDate = permit.IssueDate.AddYears(1);
        } else
        {
            permit.Status = ParkingPermitStatus.Rejected;
        }
        permit.ReviewedAtUtc = DateTime.UtcNow;
        permit.ReviewNote = note;
        await context.SaveChangesAsync();
        await notifications.CreateAsync(permit.CustomerId, $"Parking permit {permit.Status}",
        $"Your parking permit {permit.PermitNumber} was {permit.Status.ToString().ToLowerInvariant()}.");
        TempData["Success"] = $"Permit {permit.PermitNumber} marked {permit.Status}.";
        return RedirectToAction(nameof(Permits));
    }
}
