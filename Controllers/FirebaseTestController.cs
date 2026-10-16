
using ArnoxPay.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ArnoxPay.Controllers
{
    public class FirebaseTestController : Controller
    {
        private readonly FirestoreService _firestoreService;

        public FirebaseTestController(FirestoreService firestoreService)
        {
            _firestoreService = firestoreService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var documents = await _firestoreService
                    .GetDocumentsAsync("accomodations");

                return Content(
                    "Firebase connected successfully! Documents found: "
                    + documents.Count);
            }
            catch (Exception ex)
            {
                return Content("Firebase connection failed: " + ex.Message);
            }
        }
    }
}
