using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArnoxPay.Controllers;

/// <summary>
/// Customer payment services entry point.
/// Redirects to the card-based Services Hub (not a payment table).
/// </summary>
[Authorize(Roles = SystemRoles.Customer)]
public class ServicesController : Controller
{
    [HttpGet]
    public IActionResult Index()
        => RedirectToAction("Hub", "ServicesHub");
}
