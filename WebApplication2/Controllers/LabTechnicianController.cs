using Dapper;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using NMBDHL.Auth.Repository;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using NMBDHL.Auth.Model;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NMBHDL.LabTechnician.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebApplication2.Models;
using WebApplication2.Services;
using static WebApplication2.Controllers.StatUrgentTestViewModel;

namespace WebApplication2.Controllers
{
    // View models used by the Lab Technician pages and forms
    public sealed class ReauthenticateViewModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Password { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string ReturnAction { get; set; } = string.Empty;
    }


    public sealed class EditLabTechnicianProfileViewModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string Surname { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = string.Empty;

        [Phone, StringLength(30)]
        public string? ContactNumber { get; set; }
    }

    public sealed class ChangeLabTechnicianPasswordViewModel
    {
        [Required, DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(128),
         DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, Compare(nameof(NewPassword)),
         DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class LabTechnicianProfileViewModel
    {
        public int UserID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string FullName => $"{Name} {Surname}".Trim();
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Role { get; set; } = "Lab Technician";
        public bool IsActive { get; set; }
    }


    public sealed class TatPerformanceReportViewModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        public int TotalTests { get; set; }
        public int CompletedTests { get; set; }
        public double CompliancePercent { get; set; }
        public double? AverageTatMinutes { get; set; }
        public int BreachCount { get; set; }
        public int AtRiskCount { get; set; }

        public List<TatPerformanceReportRow> Rows { get; set; } = new();
    }

    public sealed class LabTechReportExporter
    {
        public int LabTechnicianID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
    }

    public sealed class TatPerformanceReportRow
    {
        public int TestTypeID { get; set; }
        public string TestName { get; set; } = string.Empty;
        public int? TargetMinutes { get; set; }
        public double? AverageMinutes { get; set; }
        public double CompliancePercent { get; set; }
        public int BreachCount { get; set; }
        public int AtRiskCount { get; set; }
        public int TotalTests { get; set; }
        public int CompletedTests { get; set; }
    }

