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
                              AND NOT EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.SampleReceipt sr
                                  WHERE sr.TestRequestID = tr.TestRequestID
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
                var escalations = await _labTechnicianData
                    .GetActiveEscalationsAsync(cancellationToken);

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

                // Confirm the test is overdue and retrieve the details needed for the doctor's notification
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
                        COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,
                        DATEDIFF(MINUTE,
                            DATEADD(MINUTE, tt.TurnAroundMinutes, sr.DateTimeReceived),
                            GETDATE()) AS MinutesOverdue
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
                      AND DATEADD(MINUTE, tt.TurnAroundMinutes, sr.DateTimeReceived) < GETDATE()
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
                    TempData["ErrorMessage"] = "That test is no longer overdue or could not be matched to this request. Refresh the page.";
                    return RedirectToAction(nameof(Escalations));
                }

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
            var result = await Reports(startDate, endDate, cancellationToken);

            if (result is not ViewResult viewResult ||
                viewResult.Model is not TatPerformanceReportViewModel report)
            {
                return BadRequest("Could not generate the report.");
            }

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
                var samples = await _labTechnicianData
                    .GetReceivedSamplesAsync(cancellationToken);


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
                var result = await _labTechnicianData.SelectTestAsync(
                    sampleReceiptId,
                    testTypeId,
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
            string barcode,
            CancellationToken cancellationToken)
        {
            if (testRequestId <= 0 || string.IsNullOrWhiteSpace(barcode))
            {
                TempData["ErrorMessage"] =
                    "A valid request and barcode are required.";

                return RedirectToAction(nameof(ReceiveSamples));
            }

            try
            {
                var received = await _labTechnicianData.ReceiveSampleAsync(
                    testRequestId,
                    barcode,
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
                        : "Sample not found. Refresh tusing System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\nusing System.Threading;\r\nusing System.Threading.Tasks;\r\nusing Dapper;\r\nusing Microsoft.AspNetCore.Mvc;\r\nusing Microsoft.Data.SqlClient;\r\nusing Microsoft.Extensions.Configuration;\r\nusing WebApplication2.Models;\r\nusing WebApplication2.Services;\r\n\r\nnamespace WebApplication2.Controllers\r\n{\r\n    public sealed class StatUrgentTestViewModel\r\n    {\r\n        public int TestRequestID { get; set; }\r\n        public int SampleReceiptID { get; set; }\r\n        public int TestTypeID { get; set; }\r\n        public int TestResultID { get; set; }\r\n        public int PatientID { get; set; }\r\n        public string Barcode { get; set; } = string.Empty;\r\n        public DateTime? RequestDate { get; set; }\r\n        public string Urgency { get; set; } = string.Empty;\r\n        public string RequestStatus { get; set; } = string.Empty;\r\n        public string TestName { get; set; } = string.Empty;\r\n        public int TurnAroundMinutes { get; set; }\r\n        public DateTime? DateTimeReceived { get; set; }\r\n        public string PatientName { get; set; } = string.Empty;\r\n        public string PatientNumber { get; set; } = string.Empty;\r\n        public string ResultStatus { get; set; } = string.Empty;\r\n        public DateTime? CompletionDateTime { get; set; }\r\n        public string LatestDecision { get; set; } = string.Empty;\r\n        public string Stage { get; set; } = string.Empty;\r\n        public DateTime? DueDateTime { get; set; }\r\n        public string TatStatus { get; set; } = string.Empty;\r\n    }\r\n\r\n    public sealed class TestDetailViewModel\r\n    {\r\n        public int TestRequestID { get; set; }\r\n        public int SampleReceiptID { get; set; }\r\n        public int PatientID { get; set; }\r\n        public string RequestNumber => $\"REQ-{TestRequestID:D5}\";\r\n        public string Barcode { get; set; } = string.Empty;\r\n        public string SampleType { get; set; } = \"Not specified\";\r\n        public DateTime? RequestDate { get; set; }\r\n        public DateTime? DateTimeReceived { get; set; }\r\n        public string Urgency { get; set; } = string.Empty;\r\n        public string RequestStatus { get; set; } = string.Empty;\r\n        public string PatientName { get; set; } = string.Empty;\r\n        public string PatientNumber { get; set; } = string.Empty;\r\n        public string DoctorName { get; set; } = string.Empty;\r\n        public string RequestedTypeText { get; set; } = string.Empty;\r\n        public List<string> RequestedTests { get; set; } = new();\r\n        public bool HasActiveSample { get; set; }\r\n        public bool IsRejectedSample { get; set; }\r\n        public bool IsSelectedTest { get; set; }\r\n        public bool HasResult { get; set; }\r\n        public bool IsComplete { get; set; }\r\n        public int TestTypeID { get; set; }\r\n        public string SelectedTestName { get; set; } = string.Empty;\r\n        public string StageMessage { get; set; } = string.Empty;\r\n    }\r\n\r\n    public class LabTechnicianController : Controller\r\n    {\r\n        private readonly LabTechnicianDataService _labTechnicianData;\r\n        private readonly IConfiguration _configuration;\r\n        private readonly IWebHostEnvironment _environment;\r\n\r\n        public LabTechnicianController(\r\n            LabTechnicianDataService labTechnicianData,\r\n            IConfiguration configuration,\r\n            IWebHostEnvironment environment)\r\n        {\r\n            _labTechnicianData = labTechnicianData;\r\n            _configuration = configuration;\r\n            _environment = environment;\r\n        }\r\n\r\n        // Dashboard\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> Dashboard(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var connectionString =\r\n                    _configuration.GetConnectionString(\"DefaultConnection\");\r\n\r\n                if (string.IsNullOrWhiteSpace(connectionString))\r\n                {\r\n                    throw new InvalidOperationException(\r\n                        \"The DefaultConnection connection string is missing.\");\r\n                }\r\n\r\n                const string statsSql = \"\"\"\r\n                    SELECT\r\n                        (\r\n                            SELECT COUNT(*)\r\n                            FROM dbo.TestRequest tr\r\n                            WHERE COALESCE(tr.Status, '') NOT IN\r\n                                ('Cancelled', 'Canceled', 'Completed',\r\n                                 'Awaiting Selection')\r\n                              AND NOT EXISTS\r\n                              (\r\n                                  SELECT 1\r\n                                  FROM dbo.SampleReceipt sr\r\n                                  WHERE sr.TestRequestID = tr.TestRequestID\r\n                              )\r\n                        ) AS RequestsAwaitingReceipt,\r\n                        (\r\n                            SELECT COUNT(*)\r\n                            FROM dbo.TestRequest tr\r\n                            WHERE UPPER(LTRIM(RTRIM(\r\n                                COALESCE(tr.Urgency, '')))) IN ('STAT', 'URGENT')\r\n                              AND COALESCE(tr.Status, '') NOT IN\r\n                                ('Cancelled', 'Canceled', 'Completed')\r\n                        ) AS UrgentRequests,\r\n                        (\r\n                            SELECT COUNT(*)\r\n                            FROM dbo.TestResult\r\n                            WHERE CompletionDateTime >= CONVERT(date, GETDATE())\r\n                              AND CompletionDateTime <\r\n                                  DATEADD(day, 1, CONVERT(date, GETDATE()))\r\n                        ) AS ResultsCompletedToday,\r\n                        (\r\n                            SELECT COUNT(*)\r\n                            FROM dbo.TestResult tr\r\n                            WHERE NOT EXISTS\r\n                            (\r\n                                SELECT 1\r\n                                FROM dbo.ResultVerification rv\r\n                                WHERE rv.TestResultID = tr.TestResultID\r\n                                  AND UPPER(LTRIM(RTRIM(\r\n                                      COALESCE(rv.Decision, '')))) = 'APPROVED'\r\n                            )\r\n                        ) AS AwaitingVerification;\r\n                    \"\"\";\r\n\r\n                const string urgentSql = \"\"\"\r\n                    SELECT TOP (6)\r\n                        tr.TestRequestID,\r\n                        COALESCE(tr.barcode, '') AS Barcode,\r\n                        COALESCE(tr.Type, '') AS TestType,\r\n                        COALESCE(tr.Urgency, '') AS Urgency,\r\n                        COALESCE(tr.Status, '') AS Status,\r\n                        tr.RequestDate,\r\n                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name)))\r\n                            AS PatientName\r\n                    FROM dbo.TestRequest tr\r\n                    INNER JOIN dbo.Patient p\r\n                        ON p.PatientID = tr.PatientID\r\n                    INNER JOIN dbo.[User] u\r\n                        ON u.UserID = p.UserID\r\n                    WHERE UPPER(LTRIM(RTRIM(\r\n                        COALESCE(tr.Urgency, '')))) IN ('STAT', 'URGENT')\r\n                      AND COALESCE(tr.Status, '') NOT IN\r\n                        ('Cancelled', 'Canceled', 'Completed')\r\n                    ORDER BY\r\n                        CASE\r\n                            WHEN UPPER(LTRIM(RTRIM(\r\n                                COALESCE(tr.Urgency, '')))) = 'STAT'\r\n                            THEN 0 ELSE 1\r\n                        END,\r\n                        tr.RequestDate ASC;\r\n                    \"\"\";\r\n\r\n                await using var connection =\r\n                    new SqlConnection(connectionString);\r\n\r\n                await connection.OpenAsync(cancellationToken);\r\n\r\n                var stats = await connection.QuerySingleAsync<DashboardStats>(\r\n                    new CommandDefinition(\r\n                        statsSql,\r\n                        cancellationToken: cancellationToken));\r\n\r\n                var urgentRequests =\r\n                    (await connection.QueryAsync<DashboardUrgentItem>(\r\n                        new CommandDefinition(\r\n                            urgentSql,\r\n                            cancellationToken: cancellationToken)))\r\n                    .ToList();\r\n\r\n                var assignedTests = await _labTechnicianData\r\n                    .GetAssignedTestsAsync(cancellationToken);\r\n\r\n                var assignedTestsCount = assignedTests.Count(test =>\r\n                    !_labTechnicianData.IsTestCompleted(\r\n                        test.SampleReceiptID,\r\n                        test.TestTypeID));\r\n\r\n                ViewBag.Stats = stats;\r\n                ViewBag.AssignedTestsCount = assignedTestsCount;\r\n                ViewBag.RequestsAwaitingReceipt = stats.RequestsAwaitingReceipt;\r\n                ViewBag.UrgentRequestsCount = stats.UrgentRequests;\r\n                ViewBag.ResultsCompletedToday = stats.ResultsCompletedToday;\r\n                ViewBag.AwaitingVerification = stats.AwaitingVerification;\r\n                ViewBag.urgentRequests = urgentRequests;\r\n                ViewBag.UrgentRequests = urgentRequests;\r\n\r\n                return View();\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"Dashboard failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load dashboard statistics. Check the application logs.\";\r\n\r\n                ViewBag.Stats = new DashboardStats();\r\n                ViewBag.AssignedTestsCount = 0;\r\n                ViewBag.RequestsAwaitingReceipt = 0;\r\n                ViewBag.UrgentRequestsCount = 0;\r\n                ViewBag.ResultsCompletedToday = 0;\r\n                ViewBag.AwaitingVerification = 0;\r\n                ViewBag.urgentRequests = new List<DashboardUrgentItem>();\r\n                ViewBag.UrgentRequests = new List<DashboardUrgentItem>();\r\n\r\n                return View();\r\n            }\r\n        }\r\n\r\n        private sealed class DashboardStats\r\n        {\r\n            public int RequestsAwaitingReceipt { get; set; }\r\n            public int UrgentRequests { get; set; }\r\n            public int ResultsCompletedToday { get; set; }\r\n            public int AwaitingVerification { get; set; }\r\n        }\r\n\r\n        private sealed class DashboardUrgentItem\r\n        {\r\n            public int TestRequestID { get; set; }\r\n            public string Barcode { get; set; } = string.Empty;\r\n            public string TestType { get; set; } = string.Empty;\r\n            public string Urgency { get; set; } = string.Empty;\r\n            public string Status { get; set; } = string.Empty;\r\n            public DateTime? RequestDate { get; set; }\r\n            public string PatientName { get; set; } = string.Empty;\r\n        }\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> StatUrgent(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var connectionString =\r\n                    _configuration.GetConnectionString(\"DefaultConnection\");\r\n\r\n                if (string.IsNullOrWhiteSpace(connectionString))\r\n                {\r\n                    throw new InvalidOperationException(\r\n                        \"The DefaultConnection connection string is missing.\");\r\n                }\r\n\r\n                const string sql = \"\"\"\r\n                    SELECT\r\n                        tr.TestRequestID,\r\n                        COALESCE(sr.SampleReceiptID, 0) AS SampleReceiptID,\r\n                        COALESCE(tt.TestTypeID, 0) AS TestTypeID,\r\n                        COALESCE(sr.Barcode, tr.Barcode, '') AS Barcode,\r\n                        tr.RequestDate,\r\n                        COALESCE(tr.Urgency, '') AS Urgency,\r\n                        COALESCE(tr.Status, '') AS RequestStatus,\r\n                        COALESCE(tt.TestName, LTRIM(RTRIM(requested.value)), '') AS TestName,\r\n                        COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,\r\n                        sr.DateTimeReceived,\r\n                        p.PatientID,\r\n                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,\r\n                        COALESCE(u.IDNumber, '') AS PatientNumber,\r\n                        COALESCE(latestResult.TestResultID, 0) AS TestResultID,\r\n                        COALESCE(latestResult.ResultStatus, '') AS ResultStatus,\r\n                        latestResult.CompletionDateTime,\r\n                        COALESCE(latestDecision.Decision, '') AS LatestDecision,\r\n                        CASE\r\n                            WHEN COALESCE(latestDecision.Decision, '') IN\r\n                                ('Returned', 'Return', 'Rejected')\r\n                                THEN 'Review'\r\n                            WHEN COALESCE(latestDecision.Decision, '') IN\r\n                                ('Approved', 'Verified')\r\n                                THEN 'Complete'\r\n                            WHEN latestResult.TestResultID IS NOT NULL\r\n                                THEN 'Verification'\r\n                            WHEN COALESCE(tr.Status, '') IN ('Completed', 'Verified')\r\n                                THEN 'Complete'\r\n                            WHEN COALESCE(tr.Status, '') IN ('In Progress', 'Processing')\r\n                                THEN 'Processing'\r\n                            WHEN sr.SampleReceiptID IS NOT NULL\r\n                                THEN 'Sample Received'\r\n                            ELSE 'Awaiting Sample'\r\n                        END AS Stage\r\n                    FROM dbo.TestRequest tr\r\n                    INNER JOIN dbo.Patient p\r\n                        ON p.PatientID = tr.PatientID\r\n                    INNER JOIN dbo.[User] u\r\n                        ON u.UserID = p.UserID\r\n                    CROSS APPLY STRING_SPLIT(\r\n                        REPLACE(\r\n                            REPLACE(\r\n                                REPLACE(\r\n                                    REPLACE(COALESCE(tr.Type, ''), ';', ','),\r\n                                    '+', ','\r\n                                ),\r\n                                CHAR(13), ','\r\n                            ),\r\n                            CHAR(10), ','\r\n                        ),\r\n                        ','\r\n                    ) requested\r\n                    LEFT JOIN dbo.TestType tt\r\n                        ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))\r\n                    LEFT JOIN dbo.SampleReceipt sr\r\n                        ON sr.TestRequestID = tr.TestRequestID\r\n                       AND NOT EXISTS\r\n                       (\r\n                           SELECT 1\r\n                           FROM dbo.SampleReceiptRejection rejected\r\n                           WHERE rejected.SampleReceiptID = sr.SampleReceiptID\r\n                       )\r\n                    OUTER APPLY\r\n                    (\r\n                        SELECT TOP (1)\r\n                            result.TestResultID,\r\n                            COALESCE(result.Status, '') AS ResultStatus,\r\n                            result.CompletionDateTime\r\n                        FROM dbo.TestResult result\r\n                        WHERE result.TestRequestID = tr.TestRequestID\r\n                          AND result.TestTypeID = tt.TestTypeID\r\n                        ORDER BY result.CompletionDateTime DESC, result.TestResultID DESC\r\n                    ) latestResult\r\n                    OUTER APPLY\r\n                    (\r\n                        SELECT TOP (1)\r\n                            verification.Decision\r\n                        FROM dbo.ResultVerification verification\r\n                        WHERE verification.TestResultID = latestResult.TestResultID\r\n                        ORDER BY verification.VerificationDateTime DESC,\r\n                                 verification.ResultVerificationID DESC\r\n                    ) latestDecision\r\n                    WHERE UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, ''))))\r\n                        IN ('STAT', 'URGENT')\r\n                      AND COALESCE(tr.Status, '') NOT IN\r\n                        ('Cancelled', 'Canceled')\r\n                    ORDER BY\r\n                        CASE\r\n                            WHEN UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, '')))) = 'STAT'\r\n                                THEN 0\r\n                            ELSE 1\r\n                        END,\r\n                        tr.RequestDate ASC,\r\n                        tr.TestRequestID,\r\n                        tt.TestName;\r\n                    \"\"\";\r\n\r\n                await using var connection = new SqlConnection(connectionString);\r\n\r\n                var rows = (await connection.QueryAsync<StatUrgentTestViewModel>(\r\n                    new CommandDefinition(\r\n                        sql,\r\n                        cancellationToken: cancellationToken))).ToList();\r\n\r\n                var assignedTests = await _labTechnicianData\r\n                    .GetAssignedTestsAsync(cancellationToken);\r\n\r\n                var assignedKeys = new HashSet<string>(\r\n                    assignedTests\r\n                        .Where(test => !_labTechnicianData.IsTestCompleted(\r\n                            test.SampleReceiptID, test.TestTypeID))\r\n                        .Select(test => $\"{test.SampleReceiptID}:{test.TestTypeID}\"),\r\n                    StringComparer.OrdinalIgnoreCase);\r\n\r\n                var now = DateTime.Now;\r\n\r\n                foreach (var test in rows)\r\n                {\r\n                    if (test.SampleReceiptID > 0 && test.TestTypeID > 0 &&\r\n                        assignedKeys.Contains($\"{test.SampleReceiptID}:{test.TestTypeID}\") &&\r\n                        test.TestResultID == 0)\r\n                    {\r\n                        test.Stage = \"Processing\";\r\n                    }\r\n\r\n                    if (test.DateTimeReceived.HasValue &&\r\n                        test.TurnAroundMinutes > 0 &&\r\n                        test.TestResultID == 0 &&\r\n                        !string.Equals(test.Stage, \"Complete\",\r\n                            StringComparison.OrdinalIgnoreCase))\r\n                    {\r\n                        test.DueDateTime = test.DateTimeReceived.Value\r\n                            .AddMinutes(test.TurnAroundMinutes);\r\n\r\n                        var minutesRemaining =\r\n                            (test.DueDateTime.Value - now).TotalMinutes;\r\n\r\n                        test.TatStatus = minutesRemaining < 0\r\n                            ? \"Breached\"\r\n                            : minutesRemaining <= Math.Max(15, test.TurnAroundMinutes * 0.2)\r\n                                ? \"At Risk\"\r\n                                : \"Within TAT\";\r\n                    }\r\n                    else if (test.TestResultID > 0 ||\r\n                             string.Equals(test.Stage, \"Complete\",\r\n                                 StringComparison.OrdinalIgnoreCase))\r\n                    {\r\n                        test.TatStatus = \"Complete\";\r\n                    }\r\n                    else\r\n                    {\r\n                        test.TatStatus = \"Unknown\";\r\n                    }\r\n                }\r\n\r\n                ViewBag.TatBreachedCount = rows.Count(x => x.TatStatus == \"Breached\");\r\n                ViewBag.AtRiskCount = rows.Count(x => x.TatStatus == \"At Risk\");\r\n                ViewBag.ActiveStatCount = rows.Count(x =>\r\n                    string.Equals(x.Urgency, \"STAT\",\r\n                        StringComparison.OrdinalIgnoreCase) &&\r\n                    !string.Equals(x.Stage, \"Complete\",\r\n                        StringComparison.OrdinalIgnoreCase));\r\n                ViewBag.CompletedTodayCount = rows.Count(x =>\r\n                    x.CompletionDateTime.HasValue &&\r\n                    x.CompletionDateTime.Value.Date == now.Date);\r\n                var completedToday = rows\r\n                    .Where(x => x.CompletionDateTime.HasValue &&\r\n                                x.DateTimeReceived.HasValue &&\r\n                                x.CompletionDateTime.Value.Date == now.Date &&\r\n                                x.CompletionDateTime.Value >= x.DateTimeReceived.Value)\r\n                    .Select(x => (x.CompletionDateTime!.Value -\r\n                                  x.DateTimeReceived!.Value).TotalMinutes)\r\n                    .ToList();\r\n                ViewBag.AverageTatToday = completedToday.Count == 0\r\n                    ? \"—\"\r\n                    : $\"{Math.Round(completedToday.Average())}m\";\r\n\r\n                return View(rows);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"StatUrgent failed: {ex}\");\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load STAT and urgent tests from the database. Check the application logs.\";\r\n\r\n                ViewBag.TatBreachedCount = 0;\r\n                ViewBag.AtRiskCount = 0;\r\n                ViewBag.ActiveStatCount = 0;\r\n                ViewBag.CompletedTodayCount = 0;\r\n                ViewBag.AverageTatToday = \"—\";\r\n\r\n                return View(new List<StatUrgentTestViewModel>());\r\n            }\r\n        }\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> TestDetail(\r\n            int id,\r\n            int testRequestId = 0,\r\n            int sampleReceiptId = 0,\r\n            int testTypeId = 0,\r\n            CancellationToken cancellationToken = default)\r\n        {\r\n            // Support the common route/query names used by existing links.\r\n            var requestId = testRequestId > 0 ? testRequestId : id;\r\n            if (requestId <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] = \"Select a valid test request first.\";\r\n                return RedirectToAction(nameof(ReceiveSamples));\r\n            }\r\n\r\n            try\r\n            {\r\n                const string sql = \"\"\"\r\n                    SELECT\r\n                        tr.TestRequestID,\r\n                        tr.PatientID,\r\n                        tr.RequestDate,\r\n                        COALESCE(tr.Urgency, '') AS Urgency,\r\n                        COALESCE(tr.Status, '') AS RequestStatus,\r\n                        COALESCE(tr.Type, '') AS RequestedTypeText,\r\n                        COALESCE(tr.Barcode, '') AS RequestBarcode,\r\n                        LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,\r\n                        COALESCE(u.IDNumber, '') AS PatientNumber,\r\n                        LTRIM(RTRIM(CONCAT(\r\n                            COALESCE(doctor.Surname, ''),\r\n                            CASE WHEN doctor.Surname IS NOT NULL THEN ', ' ELSE '' END,\r\n                            COALESCE(doctor.Name, '')\r\n                        ))) AS DoctorName,\r\n                        COALESCE(sr.SampleReceiptID, 0) AS SampleReceiptID,\r\n                        COALESCE(sr.Barcode, tr.Barcode, '') AS SampleBarcode,\r\n                        sr.DateTimeReceived,\r\n                        CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejectedSample\r\n                    FROM dbo.TestRequest tr\r\n                    INNER JOIN dbo.Patient p ON p.PatientID = tr.PatientID\r\n                    INNER JOIN dbo.[User] u ON u.UserID = p.UserID\r\n                    LEFT JOIN dbo.Doctor d ON d.DoctorID = tr.DoctorID\r\n                    LEFT JOIN dbo.[User] doctor ON doctor.UserID = d.UserID\r\n                    OUTER APPLY\r\n                    (\r\n                        SELECT TOP (1) receipt.*\r\n                        FROM dbo.SampleReceipt receipt\r\n                        WHERE receipt.TestRequestID = tr.TestRequestID\r\n                          AND (@SampleReceiptID = 0 OR receipt.SampleReceiptID = @SampleReceiptID)\r\n                        ORDER BY\r\n                            CASE WHEN EXISTS\r\n                            (\r\n                                SELECT 1\r\n                                FROM dbo.SampleReceiptRejection r\r\n                                WHERE r.SampleReceiptID = receipt.SampleReceiptID\r\n                            ) THEN 1 ELSE 0 END,\r\n                            receipt.DateTimeReceived DESC,\r\n                            receipt.SampleReceiptID DESC\r\n                    ) sr\r\n                    LEFT JOIN dbo.SampleReceiptRejection rejection\r\n                        ON rejection.SampleReceiptID = sr.SampleReceiptID\r\n                    WHERE tr.TestRequestID = @TestRequestID;\r\n                    \"\"\";\r\n\r\n                var connectionString = _configuration.GetConnectionString(\"DefaultConnection\");\r\n                if (string.IsNullOrWhiteSpace(connectionString))\r\n                    throw new InvalidOperationException(\"The DefaultConnection connection string is missing.\");\r\n\r\n                await using var connection = new SqlConnection(connectionString);\r\n                var row = await connection.QuerySingleOrDefaultAsync<TestDetailQueryRow>(\r\n                    new CommandDefinition(\r\n                        sql,\r\n                        new { TestRequestID = requestId, SampleReceiptID = sampleReceiptId },\r\n                        cancellationToken: cancellationToken));\r\n\r\n                if (row == null)\r\n                {\r\n                    TempData[\"ErrorMessage\"] = \"The selected test request could not be found.\";\r\n                    return RedirectToAction(nameof(ReceiveSamples));\r\n                }\r\n\r\n                var model = new TestDetailViewModel\r\n                {\r\n                    TestRequestID = row.TestRequestID,\r\n                    SampleReceiptID = row.SampleReceiptID,\r\n                    PatientID = row.PatientID,\r\n                    Barcode = row.SampleBarcode,\r\n                    RequestDate = row.RequestDate,\r\n                    DateTimeReceived = row.DateTimeReceived,\r\n                    Urgency = row.Urgency,\r\n                    RequestStatus = row.RequestStatus,\r\n                    PatientName = row.PatientName,\r\n                    PatientNumber = row.PatientNumber,\r\n                    DoctorName = string.IsNullOrWhiteSpace(row.DoctorName) ? \"Not specified\" : row.DoctorName,\r\n                    RequestedTypeText = row.RequestedTypeText,\r\n                    HasActiveSample = row.SampleReceiptID > 0 && !row.IsRejectedSample,\r\n                    IsRejectedSample = row.IsRejectedSample\r\n                };\r\n\r\n                model.RequestedTests = (row.RequestedTypeText ?? string.Empty)\r\n                    .Split(new[] { ',', ';', '+', '\r', '\n' },\r\n                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)\r\n                    .Distinct(StringComparer.OrdinalIgnoreCase)\r\n                    .ToList();\r\n\r\n                if (testTypeId > 0 && model.SampleReceiptID > 0 && model.HasActiveSample)\r\n                {\r\n                    const string selectedSql = \"\"\"\r\n                        SELECT\r\n                            tt.TestTypeID,\r\n                            COALESCE(tt.TestName, '') AS TestName,\r\n                            COALESCE(latest.TestResultID, 0) AS TestResultID,\r\n                            COALESCE(latest.Decision, '') AS LatestDecision\r\n                        FROM dbo.TestType tt\r\n                        OUTER APPLY\r\n                        (\r\n                            SELECT TOP (1)\r\n                                result.TestResultID,\r\n                                verification.Decision\r\n                            FROM dbo.TestResult result\r\n                            OUTER APPLY\r\n                            (\r\n                                SELECT TOP (1) rv.Decision\r\n                                FROM dbo.ResultVerification rv\r\n                                WHERE rv.TestResultID = result.TestResultID\r\n                                ORDER BY rv.VerificationDateTime DESC, rv.ResultVerificationID DESC\r\n                            ) verification\r\n                            WHERE result.TestRequestID = @TestRequestID\r\n                              AND result.TestTypeID = tt.TestTypeID\r\n                            ORDER BY result.CompletionDateTime DESC, result.TestResultID DESC\r\n                        ) latest\r\n                        WHERE tt.TestTypeID = @TestTypeID;\r\n                        \"\"\";\r\n\r\n                    var selected = await connection.QuerySingleOrDefaultAsync<TestDetailSelectedRow>(\r\n                        new CommandDefinition(\r\n                            selectedSql,\r\n                            new { TestRequestID = requestId, TestTypeID = testTypeId },\r\n                            cancellationToken: cancellationToken));\r\n\r\n                    if (selected != null &&\r\n                        model.RequestedTests.Contains(selected.TestName, StringComparer.OrdinalIgnoreCase))\r\n                    {\r\n                        model.IsSelectedTest = true;\r\n                        model.TestTypeID = selected.TestTypeID;\r\n                        model.SelectedTestName = selected.TestName;\r\n                        model.HasResult = selected.TestResultID > 0;\r\n                        model.IsComplete = string.Equals(selected.LatestDecision, \"Approved\", StringComparison.OrdinalIgnoreCase)\r\n                            || string.Equals(selected.LatestDecision, \"Verified\", StringComparison.OrdinalIgnoreCase);\r\n\r\n                        model.StageMessage = model.IsComplete\r\n                            ? \"This test has already been verified.\"\r\n                            : model.HasResult && (string.Equals(selected.LatestDecision, \"Returned\", StringComparison.OrdinalIgnoreCase)\r\n                                || string.Equals(selected.LatestDecision, \"Return\", StringComparison.OrdinalIgnoreCase)\r\n                                || string.Equals(selected.LatestDecision, \"Rejected\", StringComparison.OrdinalIgnoreCase))\r\n                                ? \"This result was returned and is available for recapture.\"\r\n                                : model.HasResult\r\n                                    ? \"This test has a result awaiting verification.\"\r\n                                    : \"This test is selected and ready for result capture.\";\r\n                    }\r\n                }\r\n\r\n                return View(model);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"TestDetail failed: {ex}\");\r\n                TempData[\"ErrorMessage\"] = \"Could not load the selected test details. Check the application logs.\";\r\n                return RedirectToAction(nameof(ReceiveSamples));\r\n            }\r\n        }\r\n\r\n        private sealed class TestDetailQueryRow\r\n        {\r\n            public int TestRequestID { get; set; }\r\n            public int PatientID { get; set; }\r\n            public DateTime? RequestDate { get; set; }\r\n            public string Urgency { get; set; } = string.Empty;\r\n            public string RequestStatus { get; set; } = string.Empty;\r\n            public string RequestedTypeText { get; set; } = string.Empty;\r\n            public string PatientName { get; set; } = string.Empty;\r\n            public string PatientNumber { get; set; } = string.Empty;\r\n            public string DoctorName { get; set; } = string.Empty;\r\n            public int SampleReceiptID { get; set; }\r\n            public string SampleBarcode { get; set; } = string.Empty;\r\n            public DateTime? DateTimeReceived { get; set; }\r\n            public bool IsRejectedSample { get; set; }\r\n        }\r\n\r\n        private sealed class TestDetailSelectedRow\r\n        {\r\n            public int TestTypeID { get; set; }\r\n            public string TestName { get; set; } = string.Empty;\r\n            public int TestResultID { get; set; }\r\n            public string LatestDecision { get; set; } = string.Empty;\r\n        }\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> Escalations(\r\n    CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var escalations = await _labTechnicianData\r\n                    .GetActiveEscalationsAsync(cancellationToken);\r\n\r\n                return View(escalations);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"Escalations failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    $\"Escalations error: {ex.Message}\";\r\n\r\n                return View(new List<LabEscalationViewModel>());\r\n            }\r\n        }\r\n\r\n        [HttpGet]\r\n        public IActionResult Reports() => View();\r\n\r\n        [HttpGet]\r\n        public IActionResult Profile() => View();\r\n\r\n        [HttpGet]\r\n        public IActionResult EditProfile() => View();\r\n\r\n        [HttpGet]\r\n        public IActionResult ChangePassword() => View();\r\n\r\n        // Sample receipt and test selection\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> AwaitingSelection(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var samples = await _labTechnicianData\r\n                    .GetReceivedSamplesAsync(cancellationToken);\r\n\r\n                ViewBag.TestTypes = await _labTechnicianData\r\n                    .GetAvailableTestTypesAsync(cancellationToken);\r\n\r\n                ViewBag.Consumables = await _labTechnicianData\r\n                    .GetConsumableStockAsync(cancellationToken);\r\n\r\n                return View(samples.Where(x => !x.IsRejected).ToList());\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"AwaitingSelection failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load Awaiting Selection data. Check the application logs.\";\r\n\r\n                ViewBag.TestTypes = new List<LabTechnicianQueueItem>();\r\n                ViewBag.Consumables = new List<ConsumableStockItem>();\r\n\r\n                return View(new List<LabTechnicianQueueItem>());\r\n            }\r\n        }\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> SelectTest(\r\n            int sampleReceiptId,\r\n            int testTypeId,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (sampleReceiptId <= 0 || testTypeId <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] = \"Select a valid sample and test.\";\r\n                return RedirectToAction(nameof(AwaitingSelection));\r\n            }\r\n\r\n            try\r\n            {\r\n                var result = await _labTechnicianData.SelectTestAsync(\r\n                    sampleReceiptId,\r\n                    testTypeId,\r\n                    cancellationToken);\r\n\r\n                TempData[result.Success ? \"SuccessMessage\" : \"ErrorMessage\"] =\r\n                    result.Message;\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"SelectTest failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"The test could not be selected. Check the application logs.\";\r\n            }\r\n\r\n            return RedirectToAction(nameof(AwaitingSelection));\r\n        }\r\n\r\n        // Assigned tests and result capture\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> AssignedTests(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var tests = await _labTechnicianData\r\n                    .GetAssignedTestsAsync(cancellationToken);\r\n\r\n                var completedTests = new HashSet<string>();\r\n\r\n                foreach (var test in tests)\r\n                {\r\n                    if (_labTechnicianData.IsTestCompleted(\r\n                        test.SampleReceiptID,\r\n                        test.TestTypeID))\r\n                    {\r\n                        completedTests.Add(\r\n                            $\"{test.SampleReceiptID}-{test.TestTypeID}\");\r\n                    }\r\n                }\r\n\r\n                ViewBag.CompletedTests = completedTests;\r\n                return View(tests);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"AssignedTests failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] = \"Could not load assigned tests.\";\r\n                ViewBag.CompletedTests = new HashSet<string>();\r\n\r\n                return View(new List<LabTechnicianQueueItem>());\r\n            }\r\n        }\r\n\r\n        [HttpGet]\r\n        public IActionResult CaptureResultsLookup()\r\n        {\r\n            return View();\r\n        }\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> CaptureResultsLookup(\r\n            string registrationNumber,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (string.IsNullOrWhiteSpace(registrationNumber))\r\n            {\r\n                ModelState.AddModelError(\r\n                    nameof(registrationNumber),\r\n                    \"Enter a patient registration number.\");\r\n\r\n                return View();\r\n            }\r\n\r\n            registrationNumber = registrationNumber.Trim();\r\n\r\n            try\r\n            {\r\n                var matchingTests = await _labTechnicianData\r\n                    .FindPatientTestsAsync(\r\n                        registrationNumber,\r\n                        cancellationToken);\r\n\r\n                ViewBag.RegistrationNumber = registrationNumber;\r\n\r\n                if (matchingTests.Count == 0)\r\n                {\r\n                    ViewBag.LookupMessage =\r\n                        \"No assigned tests were found for that registration number.\";\r\n\r\n                    return View();\r\n                }\r\n\r\n                ViewBag.MatchingTests = matchingTests;\r\n                return View();\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"CaptureResultsLookup failed: {ex}\");\r\n\r\n                ViewBag.LookupMessage =\r\n                    \"The patient lookup failed. Check the application logs.\";\r\n                ViewBag.RegistrationNumber = registrationNumber;\r\n\r\n                return View();\r\n            }\r\n        }\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> CaptureResults(\r\n            int sampleReceiptId,\r\n            int testTypeId,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (sampleReceiptId <= 0 || testTypeId <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Choose an individual assigned test first.\";\r\n\r\n                return RedirectToAction(nameof(AssignedTests));\r\n            }\r\n\r\n            try\r\n            {\r\n                var model = await _labTechnicianData.GetCaptureTestAsync(\r\n                    sampleReceiptId,\r\n                    testTypeId,\r\n                    cancellationToken);\r\n\r\n                if (model == null)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"The selected test could not be loaded. It may not be assigned, may have been rejected, or may already be completed.\";\r\n\r\n                    return RedirectToAction(nameof(AssignedTests));\r\n                }\r\n\r\n                return View(model);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"CaptureResults GET failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load the selected test. Check the application logs.\";\r\n\r\n                return RedirectToAction(nameof(AssignedTests));\r\n            }\r\n        }\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> CaptureResults(\r\n            CaptureResultsViewModel model,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (model.SampleReceiptID <= 0 || model.TestTypeID <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"The selected sample or test is invalid.\";\r\n\r\n                return RedirectToAction(nameof(AssignedTests));\r\n            }\r\n\r\n            if (string.IsNullOrWhiteSpace(model.ResultValue))\r\n            {\r\n                ModelState.AddModelError(\r\n                    nameof(model.ResultValue),\r\n                    \"Enter a result before saving.\");\r\n\r\n                return await ReloadCaptureFormAsync(model, cancellationToken);\r\n            }\r\n\r\n            try\r\n            {\r\n                var saveResult = await _labTechnicianData.SaveTestResultAsync(\r\n                    model.SampleReceiptID,\r\n                    model.TestTypeID,\r\n                    model.ResultValue,\r\n                    model.TechnicianNotes,\r\n                    cancellationToken);\r\n\r\n                if (!saveResult.Success)\r\n                {\r\n                    ModelState.AddModelError(string.Empty, saveResult.Message);\r\n                    return await ReloadCaptureFormAsync(model, cancellationToken);\r\n                }\r\n\r\n                TempData[\"SuccessMessage\"] = saveResult.Message;\r\n                return RedirectToAction(nameof(AssignedTests));\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"CaptureResults POST failed: {ex}\");\r\n\r\n                ModelState.AddModelError(\r\n                    string.Empty,\r\n                    \"Could not save the result. Check the application logs.\");\r\n\r\n                return await ReloadCaptureFormAsync(model, cancellationToken);\r\n            }\r\n        }\r\n\r\n        private async Task<IActionResult> ReloadCaptureFormAsync(\r\n            CaptureResultsViewModel submittedModel,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var freshModel = await _labTechnicianData.GetCaptureTestAsync(\r\n                    submittedModel.SampleReceiptID,\r\n                    submittedModel.TestTypeID,\r\n                    cancellationToken);\r\n\r\n                if (freshModel == null)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"The selected test is no longer available.\";\r\n\r\n                    return RedirectToAction(nameof(AssignedTests));\r\n                }\r\n\r\n                freshModel.ResultValue = submittedModel.ResultValue;\r\n                freshModel.TechnicianNotes = submittedModel.TechnicianNotes;\r\n\r\n                return View(nameof(CaptureResults), freshModel);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"ReloadCaptureForm failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] = \"Could not reload the capture form.\";\r\n                return RedirectToAction(nameof(AssignedTests));\r\n            }\r\n        }\r\n\r\n        // Result verification\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> VerificationQueue(\r\n            CancellationToken cancellationToken)\r\n        {\r\n\r\n            try\r\n            {\r\n                var results = await _labTechnicianData\r\n                    .GetVerificationQueueAsync(cancellationToken);\r\n\r\n                var pendingResults = results\r\n                    .Where(x =>\r\n                        !string.Equals(x.LatestVerificationDecision, \"Approved\",\r\n                            StringComparison.OrdinalIgnoreCase)\r\n                        && !string.Equals(x.LatestVerificationDecision, \"Returned\",\r\n                            StringComparison.OrdinalIgnoreCase)\r\n                        && !string.Equals(x.LatestVerificationDecision, \"Return\",\r\n                            StringComparison.OrdinalIgnoreCase)\r\n                        && !string.Equals(x.LatestVerificationDecision, \"Rejected\",\r\n                            StringComparison.OrdinalIgnoreCase))\r\n                    .ToList();\r\n\r\n                return View(pendingResults);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"VerificationQueue failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load the verification queue. Check the application logs.\";\r\n\r\n                return View(new List<VerificationQueueItemViewModel>());\r\n            }\r\n        }\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> VerifyResults(\r\n            int testResultId,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (testResultId <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Select a valid test result to verify.\";\r\n\r\n                return RedirectToAction(nameof(VerificationQueue));\r\n            }\r\n\r\n            try\r\n            {\r\n                var result = await _labTechnicianData\r\n                    .GetVerificationResultAsync(\r\n                        testResultId,\r\n                        cancellationToken);\r\n\r\n                if (result == null)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"That result could not be found in the current verification session.\";\r\n\r\n                    return RedirectToAction(nameof(VerificationQueue));\r\n                }\r\n\r\n                return View(result);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"VerifyResults failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load the selected result. Check the application logs.\";\r\n\r\n                return RedirectToAction(nameof(VerificationQueue));\r\n            }\r\n        }\r\n\r\n\r\n\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> SubmitVerificationDecision(\r\n            int testResultId,\r\n            string decision,\r\n            string? returnReason,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (testResultId <= 0)\r\n            {\r\n                TempData[\"ErrorMessage\"] = \"Invalid test result.\";\r\n                return RedirectToAction(nameof(VerificationQueue));\r\n            }\r\n\r\n            var normalizedDecision = decision?.Trim();\r\n\r\n            if (!string.Equals(normalizedDecision, \"Approved\",\r\n                    StringComparison.OrdinalIgnoreCase)\r\n                && !string.Equals(normalizedDecision, \"Returned\",\r\n                    StringComparison.OrdinalIgnoreCase))\r\n            {\r\n                TempData[\"ErrorMessage\"] = \"Select a valid verification decision.\";\r\n                return RedirectToAction(nameof(VerifyResults), new { testResultId });\r\n            }\r\n\r\n            if (string.Equals(normalizedDecision, \"Returned\",\r\n                    StringComparison.OrdinalIgnoreCase)\r\n                && string.IsNullOrWhiteSpace(returnReason))\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"A reason is required when returning a result for recapture.\";\r\n                return RedirectToAction(nameof(VerifyResults), new { testResultId });\r\n            }\r\n\r\n            try\r\n            {\r\n                var result = await _labTechnicianData.GetVerificationResultAsync(\r\n                    testResultId, cancellationToken);\r\n\r\n                if (result == null)\r\n                {\r\n                    TempData[\"ErrorMessage\"] = \"The selected test result was not found.\";\r\n                    return RedirectToAction(nameof(VerificationQueue));\r\n                }\r\n\r\n                var userId = User.FindFirst(\r\n                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;\r\n\r\n\r\n                if (!int.TryParse(userId, out var loggedInUserId))\r\n                {\r\n                    var developmentUserId =\r\n                        _configuration.GetValue<int?>(\r\n                            \"LabTechnician:DevelopmentUserId\");\r\n\r\n                    if (!_environment.IsDevelopment()\r\n                        || developmentUserId is null\r\n                        || developmentUserId.Value <= 0)\r\n                    {\r\n                        TempData[\"ErrorMessage\"] =\r\n                            \"Your login could not be matched to a lab technician.\";\r\n\r\n                        return RedirectToAction(\r\n                            nameof(VerifyResults),\r\n                            new { testResultId });\r\n                    }\r\n\r\n                    loggedInUserId = developmentUserId.Value;\r\n                }\r\n\r\n\r\n                var technicianId = await _labTechnicianData\r\n                    .GetLabTechnicianIdForUserAsync(loggedInUserId, cancellationToken);\r\n\r\n                if (technicianId == null || technicianId.Value <= 0)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"No lab technician profile is linked to your login.\";\r\n                    return RedirectToAction(nameof(VerifyResults), new { testResultId });\r\n                }\r\n\r\n                var saveResult = await _labTechnicianData.SaveVerificationDecisionAsync(\r\n                    testResultId,\r\n                    technicianId.Value,\r\n                    normalizedDecision!,\r\n                    returnReason,\r\n                    cancellationToken);\r\n\r\n                if (!saveResult.Success)\r\n                {\r\n                    Console.Error.WriteLine(\r\n                        $\"Verification save failed for TestResultID {testResultId}: {saveResult.Message}\");\r\n\r\n                    TempData[\"ErrorMessage\"] = saveResult.Message;\r\n                    return RedirectToAction(nameof(VerifyResults), new { testResultId });\r\n                }\r\n\r\n                Console.WriteLine(\r\n                    $\"Verification save succeeded for TestResultID {testResultId}: {saveResult.Message}\");\r\n\r\n                TempData[\"SuccessMessage\"] = saveResult.Message;\r\n\r\n                // After a result is returned, stay on the Verification Queue.\r\n                // The returned result can be opened for recapture from Review Queue.\r\n                return RedirectToAction(nameof(VerificationQueue));\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"SubmitVerificationDecision failed: {ex}\");\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not save the verification decision. Check the application logs.\";\r\n                return RedirectToAction(nameof(VerifyResults), new { testResultId });\r\n            }\r\n        }\r\n\r\n\r\n\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> ReviewQueue(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var results = await _labTechnicianData\r\n                    .GetVerificationQueueAsync(cancellationToken);\r\n\r\n                // Pass all results to the view. ReviewQueue.cshtml must first\r\n                // select the newest result for each request/test type and only\r\n                // then filter for Returned/Return/Rejected. Filtering here first\r\n                // would leave an older returned result visible after recapture.\r\n                return View(results);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"ReviewQueue failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"The Review Queue could not be loaded. Please try again.\";\r\n\r\n                return View(new List<VerificationQueueItemViewModel>());\r\n            }\r\n        }\r\n\r\n        // Receive and reject samples\r\n\r\n        [HttpGet]\r\n        public async Task<IActionResult> ReceiveSamples(\r\n            CancellationToken cancellationToken)\r\n        {\r\n            try\r\n            {\r\n                var pendingRequests = await _labTechnicianData\r\n                    .GetRequestsAwaitingReceiptAsync(cancellationToken);\r\n\r\n                var receivedSamples = await _labTechnicianData\r\n                    .GetReceivedSamplesAsync(cancellationToken);\r\n\r\n                var allRows = pendingRequests\r\n                    .Concat(receivedSamples)\r\n                    .OrderByDescending(x => x.DateTimeReceived.HasValue)\r\n                    .ThenByDescending(x => x.DateTimeReceived)\r\n                    .ThenByDescending(x => x.RequestDate)\r\n                    .ToList();\r\n\r\n                return View(allRows);\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"ReceiveSamples failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not load sample requests. Check the application logs.\";\r\n\r\n                return View(new List<LabTechnicianQueueItem>());\r\n            }\r\n        }\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> ReceiveSample(\r\n            int testRequestId,\r\n            string barcode,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (testRequestId <= 0 || string.IsNullOrWhiteSpace(barcode))\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"A valid request and barcode are required.\";\r\n\r\n                return RedirectToAction(nameof(ReceiveSamples));\r\n            }\r\n\r\n            try\r\n            {\r\n                var received = await _labTechnicianData.ReceiveSampleAsync(\r\n                    testRequestId,\r\n                    barcode,\r\n                    cancellationToken);\r\n\r\n                TempData[received ? \"SuccessMessage\" : \"ErrorMessage\"] =\r\n                    received\r\n                        ? \"Sample received successfully.\"\r\n                        : \"The request could not be found, or this barcode has already been received.\";\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"ReceiveSample failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not receive the sample. Check the application logs.\";\r\n            }\r\n\r\n            return RedirectToAction(nameof(ReceiveSamples));\r\n        }\r\n\r\n        // Reject a sample directly from the waiting-to-be-accepted queue.\r\n        // We first create the receipt using the existing service, then record\r\n        // the rejection against that receipt using the existing rejection table.\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> RejectPendingSample(\r\n            int testRequestId,\r\n            string barcode,\r\n            string reason,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (testRequestId <= 0 ||\r\n                string.IsNullOrWhiteSpace(barcode) ||\r\n                string.IsNullOrWhiteSpace(reason))\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Enter the barcode and a rejection reason before rejecting the sample.\";\r\n\r\n                return RedirectToAction(nameof(ReceiveSamples));\r\n            }\r\n\r\n            barcode = barcode.Trim();\r\n            reason = reason.Trim();\r\n\r\n            try\r\n            {\r\n                // Use the existing receipt workflow; no new table is required.\r\n                var received = await _labTechnicianData.ReceiveSampleAsync(\r\n                    testRequestId,\r\n                    barcode,\r\n                    cancellationToken);\r\n\r\n                if (!received)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"The sample could not be recorded. Check that the barcode is unique and the request is still awaiting a sample.\";\r\n\r\n                    return RedirectToAction(nameof(ReceiveSamples));\r\n                }\r\n\r\n                var connectionString =\r\n                    _configuration.GetConnectionString(\"DefaultConnection\");\r\n\r\n                if (string.IsNullOrWhiteSpace(connectionString))\r\n                {\r\n                    throw new InvalidOperationException(\r\n                        \"The DefaultConnection connection string is missing.\");\r\n                }\r\n\r\n                const string receiptSql = \"\"\"\r\n                    SELECT TOP (1) SampleReceiptID\r\n                    FROM dbo.SampleReceipt\r\n                    WHERE TestRequestID = @TestRequestID\r\n                      AND Barcode = @Barcode\r\n                    ORDER BY DateTimeReceived DESC, SampleReceiptID DESC;\r\n                    \"\"\";\r\n\r\n                await using var connection = new SqlConnection(connectionString);\r\n\r\n                var sampleReceiptId = await connection.QuerySingleOrDefaultAsync<int?>(\r\n                    new CommandDefinition(\r\n                        receiptSql,\r\n                        new\r\n                        {\r\n                            TestRequestID = testRequestId,\r\n                            Barcode = barcode\r\n                        },\r\n                        cancellationToken: cancellationToken));\r\n\r\n                if (sampleReceiptId is null || sampleReceiptId.Value <= 0)\r\n                {\r\n                    TempData[\"ErrorMessage\"] =\r\n                        \"The sample was recorded, but its receipt could not be found to finish the rejection. Please refresh and check the Received Samples queue.\";\r\n\r\n                    return RedirectToAction(nameof(ReceiveSamples));\r\n                }\r\n\r\n                var rejected = await _labTechnicianData.RejectSampleAsync(\r\n                    sampleReceiptId.Value,\r\n                    reason,\r\n                    cancellationToken);\r\n\r\n                TempData[rejected ? \"SuccessMessage\" : \"ErrorMessage\"] =\r\n                    rejected\r\n                        ? \"Sample rejected and saved to rejection history.\"\r\n                        : \"The sample was recorded, but the rejection could not be saved. Check the Rejected Sample History before trying again.\";\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"RejectPendingSample failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not reject the sample. Check the application logs.\";\r\n            }\r\n\r\n            return RedirectToAction(nameof(ReceiveSamples));\r\n        }\r\n\r\n        [HttpPost]\r\n        [ValidateAntiForgeryToken]\r\n        public async Task<IActionResult> RejectSample(\r\n            int sampleReceiptId,\r\n            string reason,\r\n            CancellationToken cancellationToken)\r\n        {\r\n            if (sampleReceiptId <= 0 || string.IsNullOrWhiteSpace(reason))\r\n            {\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Select a sample and provide a rejection reason.\";\r\n\r\n                return RedirectToAction(nameof(ReceiveSamples));\r\n            }\r\n\r\n            try\r\n            {\r\n                var rejected = await _labTechnicianData.RejectSampleAsync(\r\n                    sampleReceiptId,\r\n                    reason,\r\n                    cancellationToken);\r\n\r\n                TempData[rejected ? \"SuccessMessage\" : \"ErrorMessage\"] =\r\n                    rejected\r\n                        ? \"Sample rejected. Other samples on this request remain unaffected.\"\r\n                        : \"Sample not found. Refresh the page and try again.\";\r\n            }\r\n            catch (Exception ex)\r\n            {\r\n                Console.Error.WriteLine($\"RejectSample failed: {ex}\");\r\n\r\n                TempData[\"ErrorMessage\"] =\r\n                    \"Could not reject the sample. Check the application logs.\";\r\n            }\r\n\r\n            return RedirectToAction(nameof(ReceiveSamples));\r\n        }\r\n    }\r\n}he page and try again.";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RejectSample failed: {ex}");

                TempData["ErrorMessage"] =
                    "Could not reject the sample. Check the application logs.";
            }

            return RedirectToAction(nameof(ReceiveSamples));
        }
    }
}