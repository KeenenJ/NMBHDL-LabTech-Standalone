using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NMBDHL.DataAccess.SQLDataBaseAccess;
using NMBDHL.Doctor.Model;
using NMBDHL.Doctor.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebApplication2.Controllers
{
    public class DoctorController : Controller
    {
        private readonly IDoctorRepository _doctorRepo;

        public DoctorController(IConfiguration config)
        {
            var db = new SQLDataBaseAccess(config);
            _doctorRepo = new DoctorRepository(db, config);
        }

        public IActionResult DoctorDashboard()
        {
            return View("~/Views/Home/DoctorDashboard.cshtml");
        }

        [HttpPost]
        public async Task<IActionResult> SubmitTestRequest(int patientId, List<string> testPanels, string urgency, string clinicalNotes, string barcode)
        {
            try
            {
                // Defaulting to DoctorID 1 for the session
                int doctorId = 1;

                // Convert the multiple selected panels into a single comma-separated string for the 'Type' column
                string combinedPanels = testPanels != null && testPanels.Any() ? string.Join(", ", testPanels) : "";

                // Send the new barcode parameter to the repository
                bool success = await _doctorRepo.CreateTestRequestAsync(patientId, doctorId, combinedPanels, urgency, clinicalNotes, barcode);

                if (success)
                    TempData["SuccessMessage"] = "Test request submitted successfully.";
                else
                    TempData["ErrorMessage"] = "Failed to submit test request.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred: " + ex.Message;
            }

            return RedirectToAction("DoctorDashboard");
        }

        [HttpPost]
        public async Task<IActionResult> CancelRequest(int requestId, string cancelReason)
        {
            try
            {
                // This calls the repository method
                await _doctorRepo.CancelTestRequestAsync(requestId, cancelReason);

                // Return a JSON success message to the frontend
                return Json(new { success = true, message = $"Order #{requestId} has been successfully revoked." });
            }
            catch (Exception ex)
            {
                // If SQL Server throws an error, send it back to the UI
                return Json(new { success = false, message = "Database Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> LoadDashboardSection(string section)
        {
            if (string.IsNullOrEmpty(section)) section = "alerts";
            section = section.ToLower();

            int doctorId = 1;

            switch (section)
            {
                case "alerts":
                    try
                    {
                        var metrics = await _doctorRepo.GetDashboardMetricsAsync(doctorId);
                        if (metrics != null)
                        {
                            ViewBag.TotalActiveRequests = metrics.TotalActiveRequests;
                            ViewBag.CriticalAlertsCount = metrics.CriticalAlertsCount;
                            ViewBag.PendingCollections = metrics.PendingCollections;
                            ViewBag.TurnaroundCompliance = metrics.TurnaroundCompliance;
                        }
                        else
                        {
                            ViewBag.TotalActiveRequests = 0;
                            ViewBag.CriticalAlertsCount = 0;
                            ViewBag.PendingCollections = 0;
                            ViewBag.TurnaroundCompliance = "0%";
                        }

                        var dbRecent = await _doctorRepo.GetRecentRequestsAsync(doctorId);
                        var dbAlerts = await _doctorRepo.GetAbnormalAlertsAsync(doctorId);

                        ViewBag.RecentRequests = dbRecent ?? new List<RecentRequestModel>();
                        return PartialView("~/Views/Doctor/_AbnormalAlertsPartial.cshtml", dbAlerts ?? new List<AbnormalAlertModel>());
                    }
                    catch
                    {
                        ViewBag.TotalActiveRequests = 0;
                        ViewBag.CriticalAlertsCount = 0;
                        ViewBag.PendingCollections = 0;
                        ViewBag.TurnaroundCompliance = "0%";
                        ViewBag.RecentRequests = new List<RecentRequestModel>();
                        return PartialView("~/Views/Doctor/_AbnormalAlertsPartial.cshtml", new List<AbnormalAlertModel>());
                    }

                case "managepatients":
                    try
                    {
                        var dbPatients = await _doctorRepo.GetManagePatientsAsync(doctorId);
                        return PartialView("~/Views/Doctor/_ManagePatientsPartial.cshtml", dbPatients ?? new List<PatientRecordModel>());
                    }
                    catch
                    {
                        return PartialView("~/Views/Doctor/_ManagePatientsPartial.cshtml", new List<PatientRecordModel>());
                    }

                case "new-request":
                case "createrequest":
                    try
                    {
                        var dbDropdown = await _doctorRepo.GetPatientsDropdownAsync();
                        if (dbDropdown != null)
                        {
                            // Explicitly map the database results to the model the HTML expects
                            ViewBag.PatientsList = dbDropdown.Select(x => new PatientDropdownModel
                            {
                                PatientID = (int)x.PatientID,
                                IdNumber = (string)x.IdNumber,
                                FullName = (string)x.FullName
                            }).ToList();
                        }
                        else
                        {
                            ViewBag.PatientsList = new List<PatientDropdownModel>();
                        }
                    }
                    catch
                    {
                        ViewBag.PatientsList = new List<PatientDropdownModel>();
                    }
                    return PartialView("~/Views/Doctor/_CreateRequestPartial.cshtml");

                case "trackprogress":
                    try
                    {
                        var dbProgress = await _doctorRepo.GetTrackProgressAsync(doctorId);
                        return PartialView("~/Views/Doctor/_TrackProgressPartial.cshtml", dbProgress ?? new List<TrackProgressModel>());
                    }
                    catch
                    {
                        return PartialView("~/Views/Doctor/_TrackProgressPartial.cshtml", new List<TrackProgressModel>());
                    }

                case "reports":
                    return PartialView("~/Views/Doctor/_ReportsPortalPartial.cshtml");

                case "profilesettings":
                    return PartialView("~/Views/Doctor/_ProfileSettingsPartial.cshtml");

                default:
                    return PartialView("~/Views/Doctor/_AbnormalAlertsPartial.cshtml");
            }
        }
    }
}