    public sealed class StatUrgentTestViewModel
    {
        public int TestRequestID { get; set; }
        public int SampleReceiptID { get; set; }
        public int TestTypeID { get; set; }
        public int TestResultID { get; set; }
        public int PatientID { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public DateTime? RequestDate { get; set; }
        public string Urgency { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public int TurnAroundMinutes { get; set; }
        public DateTime? DateTimeReceived { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string PatientNumber { get; set; } = string.Empty;
        public string ResultStatus { get; set; } = string.Empty;
        public DateTime? CompletionDateTime { get; set; }
        public string LatestDecision { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public DateTime? DueDateTime { get; set; }
        public string TatStatus { get; set; } = string.Empty;
    }

    public sealed class TestDetailViewModel
    {
        public int TestRequestID { get; set; }
        public int SampleReceiptID { get; set; }
        public int PatientID { get; set; }
        public string RequestNumber => $"REQ-{TestRequestID:D5}";
        public string Barcode { get; set; } = string.Empty;
        public string SampleType { get; set; } = "Not specified";
        public DateTime? RequestDate { get; set; }
        public DateTime? DateTimeReceived { get; set; }
        public string Urgency { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string PatientNumber { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string DoctorNotes { get; set; } = string.Empty;
        public string RequestedTypeText { get; set; } = string.Empty;
        public List<string> RequestedTests { get; set; } = new();
        public bool HasActiveSample { get; set; }
        public bool IsRejectedSample { get; set; }
        public bool IsSelectedTest { get; set; }
        public bool HasResult { get; set; }
        public bool IsComplete { get; set; }
        public int TestTypeID { get; set; }
        public string SelectedTestName { get; set; } = string.Empty;
        public string StageMessage { get; set; } = string.Empty;
        public List<string> MedicalConditions { get; set; } = new();
        public List<string> Allergies { get; set; } = new();
    }

    public class LabTechnicianController : Controller
    {
        private readonly LabTechnicianDataService _labTechnicianData;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LabTechnicianController> _logger;
        private readonly LabTechEmailService _labTechEmailService;
        private readonly LabTechReportPdf _labTechReportPdf;
        private readonly IDataProtector _reauthProtector;
        private readonly IAuthRepository _authRepository;

        private const string ReauthCookieName = ".LabTech.Reauth";
        private const int ReauthMinutes = 5;


        public LabTechnicianController(
            LabTechnicianDataService labTechnicianData,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<LabTechnicianController> logger,
            LabTechEmailService labTechEmailService,
            LabTechReportPdf labTechReportPdf,
            IDataProtectionProvider dataProtectionProvider,
            IAuthRepository authRepository)
        {
            _labTechnicianData = labTechnicianData;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
            _labTechEmailService = labTechEmailService;
            _labTechReportPdf = labTechReportPdf;
            _reauthProtector = dataProtectionProvider.CreateProtector("NMBDHL.LabTechnician.ProfileReauthentication.v1");
            _authRepository = authRepository;
        }


        // Dashboard

        [HttpGet]
        public async Task<IActionResult> Dashboard(
            CancellationToken cancellationToken)
        {
            try
            {
                var connectionString =
                    _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "The DefaultConnection connection string is missing.");
                }

                // Load dashboard totals and the most urgent outstanding requests
                const string statsSql = """
                    SELECT
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestRequest tr
                            WHERE COALESCE(tr.Status, '') NOT IN
                                ('Cancelled', 'Canceled', 'Completed',
                                 'Awaiting Selection')
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM STRING_SPLIT(
                                      REPLACE(
                                          REPLACE(
                                              REPLACE(
                                                  REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                                  '+', ','
                                              ),
                                              CHAR(13), ','
                                          ),
                                          CHAR(10), ','
                                      ),
                                      ','
                                  ) requested
                                  INNER JOIN dbo.TestType tt
                                      ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                                  WHERE NOT EXISTS
                                  (
                                      SELECT 1
                                      FROM dbo.SampleReceipt sr
                                      WHERE sr.TestRequestID = tr.TestRequestID
                                        AND sr.TestTypeID = tt.TestTypeID
                                        AND NOT EXISTS
                                        (
                                            SELECT 1
                                            FROM dbo.SampleReceiptRejection rejection
                                            WHERE rejection.SampleReceiptID = sr.SampleReceiptID
                                        )
                                  )
                              )
                        ) AS RequestsAwaitingReceipt,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestRequest tr
                            WHERE UPPER(LTRIM(RTRIM(
                                COALESCE(tr.Urgency, '')))) IN ('STAT', 'URGENT')
                              AND COALESCE(tr.Status, '') NOT IN
                                ('Cancelled', 'Canceled', 'Completed')
                        ) AS UrgentRequests,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestResult
                            WHERE CompletionDateTime >= CONVERT(date, GETDATE())
                              AND CompletionDateTime <
                                  DATEADD(day, 1, CONVERT(date, GETDATE()))
                        ) AS ResultsCompletedToday,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestResult tr
                            WHERE NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.ResultVerification rv
                                WHERE rv.TestResultID = tr.TestResultID
                                  AND UPPER(LTRIM(RTRIM(
                                      COALESCE(rv.Decision, '')))) = 'APPROVED'
                            )
                        ) AS AwaitingVerification;
                    """;

                const string urgentSql = """
                    SELECT TOP (6)
                        tr.TestRequestID,
                        COALESCE(tr.barcode, '') AS Barcode,
                        COALESCE(tr.Type, '') AS TestType,
                        COALESCE(tr.Urgency, '') AS Urgency,
                        COALESCE(tr.Status, '') AS Status,
                        tr.RequestDate,
                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name)))
                            AS PatientName
                    FROM dbo.TestRequest tr
                    INNER JOIN dbo.Patient p
                        ON p.PatientID = tr.PatientID
                    INNER JOIN dbo.[User] u
                        ON u.UserID = p.UserID
                    WHERE UPPER(LTRIM(RTRIM(
                        COALESCE(tr.Urgency, '')))) IN ('STAT', 'URGENT')
                      AND COALESCE(tr.Status, '') NOT IN
                        ('Cancelled', 'Canceled', 'Completed')
                    ORDER BY
                        CASE
                            WHEN UPPER(LTRIM(RTRIM(
                                COALESCE(tr.Urgency, '')))) = 'STAT'
                            THEN 0 ELSE 1
                        END,
                        tr.RequestDate ASC;
                    """;

                await using var connection =
                    new SqlConnection(connectionString);

                await connection.OpenAsync(cancellationToken);

                var stats = await connection.QuerySingleAsync<DashboardStats>(
                    new CommandDefinition(
                        statsSql,
                        cancellationToken: cancellationToken));

                var urgentRequests =
                    (await connection.QueryAsync<DashboardUrgentItem>(
                        new CommandDefinition(
                            urgentSql,
                            cancellationToken: cancellationToken)))
                    .ToList();

                var assignedTests = await _labTechnicianData
                    .GetAssignedTestsAsync(cancellationToken);

                var currentTechnicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (currentTechnicianId.HasValue)
                {
                    assignedTests = assignedTests
                        .Where(test => test.ReceivedByLabTechnicianID == 0 ||
                                       test.ReceivedByLabTechnicianID == currentTechnicianId.Value)
                        .ToList();
                }

                var assignedTestsCount = assignedTests.Count(test =>
                    !_labTechnicianData.IsTestCompleted(
                        test.SampleReceiptID,
                        test.TestTypeID));

                ViewBag.Stats = stats;
                ViewBag.AssignedTestsCount = assignedTestsCount;
                ViewBag.RequestsAwaitingReceipt = stats.RequestsAwaitingReceipt;
                ViewBag.UrgentRequestsCount = stats.UrgentRequests;
                ViewBag.ResultsCompletedToday = stats.ResultsCompletedToday;
                ViewBag.AwaitingVerification = stats.AwaitingVerification;
                ViewBag.urgentRequests = urgentRequests;
                ViewBag.UrgentRequests = urgentRequests;

                return View();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Dashboard failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load dashboard statistics. Check the application logs.";

                ViewBag.Stats = new DashboardStats();
                ViewBag.AssignedTestsCount = 0;
                ViewBag.RequestsAwaitingReceipt = 0;
                ViewBag.UrgentRequestsCount = 0;
                ViewBag.ResultsCompletedToday = 0;
                ViewBag.AwaitingVerification = 0;
                ViewBag.urgentRequests = new List<DashboardUrgentItem>();
                ViewBag.UrgentRequests = new List<DashboardUrgentItem>();

                return View();
            }
        }

        private sealed class DashboardStats
        {
            public int RequestsAwaitingReceipt { get; set; }
            public int UrgentRequests { get; set; }
            public int ResultsCompletedToday { get; set; }
            public int AwaitingVerification { get; set; }
        }

        private sealed class DashboardUrgentItem
        {
            public int TestRequestID { get; set; }
            public string Barcode { get; set; } = string.Empty;
            public string TestType { get; set; } = string.Empty;
            public string Urgency { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime? RequestDate { get; set; }
            public string PatientName { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> StatUrgent(
            CancellationToken cancellationToken)
        {
            try
            {
                var connectionString =
                    _configuration.GetConnectionString("DefaultConnection");

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "The DefaultConnection connection string is missing.");
                }

                // Retrieve urgent tests with their latest result, verification decision and turnaround status
                const string sql = """
                    SELECT
                        tr.TestRequestID,
                        COALESCE(sr.SampleReceiptID, 0) AS SampleReceiptID,
                        COALESCE(tt.TestTypeID, 0) AS TestTypeID,
                        COALESCE(sr.Barcode, tr.Barcode, '') AS Barcode,
                        tr.RequestDate,
                        COALESCE(tr.Urgency, '') AS Urgency,
                        COALESCE(tr.Status, '') AS RequestStatus,
                        COALESCE(tt.TestName, LTRIM(RTRIM(requested.value)), '') AS TestName,
                        COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,
                        sr.DateTimeReceived,
                        p.PatientID,
                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                        COALESCE(u.IDNumber, '') AS PatientNumber,
                        COALESCE(latestResult.TestResultID, 0) AS TestResultID,
                        COALESCE(latestResult.ResultStatus, '') AS ResultStatus,
                        latestResult.CompletionDateTime,
                        COALESCE(latestDecision.Decision, '') AS LatestDecision,
                        CASE
                            WHEN COALESCE(latestDecision.Decision, '') IN
                                ('Returned', 'Return', 'Rejected')
                                THEN 'Review'
                            WHEN COALESCE(latestDecision.Decision, '') IN
                                ('Approved', 'Verified')
                                THEN 'Complete'
                            WHEN latestResult.TestResultID IS NOT NULL
                                THEN 'Verification'
                            WHEN COALESCE(tr.Status, '') IN ('Completed', 'Verified')
                                THEN 'Complete'
                            WHEN COALESCE(tr.Status, '') IN ('In Progress', 'Processing')
                                THEN 'Processing'
                            WHEN sr.SampleReceiptID IS NOT NULL
                                THEN 'Sample Received'
                            ELSE 'Awaiting Sample'
                        END AS Stage
                    FROM dbo.TestRequest tr
                    INNER JOIN dbo.Patient p
                        ON p.PatientID = tr.PatientID
                    INNER JOIN dbo.[User] u
                        ON u.UserID = p.UserID
                    CROSS APPLY STRING_SPLIT(
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                    '+', ','
                                ),
                                CHAR(13), ','
                            ),
                            CHAR(10), ','
                        ),
                        ','
                    ) requested
                    LEFT JOIN dbo.TestType tt
                        ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                    LEFT JOIN dbo.SampleReceipt sr
                        ON sr.TestRequestID = tr.TestRequestID
                       AND (sr.TestTypeID IS NULL OR sr.TestTypeID = tt.TestTypeID)
                       AND NOT EXISTS
                       (
                           SELECT 1
                           FROM dbo.SampleReceiptRejection rejected
                           WHERE rejected.SampleReceiptID = sr.SampleReceiptID
                       )
                    OUTER APPLY
                    (
                        SELECT TOP (1)
                            result.TestResultID,
                            COALESCE(result.Status, '') AS ResultStatus,
                            result.CompletionDateTime
                        FROM dbo.TestResult result
                        WHERE result.TestRequestID = tr.TestRequestID
                          AND result.TestTypeID = tt.TestTypeID
                        ORDER BY result.CompletionDateTime DESC, result.TestResultID DESC
                    ) latestResult
                    OUTER APPLY
                    (
                        SELECT TOP (1)
                            verification.Decision
                        FROM dbo.ResultVerification verification
                        WHERE verification.TestResultID = latestResult.TestResultID
                        ORDER BY verification.VerificationDateTime DESC,
                                 verification.ResultVerificationID DESC
                    ) latestDecision
                    WHERE UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, ''))))
                        IN ('STAT', 'URGENT')
                      AND COALESCE(tr.Status, '') NOT IN
                        ('Cancelled', 'Canceled')
                    ORDER BY
                        CASE
                            WHEN UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, '')))) = 'STAT'
                                THEN 0
                            ELSE 1
                        END,
                        tr.RequestDate ASC,
                        tr.TestRequestID,
                        tt.TestName;
                    """;

                await using var connection = new SqlConnection(connectionString);

                var rows = (await connection.QueryAsync<StatUrgentTestViewModel>(
                    new CommandDefinition(
                        sql,
                        cancellationToken: cancellationToken))).ToList();

                var assignedTests = await _labTechnicianData
                    .GetAssignedTestsAsync(cancellationToken);

                var assignedKeys = new HashSet<string>(
                    assignedTests
                        .Where(test => !_labTechnicianData.IsTestCompleted(
                            test.SampleReceiptID, test.TestTypeID))
                        .Select(test => $"{test.SampleReceiptID}:{test.TestTypeID}"),
                    StringComparer.OrdinalIgnoreCase);

                var now = DateTime.Now;

                foreach (var test in rows)
                {
                    if (test.SampleReceiptID > 0 && test.TestTypeID > 0 &&
                        assignedKeys.Contains($"{test.SampleReceiptID}:{test.TestTypeID}") &&
                        test.TestResultID == 0)
                    {
                        test.Stage = "Processing";
                    }

                    if (test.DateTimeReceived.HasValue &&
                        test.TurnAroundMinutes > 0 &&
                        test.TestResultID == 0 &&
                        !string.Equals(test.Stage, "Complete",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        test.DueDateTime = test.DateTimeReceived.Value
                            .AddMinutes(test.TurnAroundMinutes);

                        var minutesRemaining =
                            (test.DueDateTime.Value - now).TotalMinutes;

                        test.TatStatus = minutesRemaining < 0
                            ? "Breached"
                            : minutesRemaining <= Math.Max(15, test.TurnAroundMinutes * 0.2)
                                ? "At Risk"
                                : "Within TAT";
                    }
                    else if (test.TestResultID > 0 ||
                             string.Equals(test.Stage, "Complete",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        test.TatStatus = "Complete";
                    }
                    else
                    {
                        test.TatStatus = "Unknown";
                    }
                }

                ViewBag.TatBreachedCount = rows.Count(x => x.TatStatus == "Breached");
                ViewBag.AtRiskCount = rows.Count(x => x.TatStatus == "At Risk");
                ViewBag.ActiveStatCount = rows.Count(x =>
                    string.Equals(x.Urgency, "STAT",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Stage, "Complete",
                        StringComparison.OrdinalIgnoreCase));
                ViewBag.CompletedTodayCount = rows.Count(x =>
                    x.CompletionDateTime.HasValue &&
                    x.CompletionDateTime.Value.Date == now.Date);
                var completedToday = rows
                    .Where(x => x.CompletionDateTime.HasValue &&
                                x.DateTimeReceived.HasValue &&
                                x.CompletionDateTime.Value.Date == now.Date &&
                                x.CompletionDateTime.Value >= x.DateTimeReceived.Value)
                    .Select(x => (x.CompletionDateTime!.Value -
                                  x.DateTimeReceived!.Value).TotalMinutes)
                    .ToList();
                ViewBag.AverageTatToday = completedToday.Count == 0
                    ? "—"
                    : $"{Math.Round(completedToday.Average())}m";

                return View(rows);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"StatUrgent failed: {ex}");
                TempData["ErrorMessage"] =
                    "Could not load STAT and urgent tests from the database. Check the application logs.";

                ViewBag.TatBreachedCount = 0;
                ViewBag.AtRiskCount = 0;
                ViewBag.ActiveStatCount = 0;
                ViewBag.CompletedTodayCount = 0;
                ViewBag.AverageTatToday = "—";

                return View(new List<StatUrgentTestViewModel>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestDetail(
            int id,
            int testRequestId = 0,
            int sampleReceiptId = 0,
            int testTypeId = 0,
            CancellationToken cancellationToken = default)
        {
            // Support the common route/query names used by existing links.
            var requestId = testRequestId > 0 ? testRequestId : id;
            if (requestId <= 0)
            {
                TempData["ErrorMessage"] = "Select a valid test request first.";
                return RedirectToAction(nameof(ReceiveSamples));
            }

            // Load request, patient and sample details for the selected test
            try
            {
                const string sql = """
                    SELECT
                        tr.TestRequestID,
                        tr.PatientID,
                        tr.RequestDate,
                        COALESCE(tr.Urgency, '') AS Urgency,
                        COALESCE(tr.Status, '') AS RequestStatus,
                        COALESCE(tr.Type, '') AS RequestedTypeText,
                        COALESCE(tr.Barcode, '') AS RequestBarcode,
                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                        COALESCE(u.IDNumber, '') AS PatientNumber,
                        COALESCE(tr.ClinicalNotes, '') AS DoctorNotes,
                        LTRIM(RTRIM(CONCAT(
                            COALESCE(doctor.Surname, ''),
                            CASE WHEN doctor.Surname IS NOT NULL THEN ', ' ELSE '' END,
                            COALESCE(doctor.Name, '')
                        ))) AS DoctorName,
                        COALESCE(sr.SampleReceiptID, 0) AS SampleReceiptID,
                        COALESCE(sr.Barcode, tr.Barcode, '') AS SampleBarcode,
                        sr.DateTimeReceived,
                        CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejectedSample
                    FROM dbo.TestRequest tr
                    INNER JOIN dbo.Patient p ON p.PatientID = tr.PatientID
                    INNER JOIN dbo.[User] u ON u.UserID = p.UserID
                    LEFT JOIN dbo.Doctor d ON d.DoctorID = tr.DoctorID
                    LEFT JOIN dbo.[User] doctor ON doctor.UserID = d.UserID
                    OUTER APPLY
                    (
                        SELECT TOP (1) receipt.*
                        FROM dbo.SampleReceipt receipt
                        WHERE receipt.TestRequestID = tr.TestRequestID
                          AND (@SampleReceiptID = 0 OR receipt.SampleReceiptID = @SampleReceiptID)
                        ORDER BY
                            CASE WHEN EXISTS
                            (
                                SELECT 1
                                FROM dbo.SampleReceiptRejection r
                                WHERE r.SampleReceiptID = receipt.SampleReceiptID
                            ) THEN 1 ELSE 0 END,
                            receipt.DateTimeReceived DESC,
                            receipt.SampleReceiptID DESC
                    ) sr
                    LEFT JOIN dbo.SampleReceiptRejection rejection
                        ON rejection.SampleReceiptID = sr.SampleReceiptID
                    WHERE tr.TestRequestID = @TestRequestID;
                    """;

                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("The DefaultConnection connection string is missing.");

                await using var connection = new SqlConnection(connectionString);
                var row = await connection.QuerySingleOrDefaultAsync<TestDetailQueryRow>(
                    new CommandDefinition(
                        sql,
                        new { TestRequestID = requestId, SampleReceiptID = sampleReceiptId },
                        cancellationToken: cancellationToken));

                if (row == null)
                {
                    TempData["ErrorMessage"] = "The selected test request could not be found.";
                    return RedirectToAction(nameof(ReceiveSamples));
                }

                const string conditionsSql = """
                    SELECT DISTINCT mc.MedicalCondition
                    FROM dbo.PatientMedicalCondition pmc
                    INNER JOIN dbo.MedicalCondition mc
                        ON mc.MedicalConditionID = pmc.MedicalConditionID
                    WHERE pmc.PatientID = @PatientID
                    ORDER BY mc.MedicalCondition;
                    """;

                const string allergiesSql = """
                    SELECT DISTINCT a.Allergy
                    FROM dbo.PatientAllergies pa
                    INNER JOIN dbo.Allergies a
                        ON a.AllergiesID = pa.AllergiesID
                    WHERE pa.PatientID = @PatientID
                    ORDER BY a.Allergy;
                    """;

                var medicalConditions = (await connection.QueryAsync<string>(
                    new CommandDefinition(
                        conditionsSql,
                        new { row.PatientID },
                        cancellationToken: cancellationToken)))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                var allergies = (await connection.QueryAsync<string>(
                    new CommandDefinition(
                        allergiesSql,
                        new { row.PatientID },
                        cancellationToken: cancellationToken)))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                var model = new TestDetailViewModel
                {
                    TestRequestID = row.TestRequestID,
                    SampleReceiptID = row.SampleReceiptID,
                    PatientID = row.PatientID,
                    Barcode = row.SampleBarcode,
                    RequestDate = row.RequestDate,
                    DateTimeReceived = row.DateTimeReceived,
                    Urgency = row.Urgency,
                    RequestStatus = row.RequestStatus,
                    PatientName = row.PatientName,
                    PatientNumber = row.PatientNumber,
                    DoctorName = string.IsNullOrWhiteSpace(row.DoctorName) ? "Not specified" : row.DoctorName,
                    DoctorNotes = row.DoctorNotes,
                    RequestedTypeText = row.RequestedTypeText,
                    HasActiveSample = row.SampleReceiptID > 0 && !row.IsRejectedSample,
                    IsRejectedSample = row.IsRejectedSample,
                    MedicalConditions = medicalConditions,
                    Allergies = allergies
                };

                model.RequestedTests = (row.RequestedTypeText ?? string.Empty)
                    .Split(new[] { ',', ';', '+', '\r', '\n' },
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (testTypeId > 0 && model.SampleReceiptID > 0 && model.HasActiveSample)
                {
                    const string selectedSql = """
                        SELECT
                            tt.TestTypeID,
                            COALESCE(tt.TestName, '') AS TestName,
                            COALESCE(latest.TestResultID, 0) AS TestResultID,
                            COALESCE(latest.Decision, '') AS LatestDecision
                        FROM dbo.TestType tt
                        OUTER APPLY
                        (
                            SELECT TOP (1)
                                result.TestResultID,
                                verification.Decision
                            FROM dbo.TestResult result
                            OUTER APPLY
                            (
                                SELECT TOP (1) rv.Decision
                                FROM dbo.ResultVerification rv
                                WHERE rv.TestResultID = result.TestResultID
                                ORDER BY rv.VerificationDateTime DESC, rv.ResultVerificationID DESC
                            ) verification
                            WHERE result.TestRequestID = @TestRequestID
                              AND result.TestTypeID = tt.TestTypeID
                            ORDER BY result.CompletionDateTime DESC, result.TestResultID DESC
                        ) latest
                        WHERE tt.TestTypeID = @TestTypeID;
                        """;

                    var selected = await connection.QuerySingleOrDefaultAsync<TestDetailSelectedRow>(
                        new CommandDefinition(
                            selectedSql,
                            new { TestRequestID = requestId, TestTypeID = testTypeId },
                            cancellationToken: cancellationToken));

                    if (selected != null &&
                        model.RequestedTests.Contains(selected.TestName, StringComparer.OrdinalIgnoreCase))
                    {
                        model.IsSelectedTest = true;
                        model.TestTypeID = selected.TestTypeID;
                        model.SelectedTestName = selected.TestName;
                        model.HasResult = selected.TestResultID > 0;
                        model.IsComplete = string.Equals(selected.LatestDecision, "Approved", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(selected.LatestDecision, "Verified", StringComparison.OrdinalIgnoreCase);

                        model.StageMessage = model.IsComplete
                            ? "This test has already been verified."
                            : model.HasResult && (string.Equals(selected.LatestDecision, "Returned", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(selected.LatestDecision, "Return", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(selected.LatestDecision, "Rejected", StringComparison.OrdinalIgnoreCase))
                                ? "This result was returned and is available for recapture."
                                : model.HasResult
                                    ? "This test has a result awaiting verification."
                                    : "This test is selected and ready for result capture.";
                    }
                }

                return View(model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TestDetail failed: {ex}");
                TempData["ErrorMessage"] = "Could not load the selected test details. Check the application logs.";
                return RedirectToAction(nameof(ReceiveSamples));
            }
        }

        private sealed class TestDetailQueryRow
        {
            public int TestRequestID { get; set; }
            public int PatientID { get; set; }
            public DateTime? RequestDate { get; set; }
            public string Urgency { get; set; } = string.Empty;
            public string RequestStatus { get; set; } = string.Empty;
            public string RequestedTypeText { get; set; } = string.Empty;
            public string PatientName { get; set; } = string.Empty;
            public string PatientNumber { get; set; } = string.Empty;
            public string DoctorName { get; set; } = string.Empty;
            public string DoctorNotes { get; set; } = string.Empty;
            public int SampleReceiptID { get; set; }
            public string SampleBarcode { get; set; } = string.Empty;
            public DateTime? DateTimeReceived { get; set; }
            public bool IsRejectedSample { get; set; }
        }

        private sealed class TestDetailSelectedRow
        {
            public int TestTypeID { get; set; }
            public string TestName { get; set; } = string.Empty;
            public int TestResultID { get; set; }
            public string LatestDecision { get; set; } = string.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> Escalations(
    CancellationToken cancellationToken)
        {
            try
            {
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(Dashboard));
                }

                var escalations = await _labTechnicianData
                    .GetActiveEscalationsAsync(technicianId.Value, cancellationToken);

                return View(escalations);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Escalations failed: {ex}");

                TempData["ErrorMessage"] =
                    $"Escalations error: {ex.Message}";

                return View(new List<LabEscalationViewModel>());
            }
        }


        // Send an overdue-test notification to the doctor who requested the test.
        // The browser posts identifiers only; all email content is loaded from the database.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyDoctor(
            int testRequestId,
            int sampleReceiptId,
            int testTypeId,
            CancellationToken cancellationToken)
        {
            if (testRequestId <= 0 || sampleReceiptId <= 0 || testTypeId <= 0)
            {
                TempData["ErrorMessage"] = "The selected request, sample, or test is invalid.";
                return RedirectToAction(nameof(Escalations));
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("The DefaultConnection connection string is missing.");

                // Retrieve the request and doctor details; assignment-based overdue validation follows below.
                const string sql = """
                    SELECT TOP (1)
                        tr.TestRequestID,
                        sr.SampleReceiptID,
                        tt.TestTypeID,
                        COALESCE(tt.TestName, '') AS TestName,
                        COALESCE(tr.Urgency, '') AS Urgency,
                        COALESCE(sr.Barcode, tr.Barcode, '') AS Barcode,
                        LTRIM(RTRIM(CONCAT(pu.Surname, ', ', pu.Name))) AS PatientName,
                        LTRIM(RTRIM(CONCAT(COALESCE(du.Surname, ''), ' ', COALESCE(du.Name, '')))) AS DoctorName,
                        COALESCE(du.Email, '') AS DoctorEmail,
                        sr.DateTimeReceived,
                        COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes
                    FROM dbo.TestRequest tr
                    INNER JOIN dbo.SampleReceipt sr ON sr.TestRequestID = tr.TestRequestID
                    INNER JOIN dbo.Patient p ON p.PatientID = tr.PatientID
                    INNER JOIN dbo.[User] pu ON pu.UserID = p.UserID
                    LEFT JOIN dbo.Doctor d ON d.DoctorID = tr.DoctorID
                    LEFT JOIN dbo.[User] du ON du.UserID = d.UserID
                    INNER JOIN dbo.TestType tt ON tt.TestTypeID = @TestTypeID
                    WHERE tr.TestRequestID = @TestRequestID
                      AND sr.SampleReceiptID = @SampleReceiptID
                      AND EXISTS (
                          SELECT 1
                          FROM STRING_SPLIT(
                              REPLACE(REPLACE(REPLACE(COALESCE(tr.Type, ''), ';', ','), '+', ','), CHAR(13), ','),
                              ',') requested
                          WHERE LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                      )
                      AND sr.DateTimeReceived IS NOT NULL
                      AND tt.TurnAroundMinutes > 0
                      AND COALESCE(tr.Status, '') NOT IN ('Cancelled', 'Canceled', 'Completed', 'Rejected');
                    """;

                await using var connection = new SqlConnection(connectionString);
                var item = await connection.QuerySingleOrDefaultAsync<OverdueNotificationRow>(
                    new CommandDefinition(sql, new
                    {
                        TestRequestID = testRequestId,
                        SampleReceiptID = sampleReceiptId,
                        TestTypeID = testTypeId
                    }, cancellationToken: cancellationToken));

                if (item == null)
                {
                    TempData["ErrorMessage"] = "That test could not be matched to this request. Refresh the page.";
                    return RedirectToAction(nameof(Escalations));
                }

                // Validate the notification against the same active assignment and
                // assignment-based TAT clock shown on Assigned Tests and Escalations.
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] = "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(Escalations));
                }

                var activeAssignments = await _labTechnicianData.GetAssignedTestsAsync(cancellationToken);
                var assignment = activeAssignments.FirstOrDefault(test =>
                    test.SampleReceiptID == sampleReceiptId &&
                    test.TestTypeID == testTypeId &&
                    (test.ReceivedByLabTechnicianID == 0 ||
                     test.ReceivedByLabTechnicianID == technicianId.Value));

                if (assignment == null ||
                    _labTechnicianData.IsTestCompleted(sampleReceiptId, testTypeId) ||
                    !assignment.SelectedAt.HasValue)
                {
                    TempData["ErrorMessage"] = "This test is no longer an active assignment for your account.";
                    return RedirectToAction(nameof(Escalations));
                }

                var dueAt = assignment.SelectedAt.Value.AddMinutes(assignment.TurnAroundMinutes);
                if (dueAt >= DateTime.Now)
                {
                    TempData["ErrorMessage"] = "This test has not exceeded its assigned turnaround time.";
                    return RedirectToAction(nameof(Escalations));
                }

                item.MinutesOverdue = (int)Math.Floor((DateTime.Now - dueAt).TotalMinutes);

                if (string.IsNullOrWhiteSpace(item.DoctorEmail))
                {
                    TempData["ErrorMessage"] = "The requesting doctor's email address is not available.";
                    return RedirectToAction(nameof(Escalations));
                }

                var subject = $"Overdue laboratory test — Request {item.TestRequestID}";
                var body = $"""
                    Dear {item.DoctorName},

                    The following laboratory test has exceeded its turnaround time and requires your review.

                    Patient: {item.PatientName}
                    Test: {item.TestName}
                    Request ID: {item.TestRequestID}
                    Sample ID: {item.SampleReceiptID}
                    Barcode: {item.Barcode}
                    Urgency: {item.Urgency}
                    Sample received: {item.DateTimeReceived:yyyy-MM-dd HH:mm}
                    Turnaround time: {item.TurnAroundMinutes} minute(s)
                    Minutes overdue: {item.MinutesOverdue}

                    Please advise whether a new sample or repeat test is clinically required.
                    The turnaround-time breach alone does not determine whether a repeat test is needed.

                    Regards,
                    Laboratory Team
                    """;

                var sent = await SendLabTechEmailAsync(item.DoctorEmail, subject, body, cancellationToken);
                if (sent)
                    TempData["SuccessMessage"] = $"Overdue-test notification sent to {item.DoctorEmail}.";
                else
                    TempData["ErrorMessage"] = "The email was not sent. Check LabTech email settings and application logs.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifyDoctor failed for request {TestRequestId}, sample {SampleReceiptId}, test type {TestTypeId}.",
                    testRequestId, sampleReceiptId, testTypeId);
                TempData["ErrorMessage"] = "Could not send the overdue-test notification. Check the application logs.";
            }

            return RedirectToAction(nameof(Escalations));
        }

        private sealed class OverdueNotificationRow
        {
            public int TestRequestID { get; set; }
            public int SampleReceiptID { get; set; }
            public int TestTypeID { get; set; }
            public string TestName { get; set; } = string.Empty;
            public string Urgency { get; set; } = string.Empty;
            public string Barcode { get; set; } = string.Empty;
            public string PatientName { get; set; } = string.Empty;
            public string DoctorName { get; set; } = string.Empty;
            public string DoctorEmail { get; set; } = string.Empty;
            public DateTime? DateTimeReceived { get; set; }
            public int TurnAroundMinutes { get; set; }
            public int MinutesOverdue { get; set; }
        }

        // Uses the separate LabTech configuration section. Supported modes:
        // LogOnly, Pickup, and Smtp. Pickup/LogOnly are useful for safe local testing.

        private async Task<bool> SendLabTechEmailAsync(
            string toEmail,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            try
            {
                var htmlBody = System.Net.WebUtility.HtmlEncode(body)
                    .Replace("\r\n", "<br />")
                    .Replace("\n", "<br />");

                var result = await _labTechEmailService.SendAsync(
                    toEmail,
                    subject,
                    htmlBody,
                    cancellationToken);

                if (!result.Success)
                {
                    _logger.LogError(
                        "Lab Technician email failed. Recipient: {Recipient}; Subject: {Subject}; Reason: {Reason}",
                        toEmail,
                        subject,
                        result.Message);
                }
                else
                {
                    _logger.LogInformation(
                        "Lab Technician email processed. Recipient: {Recipient}; Subject: {Subject}; Result: {Result}",
                        toEmail,
                        subject,
                        result.Message);
                }

                return result.Success;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while processing Lab Technician email to {Recipient}.",
                    toEmail);

                return false;
            }
        }



        [HttpGet]
        public async Task<IActionResult> Reports(
            DateTime? startDate,
            DateTime? endDate,
            CancellationToken cancellationToken)
        {
            var today = DateTime.Today;
            var start = startDate?.Date ?? today.AddDays(-29);
            var end = endDate?.Date ?? today;

            if (start > today || end > today)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Future dates cannot be used in a TAT performance report.");

                if (start > today)
                    start = today;

                if (end > today)
                    end = today;
            }

            if (start > end)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Start Date cannot be after End Date.");

                start = end;
            }

            var connectionString =
                _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");
            }

            // Calculate turnaround-time performance, compliance and outstanding breaches for the selected date range
            const string sql = """
            WITH TestInstances AS
            (
                SELECT
                    sr.SampleReceiptID,
                    sr.DateTimeReceived,
                    tt.TestTypeID,
                    tt.TestName,
                    tt.TurnAroundMinutes AS TargetMinutes,
                    latest.CompletionDateTime
                FROM dbo.SampleReceipt sr
                INNER JOIN dbo.TestRequest tr
                    ON tr.TestRequestID = sr.TestRequestID
                CROSS APPLY STRING_SPLIT(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                '+', ','),
                            CHAR(13), ','),
                        CHAR(10), ','),
                    ',') requested
                INNER JOIN dbo.TestType tt
                    ON LTRIM(RTRIM(requested.value)) =
                       LTRIM(RTRIM(tt.TestName))
                   AND (sr.TestTypeID IS NULL OR sr.TestTypeID = tt.TestTypeID)
                OUTER APPLY
                (
                    SELECT TOP (1)
                        result.CompletionDateTime
                    FROM dbo.TestResult result
                    WHERE result.TestRequestID = tr.TestRequestID
                      AND result.TestTypeID = tt.TestTypeID
                    ORDER BY
                        result.CompletionDateTime DESC,
                        result.TestResultID DESC
                ) latest
                WHERE sr.DateTimeReceived >= @StartDate
                  AND sr.DateTimeReceived < DATEADD(day, 1, @EndDate)
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.SampleReceiptRejection rejected
                      WHERE rejected.SampleReceiptID = sr.SampleReceiptID
                  )
            ),
            Measured AS
            (
                SELECT *,
                    CASE
                        WHEN CompletionDateTime IS NOT NULL
                        THEN DATEDIFF_BIG(
                            SECOND, DateTimeReceived, CompletionDateTime
                        ) / 60.0
                        ELSE NULL
                    END AS ActualMinutes
                FROM TestInstances
            )
            SELECT
                TestTypeID,
                TestName,
                MAX(TargetMinutes) AS TargetMinutes,
                AVG(ActualMinutes) AS AverageMinutes,
                CAST(
                    100.0 * SUM(
                        CASE
                            WHEN ActualMinutes IS NOT NULL
                             AND TargetMinutes IS NOT NULL
                             AND ActualMinutes <= TargetMinutes
                            THEN 1 ELSE 0
                        END
                    ) / NULLIF(SUM(
                        CASE
                            WHEN ActualMinutes IS NOT NULL
                             AND TargetMinutes IS NOT NULL
                            THEN 1 ELSE 0
                        END
                    ), 0)
                    AS float
                ) AS CompliancePercent,
                SUM(
                    CASE
                        WHEN ActualMinutes IS NOT NULL
                         AND TargetMinutes IS NOT NULL
                         AND ActualMinutes > TargetMinutes
                        THEN 1 ELSE 0
                    END
                ) AS BreachCount,
                SUM(
                    CASE
                        WHEN CompletionDateTime IS NULL
                         AND TargetMinutes > 0
                         AND DATEDIFF(
                             MINUTE, DateTimeReceived, GETDATE()
                         ) > TargetMinutes
                        THEN 1 ELSE 0
                    END
                ) AS AtRiskCount,
                COUNT(*) AS TotalTests,
                SUM(
                    CASE WHEN ActualMinutes IS NOT NULL
                         THEN 1 ELSE 0 END
                ) AS CompletedTests
            FROM Measured
            GROUP BY TestTypeID, TestName
            ORDER BY TestName;
            """;

            await using var connection = new SqlConnection(connectionString);

            var rows = (await connection.QueryAsync<TatPerformanceReportRow>(
                new CommandDefinition(
                    sql,
                    new { StartDate = start, EndDate = end },
                    cancellationToken: cancellationToken))).ToList();

            var report = new TatPerformanceReportViewModel
            {
                StartDate = start,
                EndDate = end,
                GeneratedAt = DateTime.Now,
                Rows = rows,
                TotalTests = rows.Sum(row => row.TotalTests),
                CompletedTests = rows.Sum(row => row.CompletedTests),
                BreachCount = rows.Sum(row => row.BreachCount),
                AtRiskCount = rows.Sum(row => row.AtRiskCount)
            };

            var completedWithTargets = rows
                .Where(row => row.TargetMinutes.HasValue)
                .Sum(row => row.CompletedTests);

            report.AverageTatMinutes = rows
                .Where(row => row.AverageMinutes.HasValue)
                .Sum(row => row.AverageMinutes!.Value *
                            row.CompletedTests) /
                Math.Max(1, rows.Sum(row =>
                    row.AverageMinutes.HasValue ? row.CompletedTests : 0));

            report.CompliancePercent = completedWithTargets == 0
                ? 0
                : rows.Sum(row =>
                    row.CompliancePercent * row.CompletedTests / 100.0) /
                  completedWithTargets * 100.0;

            return View(report);
        }

        // Generate a PDF version of the turnaround-time performance report
        [HttpGet]
        public async Task<IActionResult> ExportReportPdf(
            DateTime? startDate,
            DateTime? endDate,
            CancellationToken cancellationToken)
        {
            var today = DateTime.Today;
            var requestedStart = startDate?.Date ?? today.AddDays(-29);
            var requestedEnd = endDate?.Date ?? today;

            if (requestedStart > today || requestedEnd > today)
            {
                TempData["ErrorMessage"] =
                    "Future dates cannot be used in a TAT performance report.";

                return RedirectToAction(
                    nameof(Reports),
                    new
                    {
                        startDate = requestedStart > today ? today : requestedStart,
                        endDate = requestedEnd > today ? today : requestedEnd
                    });
            }

            var result = await Reports(startDate, endDate, cancellationToken);

            if (result is not ViewResult viewResult ||
                viewResult.Model is not TatPerformanceReportViewModel report ||
                !viewResult.ViewData.ModelState.IsValid)
            {
                return BadRequest("The selected report dates are invalid.");
            }

            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            const string exporterSql = """
                SELECT
                    lt.LabTechnicianID,
                    u.Name,
                    u.Surname
                FROM dbo.[User] u
                INNER JOIN dbo.LabTechnician lt
                    ON lt.UserID = u.UserID
                WHERE u.UserID = @UserID
                  AND u.Role = 'Lab Technician';
                """;

            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");

            await using var exporterConnection = new SqlConnection(connectionString);
            var exporter = await exporterConnection.QuerySingleOrDefaultAsync<LabTechReportExporter>(
                new CommandDefinition(
                    exporterSql,
                    new { UserID = userId },
                    cancellationToken: cancellationToken));

            if (exporter is null)
                return NotFound("Your lab technician profile could not be found.");

            var pdfRows = report.Rows.Select(row => new LabTechReportPdfRow
            {
                TestName = row.TestName,
                TargetMinutes = row.TargetMinutes ?? 0,
                AverageMinutes = row.AverageMinutes,
                CompliancePercent = row.CompliancePercent,
                BreachCount = row.BreachCount
            }).ToList();

            var pdf = _labTechReportPdf.Build(
                "NMBH Laboratory",
                report.StartDate,
                report.EndDate,
                report.GeneratedAt,
                $"{exporter.Name} {exporter.Surname}".Trim(),
                exporter.LabTechnicianID,
                pdfRows);

            return File(
                pdf,
                "application/pdf",
                $"TAT-Performance-{report.StartDate:yyyyMMdd}-{report.EndDate:yyyyMMdd}.pdf");
        }

        // View and manage the authenticated technician's profile and password
        [Authorize(Roles = "Lab Technician")]
        [HttpGet]
        public async Task<IActionResult> Profile(
            CancellationToken cancellationToken)
        {
            var userIdValue = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdValue, out var userId))
                return Challenge();

            var connectionString =
                _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");

            const string sql = """
        SELECT
            u.UserID,
            u.Name,
            u.Surname,
            COALESCE(lt.EmployeeNumber, '') AS EmployeeNumber,
            u.Email,
            COALESCE(u.ContactNumber, '') AS ContactNumber,
            u.Role,
            u.IsActive
        FROM dbo.[User] u
        LEFT JOIN dbo.LabTechnician lt
            ON lt.UserID = u.UserID
        WHERE u.UserID = @UserID;
        """;

            await using var connection = new SqlConnection(connectionString);

            var profile = await connection.QuerySingleOrDefaultAsync
                <LabTechnicianProfileViewModel>(
                    new CommandDefinition(
                        sql,
                        new { UserID = userId },
                        cancellationToken: cancellationToken));

            if (profile == null)
                return NotFound("Your user profile could not be found.");

            return View(profile);
        }


        [Authorize(Roles = "Lab Technician")]
        [HttpGet]
        public async Task<IActionResult> EditProfile(
          CancellationToken cancellationToken)
        {

            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            if (!HasValidReauthenticationTicket(userId, nameof(EditProfile)))
            {
                return RedirectToAction(
                    nameof(Profile),
                    new { reauth = nameof(EditProfile) });
            }


            const string sql = """
        SELECT Name, Surname, Email, ContactNumber
        FROM dbo.[User]
        WHERE UserID = @UserID
          AND Role = 'Lab Technician';
        """;

            await using var connection = new SqlConnection(
                _configuration.GetConnectionString("DefaultConnection"));

            var model = await connection.QuerySingleOrDefaultAsync
                <EditLabTechnicianProfileViewModel>(
                    new CommandDefinition(
                        sql, new { UserID = userId },
                        cancellationToken: cancellationToken));

            if (model == null)
                return NotFound("Your technician account was not found.");

            return View(model);
        }

        [Authorize(Roles = "Lab Technician")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(
            EditLabTechnicianProfileViewModel model,
            CancellationToken cancellationToken)
        {


            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            if (!HasValidReauthenticationTicket(userId, nameof(EditProfile)))
            {
                return RedirectToAction(
                    nameof(Profile),
                    new { reauth = nameof(EditProfile) });
            }

            model.Name = model.Name?.Trim() ?? string.Empty;
            model.Surname = model.Surname?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim() ?? string.Empty;
            model.ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber)
                ? null
                : model.ContactNumber.Trim();

            if (!ModelState.IsValid)
                return View(model);

            var connectionString =
                _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string duplicateEmailSql = """
        SELECT COUNT(1)
        FROM dbo.[User]
        WHERE Email = @Email
          AND UserID <> @UserID;
        """;

            var duplicateEmail = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    duplicateEmailSql,
                    new { model.Email, UserID = userId },
                    cancellationToken: cancellationToken));

            if (duplicateEmail > 0)
            {
                ModelState.AddModelError(
                    nameof(model.Email),
                    "That email address is already in use.");
                return View(model);
            }

            const string updateSql = """
        UPDATE dbo.[User]
        SET Name = @Name,
            Surname = @Surname,
            Email = @Email,
            ContactNumber = @ContactNumber
        WHERE UserID = @UserID
          AND Role = 'Lab Technician';
        """;

            var updated = await connection.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new
                    {
                        model.Name,
                        model.Surname,
                        model.Email,
                        model.ContactNumber,
                        UserID = userId
                    },
                    cancellationToken: cancellationToken));

