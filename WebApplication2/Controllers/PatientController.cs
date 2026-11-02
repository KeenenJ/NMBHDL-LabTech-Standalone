using Microsoft.AspNetCore.Mvc;

namespace WebApplication2.Controllers
{
    public class PatientController: Controller
    {
        private readonly ILogger<PatientController> _logger;

        public PatientController(ILogger<PatientController> logger)
        {
            _logger = logger;
        }

        public IActionResult ViewTestRequests()
        {
            return PartialView("_ViewTestRequests");
        }

        public IActionResult MedicalHistory()
        {
            return PartialView("_MedicalHistory");
        }

        public IActionResult AddCondition()
        {
            return PartialView("_AddCondition");
        }

        public IActionResult ConsentManagement()
        {
            return PartialView("_ConsentManagement");
        }

        public IActionResult GrantAccess()
        {
            return PartialView("_GrantAccess");
        }

        public IActionResult PatientReports()
        {
            return PartialView("_PatientReports");
        }

        public IActionResult TrackTestResults()
        {
            return PartialView("_TrackTestresults");
        }

        public IActionResult DashboardContent()
        {
            return PartialView("_DashboardContent");
        }
    }
}
