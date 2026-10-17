using System.Net;
using System.Net.Mail;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace ArnoxPay.Controllers;

[AllowAnonymous]
public class ContactController(UserManager<AppUser> users, IOptions<NotificationSettings> options, ILogger<ContactController> logger) : Controller
{
 [HttpGet] public IActionResult Index()=>View(new ContactStaffModel());
 [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Index(ContactStaffModel model)
 {
  if(model.RecipientRole is not (SystemRoles.Administrator or SystemRoles.FinancialOfficer)) ModelState.AddModelError(nameof(model.RecipientRole),"Select a valid staff recipient.");
  if(!ModelState.IsValid)return View(model);
  var staff=await users.GetUsersInRoleAsync(model.RecipientRole);
  var recipient=staff.Select(x=>x.Email).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
  var cfg=options.Value;
  if(recipient is null || !cfg.EmailEnabled || string.IsNullOrWhiteSpace(cfg.SmtpHost)||string.IsNullOrWhiteSpace(cfg.FromEmail)||string.IsNullOrWhiteSpace(cfg.SmtpUser)||string.IsNullOrWhiteSpace(cfg.SmtpPassword))
  { logger.LogError("Contact mail unavailable: missing recipient or SMTP configuration for role {Role}",model.RecipientRole); ModelState.AddModelError(string.Empty,"Email delivery is currently unavailable. Please try again later or contact the office by phone."); return View(model); }
  try {
   using var client=new SmtpClient(cfg.SmtpHost,cfg.SmtpPort){EnableSsl=cfg.SmtpUseSsl,Credentials=new NetworkCredential(cfg.SmtpUser,cfg.SmtpPassword),DeliveryMethod=SmtpDeliveryMethod.Network};
   using var mail=new MailMessage {From=new MailAddress(cfg.FromEmail,cfg.FromDisplayName),Subject="[ArnoxPay Contact] "+model.Subject.Trim(),Body=$"From: {model.Name.Trim()} <{model.Email.Trim()}>\nRecipient role: {model.RecipientRole}\n\n{model.Message.Trim()}",IsBodyHtml=false};
   mail.To.Add(recipient); mail.ReplyToList.Add(new MailAddress(model.Email.Trim(),model.Name.Trim())); await client.SendMailAsync(mail);
   TempData["Success"]="Your message has been sent."; return RedirectToAction(nameof(Index));
  } catch(Exception ex) {logger.LogError(ex,"Contact form delivery failed for role {Role}",model.RecipientRole); ModelState.AddModelError(string.Empty,"Your message could not be delivered. Please try again later.");return View(model);}
 }
}