            if (updated != 1)
                return NotFound("Your technician account was not found.");

            TempData["SuccessMessage"] = "Your profile was updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [Authorize(Roles = "Lab Technician")]
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            if (!HasValidReauthenticationTicket(
                userId, nameof(ChangePassword)))
            {
                return RedirectToAction(
                    nameof(Profile),
                    new { reauth = nameof(ChangePassword) });
            }

            return View(new ChangeLabTechnicianPasswordViewModel());
        }

        [Authorize(Roles = "Lab Technician")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            ChangeLabTechnicianPasswordViewModel model,
            CancellationToken cancellationToken)
        {
            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            if (!HasValidReauthenticationTicket(
                userId, nameof(ChangePassword)))
            {
                return RedirectToAction(
                    nameof(Profile),
                    new { reauth = nameof(ChangePassword) });
            }

            if (!ModelState.IsValid)
                return View(model);

            var connectionString =
                _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");

            await using var connection = new SqlConnection(connectionString);

            const string getHashSql = """
            SELECT PasswaordHash
            FROM dbo.[User]
            WHERE UserID = @UserID
              AND Role = 'Lab Technician'
              AND IsActive = 1;
            """;

            var storedHash = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    getHashSql,
                    new { UserID = userId },
                    cancellationToken: cancellationToken));

