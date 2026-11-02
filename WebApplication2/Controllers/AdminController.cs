using Microsoft.AspNetCore.Mvc;

namespace WebApplication2.Controllers
{
    public class AdminController: Controller
    {

        private readonly ILogger<AdminController> _logger;

        public AdminController(ILogger<AdminController> logger)
        {
            _logger = logger;
        }

        public IActionResult AddMedicalConditions()
        {
            return PartialView("_AddMedicalCOnditions");
        }

        public IActionResult ListMedicalConditions()
        {
            return PartialView("_ListMedicalCOnditions");
        }

        public IActionResult AddAllergies()
        {
            return PartialView("_AddAllergies");
        }

        public IActionResult ListAllergies()
        {
            return PartialView("_ListAllergies");
        }

        public IActionResult AddMedication()
        {
            return PartialView("_AddMedication");
        }

        public IActionResult ListMedication()
        {
            return PartialView("_ListMedication");
        }

        public IActionResult UpdateProfile()
        {
            return PartialView("_UpdateProfile");
        }

        public IActionResult ChangePassword()
        {
            return PartialView("_ChangePassword");
        }

        public IActionResult DashboardContent()
        {
            return PartialView("_DashboardContent");
        }
    }
}
