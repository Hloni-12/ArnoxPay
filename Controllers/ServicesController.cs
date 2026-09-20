using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArnoxPay.Controllers;

/// <summary>
/// Customer payment services (not accommodation management).
/// </summary>
[Authorize(Roles = SystemRoles.Customer)]
public class ServicesController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Services";
        return View();
    }
}
