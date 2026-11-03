
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using NMBDHL.Auth.Model;
using NMBDHL.Auth.Repository;
using NMBHLabSys.LabManagerSubsystem.Services;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using WebApplication2.Models;
using WebApplication2.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WebApplication2.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IAuthRepository _authRepository;
        private readonly ILabManagerReportService _reports;
        private readonly LabTechnicianDataService _labTechnicianData;
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _resetProtector;

        public HomeController(
            ILogger<HomeController> logger,
            IAuthRepository authRepository,
            LabTechnicianDataService labTechnicianData,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider)
        {
            _logger = logger;
            _authRepository = authRepository;
            _labTechnicianData = labTechnicianData;
            _configuration = configuration;

            _resetProtector = dataProtectionProvider.CreateProtector(
                "NMBDHL.Auth.PasswordReset.v1");
        }

        // ---------------------------------------------------------
        // PASSWORD RESET TOKEN VALIDATION
        // ---------------------------------------------------------

        private bool TryReadResetLink(
            string token,
            out string email,
            out long expiry)
        {
            email = string.Empty;
            expiry = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    return false;

                string payload = _resetProtector.Unprotect(token);
                string[] parts = payload.Split('|');

                if (parts.Length != 2 ||
                    !long.TryParse(parts[1], out expiry))
                {
                    return false;
                }

                email = parts[0];

                return !string.IsNullOrWhiteSpace(email)
                    && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiry;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------
        // GENERAL PAGES
        // ---------------------------------------------------------

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
            });
        }

        // ---------------------------------------------------------
        // LOGIN PAGE
        // ---------------------------------------------------------

        [HttpGet]
        public IActionResult LoginPage(string? returnUrl = null)
        {
            var login = new Login
            {
                ReturnUrl = returnUrl ?? string.Empty,
                RememberLogin = true
            };

            return View("LoginPage", login);
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            return LoginPage(returnUrl);
        }

        // ---------------------------------------------------------
        // LOGIN
        // Existing login flow retained
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(Login login)
        {
            User user;

            try
            {
                user = await _authRepository.ValidateUser(login);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login validation failed.");

                ViewBag.Message = "Unable to log in. Please try again.";
                return View("LoginPage", login);
            }

            if (user == null)
            {
                ViewBag.Message = "Invalid email or password.";
                return View("LoginPage", login);
            }

            if (!user.IsActive)
            {
                ViewBag.Message = "Your account is inactive.";
                return View("LoginPage", login);
            }

            string role = (user.Role ?? string.Empty).Trim();
            string dashboardAction;

            switch (role.ToLowerInvariant())
            {
                case "admin":
                    dashboardAction = "AdminDashboard";
                    break;

                case "patient":
                    dashboardAction = "PatientDashboard";
                    break;

                case "doctor":
                    dashboardAction = "DoctorDashboard";
                    break;

                case "lab manager":
                    dashboardAction = "Dashboard";
                    break;

                case "lab technician":
                    dashboardAction = "LabTechnicianDashboard";
                    break;

                default:
                    _logger.LogWarning(
                        "Login rejected because user {UserID} has an unknown role.",
                        user.UserID);

                    ViewBag.Message =
                        "Your account has an invalid role. Contact the administrator.";

                    return View("LoginPage", login);
            }

            var claims = new List<Claim>
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    user.UserID.ToString()),

                new Claim(
                    ClaimTypes.Name,
                    $"{user.Name} {user.Surname}"),

                new Claim(
                    ClaimTypes.Email,
                    user.Email),

                new Claim(
                    ClaimTypes.Role,
                    role)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            try
            {
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = login.RememberLogin
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create the login cookie.");

                ViewBag.Message =
                    "Unable to complete sign-in. Please try again.";

                return View("LoginPage", login);
            }

            if (!string.IsNullOrWhiteSpace(login.ReturnUrl) &&
                Url.IsLocalUrl(login.ReturnUrl))
            {
                return LocalRedirect(login.ReturnUrl);
            }

            if (role.Equals(
                "Lab Technician",
                StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(
                    "LabTechnicianDashboard", "Home");
            }

            return RedirectToAction(dashboardAction, "Home");
        }

        // ---------------------------------------------------------
        // FORGOT PASSWORD - SEARCH FOR USER
        // Only one FindUserByEmail action should exist
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FindUserByEmail(Login model)
        {
            ModelState.Remove(
                nameof(NMBDHL.Auth.Model.Login.Password));

            if (string.IsNullOrWhiteSpace(model.Email) ||
                !new EmailAddressAttribute().IsValid(model.Email))
            {
                ViewBag.ShowForgotModal = true;
                ViewBag.ForgotMessage = "Enter a valid email address.";

                return View("LoginPage", model);
            }

            try
            {
                var user = await _authRepository.FindUserByEmailAsync(
                    model.Email.Trim());

                if (user == null)
                {
                    ViewBag.ShowForgotModal = true;
                    ViewBag.ForgotMessage =
                        "No registered account was found for that email.";

                    return View("LoginPage", model);
                }

                ViewBag.ShowSendLinkModal = true;
                ViewBag.RegisteredEmail = user.Email;
                ViewBag.SendLinkMessage = string.Empty;

                return View("LoginPage", new Login
                {
                    Email = user.Email,
                    RememberLogin = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email lookup failed.");

                ViewBag.ShowForgotModal = true;
                ViewBag.ForgotMessage =
                    "Unable to search right now. Please try again.";

                return View("LoginPage", model);
            }
        }

        // ---------------------------------------------------------
        // FORGOT PASSWORD - SEND RESET EMAIL
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(Login model)
        {
            ModelState.Remove(
                nameof(NMBDHL.Auth.Model.Login.Password));

            string email = model.Email?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) ||
                !new EmailAddressAttribute().IsValid(email))
            {
                ViewBag.ShowSendLinkModal = true;
                ViewBag.RegisteredEmail = email;
                ViewBag.SendLinkMessage = "Enter a valid email address.";

                return View("LoginPage", model);
            }

            try
            {
                var user = await _authRepository.FindUserByEmailAsync(email);

                if (user == null)
                {
                    ViewBag.ShowForgotModal = true;
                    ViewBag.ForgotMessage =
                        "No registered account was found for that email.";

                    return View("LoginPage", model);
                }

                // Create a protected reset token that expires after 30 minutes.
                long expiry = DateTimeOffset.UtcNow
                    .AddMinutes(30)
                    .ToUnixTimeSeconds();

                string payload = $"{user.Email}|{expiry}";
                string token = _resetProtector.Protect(payload);

                string? resetUrl = Url.Action(
                    nameof(ResetPassword),
                    "Home",
                    new { token },
                    Request.Scheme);

                if (string.IsNullOrWhiteSpace(resetUrl))
                {
                    throw new InvalidOperationException(
                        "Could not generate the password reset URL.");
                }

                // Read SMTP configuration.
                string smtpServer =
                    _configuration["EmailSettings:SmtpServer"]
                    ?? throw new InvalidOperationException(
                        "EmailSettings:SmtpServer is missing.");

                int smtpPort = int.TryParse(
                    _configuration["EmailSettings:Port"],
                    out int configuredPort)
                        ? configuredPort
                        : 587;

                string smtpUsername =
                    _configuration["EmailSettings:Username"]
                    ?? throw new InvalidOperationException(
                        "EmailSettings:Username is missing.");

                string smtpPassword =
                    _configuration["EmailSettings:Password"]
                    ?? throw new InvalidOperationException(
                        "EmailSettings:Password is missing.");

                string fromEmail =
                    _configuration["EmailSettings:FromEmail"]
                    ?? throw new InvalidOperationException(
                        "EmailSettings:FromEmail is missing.");

                string fromName =
                    _configuration["EmailSettings:FromName"]
                    ?? "NMBDHL";

                // Build the email using MimeKit.
                var emailMessage = new MimeMessage();

                emailMessage.From.Add(
                    new MailboxAddress(fromName, fromEmail));

                emailMessage.To.Add(
                    MailboxAddress.Parse(user.Email));

                emailMessage.Subject = "Password Reset Request";

                string safeResetUrl = WebUtility.HtmlEncode(resetUrl);

                emailMessage.Body = new BodyBuilder
                {
                    HtmlBody = $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family:Arial,sans-serif;">
                    <h2>Password Reset</h2>

                    <p>
                        We received a request to reset your password.
                    </p>

                    <p>
                        <a href="{safeResetUrl}"
                           style="display:inline-block;
                                  padding:12px 20px;
                                  background-color:#2c3e50;
                                  color:white;
                                  text-decoration:none;
                                  border-radius:5px;">
                            Reset Password
                        </a>
                    </p>

                    <p>This link expires in 30 minutes.</p>

                    <p>
                        If you did not request a password reset,
                        you can ignore this email.
                    </p>
                </body>
                </html>
                """
                }.ToMessageBody();

                // Connect to Brevo using STARTTLS and authenticate.
                using var smtpClient =
                    new MailKit.Net.Smtp.SmtpClient();

                await smtpClient.ConnectAsync(
                    smtpServer,
                    smtpPort,
                    SecureSocketOptions.StartTls);

                await smtpClient.AuthenticateAsync(
                    smtpUsername.Trim(),
                    smtpPassword);

                // Send the email once.
                await smtpClient.SendAsync(emailMessage);

                await smtpClient.DisconnectAsync(true);

                ViewBag.ShowSendLinkModal = true;
                ViewBag.RegisteredEmail = user.Email;
                ViewBag.SendLinkMessage =
                    "The reset email has been sent. Please check your inbox.";

                return View("LoginPage", new Login
                {
                    Email = user.Email,
                    RememberLogin = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Password reset email could not be sent.");

                ViewBag.ShowSendLinkModal = true;
                ViewBag.RegisteredEmail = email;
                ViewBag.SendLinkMessage =
                    "Unable to send the reset email right now. Please try again.";

                return View("LoginPage", model);
            }
        }

        // ---------------------------------------------------------
        // RESET PASSWORD - DISPLAY FORM
        // ---------------------------------------------------------

        [HttpGet]
        public IActionResult ResetPassword(string token)
        {
            if (!TryReadResetLink(token, out _, out _))
            {
                return View("ResetPasswordInvalid");
            }

            return View(new Login
            {
                ResetToken = token
            });
        }

        // ---------------------------------------------------------
        // RESET PASSWORD - SAVE NEW PASSWORD
        // ---------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(Login model)
        {
            ModelState.Remove(
                nameof(NMBDHL.Auth.Model.Login.Email));

            ModelState.Remove(
                nameof(NMBDHL.Auth.Model.Login.Password));

            if (!TryReadResetLink(
                model.ResetToken ?? string.Empty,
                out string email,
                out _))
            {
                return View("ResetPasswordInvalid");
            }

            if (string.IsNullOrWhiteSpace(model.NewPassword) ||
                model.NewPassword.Length < 12 ||
                model.NewPassword.Length > 128)
            {
                ModelState.AddModelError(
                    nameof(model.NewPassword),
                    "Password must be between 12 and 128 characters.");
            }

            if (model.NewPassword != model.ConfirmPassword)
            {
                ModelState.AddModelError(
                    nameof(model.ConfirmPassword),
                    "Passwords do not match.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                string passwordHash =
                    AuthRepository.HashPassword(model.NewPassword!);

                bool updated = await _authRepository.UpdatePasswordAsync(
                    email,
                    passwordHash);

                if (!updated)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "The password could not be updated. Please try again.");

                    return View(model);
                }

                return View("ResetPasswordSuccess");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset failed.");

                ModelState.AddModelError(
                    string.Empty,
                    "An error occurred while updating the password.");

                return View(model);
            }
        }

        // ---------------------------------------------------------
        // DASHBOARDS
        // ---------------------------------------------------------

        public IActionResult AdminDashboard()
        {
            return View();
        }

        public IActionResult PatientDashboard()
        {
            return View();
        }

        public IActionResult LabManagerDashboard()
        {
            return View();
        }

        public async Task<IActionResult> Dashboard()
        {
            return RedirectToAction("Dashboard", "LabManager");
        }

        // ---------------------------------------------------------
        // LAB TECHNICIAN DASHBOARD
        // ---------------------------------------------------------

        public async Task<IActionResult> LabTechnicianDashboard(
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

                const string statsSql = """
                    SELECT
                        (
                            SELECT COUNT(*)
                            FROM dbo.SampleReceipt sr
                            WHERE NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.TestResult tres
                                WHERE tres.TestRequestID = sr.TestRequestID
                            )
                        ) AS AwaitingSelection,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestResult tres
                            OUTER APPLY
                            (
                                SELECT TOP (1) rv.Decision
                                FROM dbo.ResultVerification rv
                                WHERE rv.TestResultID = tres.TestResultID
                                ORDER BY rv.VerificationDateTime DESC,
                                         rv.ResultVerificationID DESC
                            ) latest
                            WHERE latest.Decision IS NULL
                               OR UPPER(LTRIM(RTRIM(latest.Decision))) NOT IN
                                  ('APPROVED', 'RETURNED', 'RETURN', 'REJECTED')
                        ) AS AwaitingVerification,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestResult
                            WHERE CompletionDateTime >= CONVERT(date, GETDATE())
                              AND CompletionDateTime < DATEADD(day, 1, CONVERT(date, GETDATE()))
                        ) AS CompletedToday,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestRequest tr
                            WHERE UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, ''))))
                                  IN ('STAT', 'URGENT')
                              AND COALESCE(tr.Status, '') NOT IN
                                  ('Cancelled', 'Canceled', 'Completed', 'Rejected')
                        ) AS UrgentCount,
                        (
                            SELECT COUNT(*)
                            FROM dbo.TestResult tres
                            INNER JOIN dbo.TestType tt
                                ON tt.TestTypeID = tres.TestTypeID
                            INNER JOIN dbo.SampleReceipt sr
                                ON sr.TestRequestID = tres.TestRequestID
                            WHERE sr.DateTimeReceived IS NOT NULL
                              AND COALESCE(tt.TurnAroundMinutes, 0) > 0
                              AND DATEADD(
                                  MINUTE,
                                  tt.TurnAroundMinutes,
                                  sr.DateTimeReceived) < GETDATE()
                              AND NOT EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.ResultVerification rv
                                  WHERE rv.TestResultID = tres.TestResultID
                                    AND UPPER(LTRIM(RTRIM(
                                        COALESCE(rv.Decision, '')))) = 'APPROVED'
                              )
                        ) AS OverdueCount;
                    """;

                await using var connection =
                    new SqlConnection(connectionString);

                var stats =
                    await connection.QuerySingleAsync<LabTechnicianDashboardStats>(
                        new CommandDefinition(
                            statsSql,
                            cancellationToken: cancellationToken));

                var receivedSamples = await _labTechnicianData
                    .GetReceivedSamplesAsync(cancellationToken);

                var assignedTests = await _labTechnicianData
                    .GetAssignedTestsAsync(cancellationToken);

                var incompleteAssignedTests = assignedTests
                    .Where(test => !_labTechnicianData.IsTestCompleted(
                        test.SampleReceiptID,
                        test.TestTypeID))
                    .ToList();

                var assignedSampleIds = new HashSet<int>(
                    assignedTests.Select(test => test.SampleReceiptID));

                stats.AwaitingSelection = receivedSamples.Count(sample =>
                    !sample.IsRejected &&
                    !assignedSampleIds.Contains(sample.SampleReceiptID));

                int selectedCount = assignedTests.Count;

                // Match Review Queue: use only the newest captured result per request/test type.
                var verificationResults = await _labTechnicianData
                    .GetVerificationQueueAsync(cancellationToken);

                var latestResults = verificationResults
                    .GroupBy(result => new { result.TestRequestID, result.TestTypeID })
                    .Select(group => group.OrderByDescending(result => result.TestResultID).First())
                    .ToList();

                int awaitingReview = latestResults.Count(result =>
                    string.Equals(result.LatestVerificationDecision, "Returned", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.LatestVerificationDecision, "Return", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.LatestVerificationDecision, "Rejected", StringComparison.OrdinalIgnoreCase));

                int verifiedCount = latestResults.Count(result =>
                    string.Equals(result.LatestVerificationDecision, "Approved", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.LatestVerificationDecision, "Verified", StringComparison.OrdinalIgnoreCase));

                int totalCapturedCount = latestResults.Count;

                var priorityRequests = receivedSamples
                    .Where(sample =>
                        !sample.IsRejected &&
                        (string.Equals(
                            sample.Urgency,
                            "STAT",
                            StringComparison.OrdinalIgnoreCase)
                         || string.Equals(
                            sample.Urgency,
                            "URGENT",
                            StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(sample =>
                        string.Equals(
                            sample.Urgency,
                            "STAT",
                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ToList();

                var now = DateTime.Now;

                var turnaroundWarnings = incompleteAssignedTests
                    .Where(test =>
                        test.SelectedAt.HasValue &&
                        test.TurnAroundMinutes > 0 &&
                        test.SelectedAt.Value.AddMinutes(
                            test.TurnAroundMinutes) <= now.AddMinutes(30))
                    .OrderBy(test =>
                        test.SelectedAt!.Value.AddMinutes(
                            test.TurnAroundMinutes))
                    .ToList();

                stats.AssignedTests = incompleteAssignedTests.Count;
                stats.NearingTatCount = turnaroundWarnings.Count;
                stats.SelectedCount = selectedCount;
                stats.VerifiedCount = verifiedCount;
                stats.TotalCapturedCount = totalCapturedCount;
                stats.SelectedDenominator = selectedCount + stats.AwaitingSelection;

                ViewBag.AssignedTests = stats.AssignedTests;
                ViewBag.AwaitingSelection = stats.AwaitingSelection;
                ViewBag.AwaitingVerification = stats.AwaitingVerification;
                ViewBag.AwaitingReview = stats.AwaitingReview;
                ViewBag.CompletedToday = stats.CompletedToday;
                ViewBag.UrgentCount = stats.UrgentCount;
                ViewBag.OverdueCount = stats.OverdueCount;
                ViewBag.NearingTatCount = stats.NearingTatCount;
                ViewBag.PriorityRequests = priorityRequests;
                ViewBag.TurnaroundWarnings = turnaroundWarnings;
                ViewBag.SelectedCount = stats.SelectedCount;
                ViewBag.VerifiedCount = stats.VerifiedCount;
                ViewBag.TotalCapturedCount = stats.TotalCapturedCount;
                ViewBag.SelectedDenominator = stats.SelectedDenominator;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Lab Technician dashboard failed to load.");

                ViewBag.DashboardError =
                    "Some dashboard statistics could not be loaded. Check the application logs.";

                ViewBag.AssignedTests = 0;
                ViewBag.AwaitingSelection = 0;
                ViewBag.AwaitingVerification = 0;
                ViewBag.AwaitingReview = 0;
                ViewBag.CompletedToday = 0;
                ViewBag.UrgentCount = 0;
                ViewBag.OverdueCount = 0;
                ViewBag.NearingTatCount = 0;
                ViewBag.PriorityRequests =
                    new List<LabTechnicianQueueItem>();
                ViewBag.TurnaroundWarnings =
                    new List<LabTechnicianQueueItem>();
                ViewBag.SelectedCount = 0;
                ViewBag.VerifiedCount = 0;
                ViewBag.TotalCapturedCount = 0;
                ViewBag.SelectedDenominator = 0;

                return View();
            }
        }

        private sealed class LabTechnicianDashboardStats
        {
            public int AssignedTests { get; set; }
            public int AwaitingSelection { get; set; }
            public int AwaitingVerification { get; set; }
            public int AwaitingReview { get; set; }
            public int CompletedToday { get; set; }
            public int UrgentCount { get; set; }
            public int OverdueCount { get; set; }
            public int NearingTatCount { get; set; }
            public int SelectedCount { get; set; }
            public int VerifiedCount { get; set; }
            public int TotalCapturedCount { get; set; }
            public int SelectedDenominator { get; set; }
        }

        // ---------------------------------------------------------
        // DOCTOR DASHBOARD
        // ---------------------------------------------------------

        public IActionResult DoctorDashboard()
        {
            return View();
        }

        public IActionResult LoadDashboardSection(string section)
        {
            if (string.IsNullOrEmpty(section))
                section = "alerts";

            section = section.ToLowerInvariant();

            switch (section)
            {
                case "alerts":
                    {
                        ViewBag.TotalActiveRequests = 24;
                        ViewBag.CriticalAlertsCount = 4;
                        ViewBag.PendingCollections = 7;
                        ViewBag.TurnaroundCompliance = "94.2%";

                        var recentRequests = new List<dynamic>
                    {
                        new
                        {
                            RequestNumber = "REQ-2026-0042",
                            PatientName = "Sipho Dlamini",
                            IdNumber = "8706125432089",
                            Tests = "Hb, Platelets",
                            Urgency = "STAT",
                            Status = "In Progress",
                            TimeElapsed = "15 mins ago"
                        },
                        new
                        {
                            RequestNumber = "REQ-2026-0041",
                            PatientName = "Riaan Petersen",
                            IdNumber = "7901044321008",
                            Tests = "WBC, Diff Count",
                            Urgency = "Urgent",
                            Status = "Sample(s) Received",
                            TimeElapsed = "45 mins ago"
                        },
                        new
                        {
                            RequestNumber = "REQ-2026-0040",
                            PatientName = "Thandiwe Nkosi",
                            IdNumber = "9203156789012",
                            Tests = "Peripheral Blood Film",
                            Urgency = "Routine",
                            Status = "Submitted",
                            TimeElapsed = "1 hour ago"
                        },
                        new
                        {
                            RequestNumber = "REQ-2026-0039",
                            PatientName = "Ayesha Hendricks",
                            IdNumber = "9507254012345",
                            Tests = "INR, Coagulation",
                            Urgency = "STAT",
                            Status = "Completed",
                            TimeElapsed = "2 hours ago"
                        },
                        new
                        {
                            RequestNumber = "REQ-2026-0038",
                            PatientName = "Chantel Myburgh",
                            IdNumber = "8811235098081",
                            Tests = "FBC Panel",
                            Urgency = "Routine",
                            Status = "Released by doctor",
                            TimeElapsed = "3 hours ago"
                        }
                    };

                        ViewBag.RecentRequests = recentRequests;

                        var mockAlerts = new List<dynamic>
                    {
                        new
                        {
                            Surname = "Dlamini",
                            FirstName = "Sipho",
                            IdNumber = "8706125432089",
                            TestName = "Hb (Hemoglobin)",
                            ResultValue = "5.2",
                            Unit = "g/dL",
                            RefRange = "ref 13.5–17.5",
                            Category = "Full Blood Count",
                            Date = "Today, 11:42",
                            AlertLevel = "Critical Low"
                        },
                        new
                        {
                            Surname = "Petersen",
                            FirstName = "Riaan",
                            IdNumber = "7901044321008",
                            TestName = "WBC (White Blood Cell)",
                            ResultValue = "42.1",
                            Unit = "10⁹/L",
                            RefRange = "ref 4.5–11.0",
                            Category = "Differential Count",
                            Date = "Today, 11:15",
                            AlertLevel = "Critical High"
                        },
                        new
                        {
                            Surname = "Nkosi",
                            FirstName = "Thandiwe",
                            IdNumber = "9203156789012",
                            TestName = "Platelets",
                            ResultValue = "22",
                            Unit = "10⁹/L",
                            RefRange = "ref 150–400",
                            Category = "Full Blood Count",
                            Date = "Yesterday, 15:30",
                            AlertLevel = "Critical Low"
                        }
                    };

                        return PartialView(
                            "~/Views/Doctor/_AbnormalAlertsPartial.cshtml",
                            mockAlerts);
                    }

                case "managepatients":
                    {
                        var mockPatients = new List<dynamic>
                    {
                        new
                        {
                            Surname = "Dlamini",
                            FirstName = "Sipho",
                            IdNumber = "8706125432089",
                            Cellphone = "0823456789",
                            Email = "sipho.d@mail.co.za",
                            Conditions = "Hypertension, Type 2 Diabetes",
                            Allergies = "Penicillin",
                            Medication = "Metformin 500mg, Amlodipine 5mg"
                        },
                        new
                        {
                            Surname = "Petersen",
                            FirstName = "Riaan",
                            IdNumber = "7901044321008",
                            Cellphone = "0731234567",
                            Email = "riaan.p@webmail.co.za",
                            Conditions = "Hypercholesterolemia",
                            Allergies = "Sulfa Drugs",
                            Medication = "Atorvastatin 20mg"
                        },
                        new
                        {
                            Surname = "Nkosi",
                            FirstName = "Thandiwe",
                            IdNumber = "9203156789012",
                            Cellphone = "0819876543",
                            Email = "thandi.n@health.org",
                            Conditions = "None Chronic",
                            Allergies = "Bee Stings, Aspirin",
                            Medication = "None"
                        },
                        new
                        {
                            Surname = "Hendricks",
                            FirstName = "Ayesha",
                            IdNumber = "9507254012345",
                            Cellphone = "0645551234",
                            Email = "ayesha.h@outlook.com",
                            Conditions = "Asthma",
                            Allergies = "Latex, Dust Mites",
                            Medication = "Salbutamol Inhaler as needed"
                        }
                    };

                        return PartialView(
                            "~/Views/Doctor/_ManagePatientsPartial.cshtml",
                            mockPatients);
                    }

                case "createrequest":
                    {
                        ViewBag.PatientsList = new List<dynamic>
                    {
                        new
                        {
                            IdNumber = "8706125432089",
                            FullName = "Dlamini, Sipho"
                        },
                        new
                        {
                            IdNumber = "7901044321008",
                            FullName = "Petersen, Riaan"
                        },
                        new
                        {
                            IdNumber = "9203156789012",
                            FullName = "Nkosi, Thandiwe"
                        },
                        new
                        {
                            IdNumber = "9507254012345",
                            FullName = "Hendricks, Ayesha"
                        }
                    };

                        ViewBag.TestPanels = new List<string>
                    {
                        "Full Blood Count (FBC)",
                        "Hemoglobin (Hb) Isolation",
                        "Differential White Cell Count",
                        "Platelet Count Analysis",
                        "Prothrombin Time & INR",
                        "Peripheral Blood Film Study"
                    };

                        return PartialView(
                            "~/Views/Doctor/_CreateRequestPartial.cshtml");
                    }

                case "trackprogress":
                    {
                        var historicalTrackingData = new List<dynamic>
                    {
                        new
                        {
                            RequestId = 1042,
                            PatientName = "Sipho Dlamini",
                            IdNumber = "8706125432089",
                            Panels = "Hb, Platelets",
                            DateCreated = "21 May 2026",
                            Urgency = "STAT",
                            Status = "In Progress"
                        },
                        new
                        {
                            RequestId = 1041,
                            PatientName = "Riaan Petersen",
                            IdNumber = "7901044321008",
                            Panels = "WBC, Diff Count",
                            DateCreated = "21 May 2026",
                            Urgency = "Urgent",
                            Status = "Sample(s) Received"
                        },
                        new
                        {
                            RequestId = 1040,
                            PatientName = "Thandiwe Nkosi",
                            IdNumber = "9203156789012",
                            Panels = "Peripheral Blood Film",
                            DateCreated = "20 May 2026",
                            Urgency = "Routine",
                            Status = "Submitted"
                        },
                        new
                        {
                            RequestId = 1039,
                            PatientName = "Ayesha Hendricks",
                            IdNumber = "9507254012345",
                            Panels = "INR, Coagulation",
                            DateCreated = "19 May 2026",
                            Urgency = "STAT",
                            Status = "Completed"
                        },
                        new
                        {
                            RequestId = 1038,
                            PatientName = "Chantel Myburgh",
                            IdNumber = "8811235098081",
                            Panels = "FBC Panel",
                            DateCreated = "18 May 2026",
                            Urgency = "Routine",
                            Status = "Released by doctor"
                        }
                    };

                        return PartialView(
                            "~/Views/Doctor/_TrackProgressPartial.cshtml",
                            historicalTrackingData);
                    }

                case "reports":
                    {
                        ViewBag.DefaultStartDate =
                            DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");

                        ViewBag.DefaultEndDate =
                            DateTime.Now.ToString("yyyy-MM-dd");

                        return PartialView(
                            "~/Views/Doctor/_ReportsPortalPartial.cshtml");
                    }

                case "profilesettings":
                    {
                        return PartialView(
                            "~/Views/Doctor/_ProfileSettings.cshtml");
                    }

                default:
                    return Content(
                        "<div class='text-white p-4'>" +
                        "<h5>Section placeholder</h5></div>");
            }
        }
    }
}
