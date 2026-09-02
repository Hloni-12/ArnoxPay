using ArnoxPay.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArnoxPay.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole(SystemRoles.Administrator)) return RedirectToAction("Index", "AdminHome");
            if (User.IsInRole(SystemRoles.FinancialOfficer)) return RedirectToAction("Dashboard", "Payment");
            if (User.IsInRole(SystemRoles.Customer)) return RedirectToAction("Index", "Customer");
        }
        return View();
    }

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        ViewData["Title"] = "Privacy Policy";
        return View();
    }

    [AllowAnonymous]
    public IActionResult Terms()
    {
        ViewData["Title"] = "Terms & Conditions";
        return View();
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