            if (string.IsNullOrWhiteSpace(storedHash))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Your account could not be verified.");
                return View(model);
            }

            var currentHash = AuthRepository.HashPassword(model.CurrentPassword);

            if (!string.Equals(
                currentHash, storedHash.Trim(),
                StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(
                    nameof(model.CurrentPassword),
                    "The current password is incorrect.");
                return View(model);
            }

            if (string.Equals(
                model.CurrentPassword, model.NewPassword,
                StringComparison.Ordinal))
            {
                ModelState.AddModelError(
                    nameof(model.NewPassword),
                    "Your new password must differ from your current password.");
                return View(model);
            }

            var newHash = AuthRepository.HashPassword(model.NewPassword);

            const string updatePasswordSql = """
            UPDATE dbo.[User]
            SET PasswaordHash = @PasswordHash
            WHERE UserID = @UserID
              AND Role = 'Lab Technician'
              AND IsActive = 1;
            """;

            var updated = await connection.ExecuteAsync(
                new CommandDefinition(
                    updatePasswordSql,
                    new { PasswordHash = newHash, UserID = userId },
                    cancellationToken: cancellationToken));

            if (updated != 1)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "The password could not be updated. Please try again.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Your password was changed successfully.";
            return RedirectToAction(nameof(Profile));
        }
        // Helpers for validating the logged-in technician and protecting sensitive profile actions
        private bool TryGetAuthenticatedUserId(out int userId)
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out userId);
        }

        private void CreateReauthenticationTicket(int userId, string action)
        {
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(ReauthMinutes)
                .ToUnixTimeSeconds();

            var ticket = $"{userId}|{expiresAt}|{action}";
            var protectedTicket = _reauthProtector.Protect(ticket);

            Response.Cookies.Append(
                ReauthCookieName,
                protectedTicket,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(ReauthMinutes),
                    Path = "/LabTechnician"
                });
        }

        private bool HasValidReauthenticationTicket(int userId, string action)
        {
            if (!Request.Cookies.TryGetValue(ReauthCookieName, out var cookieValue)
                || string.IsNullOrWhiteSpace(cookieValue))
            {
                return false;
            }

            try
            {
                var ticket = _reauthProtector.Unprotect(cookieValue);
                var parts = ticket.Split('|');

                if (parts.Length != 3)
                    return false;

                if (!int.TryParse(parts[0], out var ticketUserId)
                    || !long.TryParse(parts[1], out var expiresAt))
                {
                    return false;
                }

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                return ticketUserId == userId
                    && expiresAt > now
                    && parts[2] == action;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }



        [Authorize(Roles = "Lab Technician")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyProfileAccess(
            ReauthenticateViewModel model,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Enter your current password.";
                return RedirectToAction(nameof(Profile));
            }

            if (model.ReturnAction != nameof(EditProfile) &&
                model.ReturnAction != nameof(ChangePassword))
            {
                return BadRequest();
            }

            if (!TryGetAuthenticatedUserId(out var userId))
                return Challenge();

            var connectionString =
    _configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "The DefaultConnection connection string is missing.");

            await using var connection = new SqlConnection(connectionString);

            const string emailSql = """
            SELECT Email
            FROM dbo.[User]
            WHERE UserID = @UserID
              AND Role = 'Lab Technician'
              AND IsActive = 1;
            """;

            var email = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    emailSql,
                    new { UserID = userId },
                    cancellationToken: cancellationToken));

            if (string.IsNullOrWhiteSpace(email))
                return Challenge();

            var login = new Login
            {
                Email = email,
                Password = model.Password
            };

            var authenticatedUser = await _authRepository.ValidateUser(login);

            if (authenticatedUser == null ||
                authenticatedUser.UserID != userId ||
                !authenticatedUser.IsActive ||
                !string.Equals(
                    authenticatedUser.Role?.Trim(),
                    "Lab Technician",
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] =
                    "Incorrect password. Please try again.";

                return RedirectToAction(
                    nameof(Profile),
                    new { reauth = model.ReturnAction });
            }

            CreateReauthenticationTicket(userId, model.ReturnAction);

            return RedirectToAction(model.ReturnAction);
        }




        // Sample receipt and test selection

        [HttpGet]
        public async Task<IActionResult> AwaitingSelection(
            CancellationToken cancellationToken)
        {
            try
            {
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(Dashboard));
                }

                var allReceivedSamples = await _labTechnicianData
                    .GetReceivedSamplesAsync(cancellationToken);

                // New receipts belong only to the technician who accepted them.
                // Legacy receipts without an owner remain visible for compatibility.
                var samples = allReceivedSamples
                    .Where(x => x.ReceivedByLabTechnicianID == 0 ||
                                x.ReceivedByLabTechnicianID == technicianId.Value)
                    .ToList();

                var patientIds = samples
                    .Select(x => x.PatientID)
                    .Distinct()
                    .ToList();

                var patientMedicalInfo =
                    new Dictionary<int, PatientMedicalInfoViewModel>();

                foreach (var patientId in patientIds)
                {
                    patientMedicalInfo[patientId] =
                        await _labTechnicianData.GetPatientMedicalInfoAsync(
                            patientId, cancellationToken);
                }

                ViewBag.PatientMedicalInfo = patientMedicalInfo;


                var testTypes = await _labTechnicianData
                    .GetAvailableTestTypesAsync(cancellationToken);

                ViewBag.TestTypes = testTypes;

                // Retain the full stock list for now.
                // The view will filter it using the test-to-consumable mappings.
                ViewBag.ConsumablesByTestType =
                  await _labTechnicianData.GetConsumablesByTestTypeAsync(cancellationToken);

                var assignedTestKeys = _labTechnicianData.GetAssignedTestKeys();
                ViewBag.AssignedTestKeys = assignedTestKeys;

                var activeSamples = samples
                    .Where(x => !x.IsRejected)
                    .ToList();

                // A sample stays in Awaiting Selection only while at least one
                // requested test is not assigned yet. Requests whose Type field
                // contains multiple tests remain visible until all matching tests
                // have been assigned.
                var availableSamples = activeSamples
                    .Where(sample =>
                    {
                        var requestedNames = (sample.Type ?? string.Empty)
                            .Split(
                                new[] { ',', ';', '+', '\r', '\n' },
                                StringSplitOptions.TrimEntries |
                                StringSplitOptions.RemoveEmptyEntries);

                        var matchingTypes = testTypes
                            .Where(test => requestedNames.Contains(
                                test.TestName,
                                StringComparer.OrdinalIgnoreCase))
                            .ToList();

                        // Do not silently hide a request if its test names cannot
                        // be matched to configured TestType records.
                        if (matchingTypes.Count == 0)
                            return true;

                        return matchingTypes.Any(test =>
                            !assignedTestKeys.Contains(
                                $"{sample.SampleReceiptID}:{test.TestTypeID}",
                                StringComparer.OrdinalIgnoreCase));
                    })
                    .ToList();

                return View(availableSamples);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AwaitingSelection failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load Awaiting Selection data. Check the application logs.";

                ViewBag.TestTypes = new List<LabTechnicianQueueItem>();
                ViewBag.ConsumablesByTestType =
                    new Dictionary<int, List<ConsumableStockItem>>();

                ViewBag.PatientMedicalInfo =
                    new Dictionary<int, PatientMedicalInfoViewModel>();

                return View(new List<LabTechnicianQueueItem>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectTest(
            int sampleReceiptId,
            int testTypeId,
            CancellationToken cancellationToken)
        {
            if (sampleReceiptId <= 0 || testTypeId <= 0)
            {
                TempData["ErrorMessage"] = "Select a valid sample and test.";
                return RedirectToAction(nameof(AwaitingSelection));
            }

            try
            {
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(AwaitingSelection));
                }

                var result = await _labTechnicianData.SelectTestAsync(
                    sampleReceiptId,
                    testTypeId,
                    technicianId.Value,
                    cancellationToken);

                TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
                    result.Message;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SelectTest failed: {ex}");

                TempData["ErrorMessage"] =
                    "The test could not be selected. Check the application logs.";
            }

            return RedirectToAction(nameof(AwaitingSelection));
        }

        // Assigned tests and result capture
        [HttpGet]
        public async Task<IActionResult> AssignedTests(
            CancellationToken cancellationToken)
        {
            try
            {
                var tests = await _labTechnicianData
                    .GetAssignedTestsAsync(cancellationToken);

                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(Dashboard));
                }

                tests = tests
                    .Where(test => test.ReceivedByLabTechnicianID == 0 ||
                                   test.ReceivedByLabTechnicianID == technicianId.Value)
                    .ToList();

                var completedTests = new HashSet<string>();

                foreach (var test in tests)
                {
                    if (_labTechnicianData.IsTestCompleted(
                        test.SampleReceiptID,
                        test.TestTypeID))
                    {
                        completedTests.Add(
                            $"{test.SampleReceiptID}-{test.TestTypeID}");
                    }
                }

                ViewBag.CompletedTests = completedTests;
                return View(tests);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AssignedTests failed: {ex}");

                TempData["ErrorMessage"] = "Could not load assigned tests.";
                ViewBag.CompletedTests = new HashSet<string>();

                return View(new List<LabTechnicianQueueItem>());
            }
        }

        [HttpGet]
        public IActionResult CaptureResultsLookup()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CaptureResultsLookup(
            string registrationNumber,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(registrationNumber))
            {
                ModelState.AddModelError(
                    nameof(registrationNumber),
                    "Enter a patient registration number.");

                return View();
            }

            registrationNumber = registrationNumber.Trim();

            try
            {
                var matchingTests = await _labTechnicianData
                    .FindPatientTestsAsync(
                        registrationNumber,
                        cancellationToken);

                ViewBag.RegistrationNumber = registrationNumber;

                if (matchingTests.Count == 0)
                {
                    ViewBag.LookupMessage =
                        "No assigned tests were found for that registration number.";

                    return View();
                }

                ViewBag.MatchingTests = matchingTests;
                return View();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"CaptureResultsLookup failed: {ex}");

                ViewBag.LookupMessage =
                    "The patient lookup failed. Check the application logs.";
                ViewBag.RegistrationNumber = registrationNumber;

                return View();
            }
        }

        // Capture test results and validate submissions before saving them

        [HttpGet]
        public async Task<IActionResult> CaptureResults(
            int sampleReceiptId,
            int testTypeId,
            CancellationToken cancellationToken)
        {
            if (sampleReceiptId <= 0 || testTypeId <= 0)
            {
                TempData["ErrorMessage"] =
                    "Choose an individual assigned test first.";

                return RedirectToAction(nameof(AssignedTests));
            }

            try
            {
                var model = await _labTechnicianData.GetCaptureTestAsync(
                    sampleReceiptId,
                    testTypeId,
                    cancellationToken);

                if (model == null)
                {
                    TempData["ErrorMessage"] =
                        "The selected test could not be loaded. It may not be assigned, may have been rejected, or may already be completed.";

                    return RedirectToAction(nameof(AssignedTests));
                }

                // Load the patient's medical conditions and allergies.
                var medicalInfo =
                    await _labTechnicianData.GetPatientMedicalInfoAsync(
                        model.PatientID,
                        cancellationToken);

                model.MedicalConditions = medicalInfo.Conditions
                    .Select(c => c.Condition)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                model.Allergies = medicalInfo.Allergies
                    .Select(a => a.Allergy)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

                return View(model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"CaptureResults GET failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load the selected test. Check the application logs.";

                return RedirectToAction(nameof(AssignedTests));
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CaptureResults(
            CaptureResultsViewModel model,
            CancellationToken cancellationToken)
        {
            if (model.SampleReceiptID <= 0 || model.TestTypeID <= 0)
            {
                TempData["ErrorMessage"] =
                    "The selected sample or test is invalid.";

                return RedirectToAction(nameof(AssignedTests));
            }

            if (string.IsNullOrWhiteSpace(model.ResultValue))
            {
                ModelState.AddModelError(
                    nameof(model.ResultValue),
                    "Enter a result before saving.");

                return await ReloadCaptureFormAsync(model, cancellationToken);
            }

            try
            {

                var loggedInUserId = User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (!int.TryParse(loggedInUserId, out var userId) || userId <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login could not be matched to a valid user.";

                    return RedirectToAction(nameof(AssignedTests));
                }

                var technicianId = await _labTechnicianData
                    .GetLabTechnicianIdForUserAsync(userId, cancellationToken);

                if (technicianId == null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "No lab technician profile is linked to your login.";

                    return RedirectToAction(nameof(AssignedTests));
                }


                var saveResult = await _labTechnicianData.SaveTestResultAsync(
                    model.SampleReceiptID,
                    model.TestTypeID,
                    model.ResultValue,
                    model.TechnicianNotes,
                    technicianId.Value,
                    cancellationToken);

                if (!saveResult.Success)
                {
                    ModelState.AddModelError(string.Empty, saveResult.Message);
                    return await ReloadCaptureFormAsync(model, cancellationToken);
                }

                TempData["SuccessMessage"] = saveResult.Message;
                return RedirectToAction(nameof(AssignedTests));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"CaptureResults POST failed: {ex}");

                ModelState.AddModelError(
                    string.Empty,
                    "Could not save the result. Check the application logs.");

                return await ReloadCaptureFormAsync(model, cancellationToken);
            }
        }

        private async Task<IActionResult> ReloadCaptureFormAsync(
            CaptureResultsViewModel submittedModel,
            CancellationToken cancellationToken)
        {
            try
            {
                var freshModel = await _labTechnicianData.GetCaptureTestAsync(
                    submittedModel.SampleReceiptID,
                    submittedModel.TestTypeID,
                    cancellationToken);

                if (freshModel == null)
                {
                    TempData["ErrorMessage"] =
                        "The selected test is no longer available.";

                    return RedirectToAction(nameof(AssignedTests));
                }

                freshModel.ResultValue = submittedModel.ResultValue;
                freshModel.TechnicianNotes = submittedModel.TechnicianNotes;

                return View(nameof(CaptureResults), freshModel);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ReloadCaptureForm failed: {ex}");

                TempData["ErrorMessage"] = "Could not reload the capture form.";
                return RedirectToAction(nameof(AssignedTests));
            }
        }

        // Result verification
        // Load results awaiting verification and process verification decisions
        [HttpGet]
        public async Task<IActionResult> VerificationQueue(
            CancellationToken cancellationToken)
        {

            try
            {
                var results = await _labTechnicianData
                    .GetVerificationQueueAsync(cancellationToken);

                var pendingResults = results
                    .Where(x =>
                        !string.Equals(x.LatestVerificationDecision, "Approved",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.LatestVerificationDecision, "Returned",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.LatestVerificationDecision, "Return",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.LatestVerificationDecision, "Rejected",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return View(pendingResults);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VerificationQueue failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load the verification queue. Check the application logs.";

                return View(new List<VerificationQueueItemViewModel>());
            }
        }


        [HttpGet]
        public async Task<IActionResult> VerifyResults(
            int testResultId,
            CancellationToken cancellationToken)
        {
            if (testResultId <= 0)
            {
                TempData["ErrorMessage"] =
                    "Select a valid test result to verify.";

                return RedirectToAction(nameof(VerificationQueue));
            }

            try
            {
                var result = await _labTechnicianData
                    .GetVerificationResultAsync(
                        testResultId,
                        cancellationToken);

                if (result == null)
                {
                    TempData["ErrorMessage"] =
                        "That result could not be found in the current verification session.";

                    return RedirectToAction(nameof(VerificationQueue));
                }

                // Load the patient's medical conditions and allergies.
                var medicalInfo =
                    await _labTechnicianData.GetPatientMedicalInfoAsync(
                        result.PatientID,
                        cancellationToken);

                result.MedicalConditions = medicalInfo.Conditions
                    .Select(c => c.Condition)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                result.Allergies = medicalInfo.Allergies
                    .Select(a => a.Allergy)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

                return View(result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VerifyResults failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load the selected result. Check the application logs.";

                return RedirectToAction(nameof(VerificationQueue));
            }
        }





        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitVerificationDecision(
            int testResultId,
            string decision,
            string? returnReason,
            CancellationToken cancellationToken)
        {
            if (testResultId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid test result.";
                return RedirectToAction(nameof(VerificationQueue));
            }

            var normalizedDecision = decision?.Trim();

            if (!string.Equals(normalizedDecision, "Approved",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedDecision, "Returned",
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Select a valid verification decision.";
                return RedirectToAction(nameof(VerifyResults), new { testResultId });
            }

            if (string.Equals(normalizedDecision, "Returned",
                    StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(returnReason))
            {
                TempData["ErrorMessage"] =
                    "A reason is required when returning a result for recapture.";
                return RedirectToAction(nameof(VerifyResults), new { testResultId });
            }

            try
            {
                var result = await _labTechnicianData.GetVerificationResultAsync(
                    testResultId, cancellationToken);

                if (result == null)
                {
                    TempData["ErrorMessage"] = "The selected test result was not found.";
                    return RedirectToAction(nameof(VerificationQueue));
                }

                var userId = User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;


                if (!int.TryParse(userId, out var loggedInUserId)
                     || loggedInUserId <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login could not be matched to a valid user.";

                    return RedirectToAction(
                        nameof(VerifyResults),
                        new { testResultId });
                }


                var technicianId = await _labTechnicianData
                    .GetLabTechnicianIdForUserAsync(loggedInUserId, cancellationToken);

                if (technicianId == null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "No lab technician profile is linked to your login.";
                    return RedirectToAction(nameof(VerifyResults), new { testResultId });
                }

                var saveResult = await _labTechnicianData.SaveVerificationDecisionAsync(
                    testResultId,
                    technicianId.Value,
                    normalizedDecision!,
                    returnReason,
                    cancellationToken);

                if (!saveResult.Success)
                {
                    Console.Error.WriteLine(
                        $"Verification save failed for TestResultID {testResultId}: {saveResult.Message}");

                    TempData["ErrorMessage"] = saveResult.Message;
                    return RedirectToAction(nameof(VerifyResults), new { testResultId });
                }

                Console.WriteLine(
                    $"Verification save succeeded for TestResultID {testResultId}: {saveResult.Message}");

                TempData["SuccessMessage"] = saveResult.Message;

                // Send a notification only after an Approved decision saves successfully.
                if (string.Equals(
                        normalizedDecision,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var testRecipient = _configuration["EmailSettings:TestRecipient"];

                        var doctorEmail = result.DoctorEmail?.Trim();

                        if (string.IsNullOrWhiteSpace(doctorEmail))
                        {
                            _logger.LogWarning(
                                "Verification saved for TestResultID {TestResultId}, but no doctor email was available.",
                                testResultId);
                        }
                        else
                        {
                            var subject = $"Laboratory result verified — Result {testResultId}";

                            var body = $"""
                                Dear Doctor,

                                A laboratory test result has been verified

                                Test Result ID: {testResultId}
                                Decision: Approved

                                Please log in to the laboratory system to view the result

                                Regards,
                                Laboratory Team
                                """;

                            var emailSent = await SendLabTechEmailAsync(
                                doctorEmail,
                                subject,
                                body,
                                cancellationToken);

                            if (emailSent)
                            {
                                _logger.LogInformation(
                                    "Verification notification sent for TestResultID {TestResultId} to {Recipient}.",
                                    testResultId,
                                    doctorEmail);
                            }
                            else
                            {
                                _logger.LogError(
                                    "Verification saved, but email notification failed for TestResultID {TestResultId}.",
                                    testResultId);

                                TempData["EmailWarning"] =
                                    "The result was verified, but the email notification failed.";
                            }
                        }
                    }
                    catch (Exception emailException)
                    {
                        _logger.LogError(
                            emailException,
                            "Verification saved, but email notification failed for TestResultID {TestResultId}.",
                            testResultId);

                        TempData["EmailWarning"] =
                            "The result was verified, but the email notification failed.";
                    }
                }

                // After a result is returned, stay on the Verification Queue.
                // The returned result can be opened for recapture from Review Queue.
                return RedirectToAction(nameof(VerificationQueue));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SubmitVerificationDecision failed: {ex}");
                TempData["ErrorMessage"] =
                    "Could not save the verification decision. Check the application logs.";
                return RedirectToAction(nameof(VerifyResults), new { testResultId });
            }
        }




        [HttpGet]
        public async Task<IActionResult> ReviewQueue(
            CancellationToken cancellationToken)
        {
            try
            {
                var results = await _labTechnicianData
                    .GetVerificationQueueAsync(cancellationToken);

                // Pass all results to the view. ReviewQueue.cshtml must first
                // select the newest result for each request/test type and only
                // then filter for Returned/Return/Rejected. Filtering here first
                // would leave an older returned result visible after recapture.
                return View(results);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ReviewQueue failed: {ex}");

                TempData["ErrorMessage"] =
                    "The Review Queue could not be loaded. Please try again.";

                return View(new List<VerificationQueueItemViewModel>());
            }
        }

        // Receive and reject samples

        [HttpGet]
        public async Task<IActionResult> ReceiveSamples(
            CancellationToken cancellationToken)
        {
            try
            {
                var pendingRequests = await _labTechnicianData
                    .GetRequestsAwaitingReceiptAsync(cancellationToken);

                var receivedSamples = await _labTechnicianData
                    .GetReceivedSamplesAsync(cancellationToken);

                var allRows = pendingRequests
                    .Concat(receivedSamples)
                    .OrderByDescending(x => x.DateTimeReceived.HasValue)
                    .ThenByDescending(x => x.DateTimeReceived)
                    .ThenByDescending(x => x.RequestDate)
                    .ToList();

                return View(allRows);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ReceiveSamples failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not load sample requests. Check the application logs.";

                return View(new List<LabTechnicianQueueItem>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveSample(
            int testRequestId,
            int testTypeId,
            string barcode,
            CancellationToken cancellationToken)
        {
            if (testRequestId <= 0 || testTypeId <= 0 ||
                string.IsNullOrWhiteSpace(barcode))
            {
                TempData["ErrorMessage"] =
                    "A valid request, test type and barcode are required.";

                return RedirectToAction(nameof(ReceiveSamples));
            }

            try
            {
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(ReceiveSamples));
                }

                var received = await _labTechnicianData.ReceiveSampleAsync(
                    testRequestId,
                    testTypeId,
                    barcode,
                    technicianId.Value,
                    cancellationToken);

                TempData[received ? "SuccessMessage" : "ErrorMessage"] =
                    received
                        ? "Sample received successfully."
                        : "The request could not be found, or this barcode has already been received.";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ReceiveSample failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not receive the sample. Check the application logs.";
            }

            return RedirectToAction(nameof(ReceiveSamples));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPendingSample(
            int testRequestId,
            int testTypeId,
            string barcode,
            string reason,
            CancellationToken cancellationToken)
        {
            if (testRequestId <= 0 || testTypeId <= 0 ||
                string.IsNullOrWhiteSpace(barcode) ||
                string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] =
                    "Enter the barcode and a rejection reason before rejecting this test sample.";
                return RedirectToAction(nameof(ReceiveSamples));
            }

            try
            {
                var technicianId = await GetCurrentLabTechnicianIdAsync(cancellationToken);
                if (technicianId is null || technicianId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "Your login is not linked to a lab technician profile.";
                    return RedirectToAction(nameof(ReceiveSamples));
                }

                barcode = barcode.Trim();
                reason = reason.Trim();

                var received = await _labTechnicianData.ReceiveSampleAsync(
                    testRequestId,
                    testTypeId,
                    barcode,
                    technicianId.Value,
                    cancellationToken);

                if (!received)
                {
                    TempData["ErrorMessage"] =
                        "The sample could not be recorded. Check that its barcode is unique and this test is still awaiting receipt.";
                    return RedirectToAction(nameof(ReceiveSamples));
                }

                const string receiptSql = """
                    SELECT TOP (1) SampleReceiptID
                    FROM dbo.SampleReceipt
                    WHERE TestRequestID = @TestRequestID
                      AND TestTypeID = @TestTypeID
                      AND Barcode = @Barcode
                    ORDER BY DateTimeReceived DESC, SampleReceiptID DESC;
                    """;

                var connectionString = _configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("The DefaultConnection connection string is missing.");

                await using var connection = new SqlConnection(connectionString);
                var sampleReceiptId = await connection.QuerySingleOrDefaultAsync<int?>(
                    new CommandDefinition(
                        receiptSql,
                        new
                        {
                            TestRequestID = testRequestId,
                            TestTypeID = testTypeId,
                            Barcode = barcode
                        },
                        cancellationToken: cancellationToken));

                if (sampleReceiptId is null || sampleReceiptId.Value <= 0)
                {
                    TempData["ErrorMessage"] =
                        "The sample was recorded, but its receipt could not be found to finish the rejection.";
                    return RedirectToAction(nameof(ReceiveSamples));
                }

                var rejected = await _labTechnicianData.RejectSampleAsync(
                    sampleReceiptId.Value,
                    reason,
                    cancellationToken);

                TempData[rejected ? "SuccessMessage" : "ErrorMessage"] =
                    rejected
                        ? "The individual test sample was rejected and saved to rejection history."
                        : "The sample was received, but its rejection could not be saved. Check Rejected Sample History.";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RejectPendingSample failed: {ex}");
                TempData["ErrorMessage"] =
                    "Could not reject the sample. Check the application logs.";
            }

            return RedirectToAction(nameof(ReceiveSamples));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectSample(
            int sampleReceiptId,
            string reason,
            CancellationToken cancellationToken)
        {
            if (sampleReceiptId <= 0 || string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] =
                    "Select a sample and provide a rejection reason.";
                return RedirectToAction(nameof(ReceiveSamples));
            }

            try
            {
                var rejected = await _labTechnicianData.RejectSampleAsync(
                    sampleReceiptId,
                    reason,
                    cancellationToken);

                TempData[rejected ? "SuccessMessage" : "ErrorMessage"] =
                    rejected
                        ? "Sample rejected. Other samples on this request remain unaffected."
                        : "Sample not found. Refresh the page and try again.";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RejectSample failed: {ex}");
                TempData["ErrorMessage"] =
                    "Could not reject the sample. Check the application logs.";
            }

            return RedirectToAction(nameof(ReceiveSamples));
        }

        private async Task<int?> GetCurrentLabTechnicianIdAsync(
            CancellationToken cancellationToken)
        {
            var userIdText = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdText, out var userId) || userId <= 0)
            {
                var developmentUserId = _configuration.GetValue<int?>(
                    "LabTechnician:DevelopmentUserId");

                if (!_environment.IsDevelopment() ||
                    developmentUserId is null ||
                    developmentUserId.Value <= 0)
                {
                    return null;
                }

                userId = developmentUserId.Value;
            }

            return await _labTechnicianData.GetLabTechnicianIdForUserAsync(
                userId,
                cancellationToken);
        }
    }
}