#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

// =====================================================================
// Dashboard.cshtml -> DashboardModel
// =====================================================================
public class DashboardModel
{
    public int BelowReorderCount { get; set; }
    public int NearReorderCount { get; set; }
    public int OpenOrderCount { get; set; }
    public int TechnicianCount { get; set; }
    public int DoctorCount { get; set; }

    /// <summary>Doctors + technicians who have not changed their temporary password yet.</summary>
    public int PendingPasswordChangeCount { get; set; }

    public int CategoryCount { get; set; }
    public int TestTypeCount { get; set; }
    public int SampleTypeCount { get; set; }
    public int SupplierCount { get; set; }

    /// <summary>Consumables that need reordering, lowest stock first.</summary>
    public List<ConsumableRow> StockAlerts { get; set; } = new();

    /// <summary>The five most recent orders.</summary>
    public List<OrderRow> RecentOrders { get; set; } = new();
}

// =====================================================================
// US-LM-08  Reports.cshtml -> ReportsModel
// =====================================================================
public class ReportRow
{
    public string CategoryName { get; set; } = "";
    public int TestCount { get; set; }

    /// <summary>Share of the total, 0-100, one decimal (e.g. 54.0).</summary>
    public decimal SharePercent { get; set; }
}

/// <summary>Reports GET reads StartDate and EndDate from the query string.</summary>
public class ReportsModel : IValidatableObject
{
    public const string DateOrderMessage = LabManagerMessages.StartAfterEnd;
    public const string NoTestsMessage = LabManagerMessages.NoTestsFound;

    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    public string LaboratoryName { get; set; } = "";

    /// <summary>True once a valid date range was submitted and the report was run.</summary>
    public bool HasResult { get; set; }

    public List<ReportRow> Rows { get; set; } = new();
    public int TotalTests => Rows.Sum(r => r.TestCount);

    /// <summary>AC-08-02: show "No tests found for the selected period" instead of the table.</summary>
    public bool IsEmpty => HasResult && TotalTests == 0;

    /// <summary>"1 January 2026 – 31 March 2026"</summary>
    public string PeriodText => StartDate.HasValue && EndDate.HasValue
        ? $"{StartDate.Value:d MMMM yyyy} – {EndDate.Value:d MMMM yyyy}"
        : "";

    /// <summary>yyyy-MM-dd values for the Download PDF link (asp-route-startDate / asp-route-endDate).</summary>
    public string StartDateValue => StartDate?.ToString("yyyy-MM-dd") ?? "";
    public string EndDateValue => EndDate?.ToString("yyyy-MM-dd") ?? "";

    /// <summary>AC-08-03 / TC-LM-08-03.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value.Date > EndDate.Value.Date)
            yield return new ValidationResult(DateOrderMessage, new[] { nameof(EndDate) });
    }
}

// =====================================================================
// US-LM-09  Account pages
//   Profile.cshtml        -> ProfileModel
//   EditProfile.cshtml    -> EditProfileModel
//   ChangePassword.cshtml -> ChangePasswordModel
//   ForgotPassword.cshtml -> ForgotPasswordModel
//   ResetPassword.cshtml  -> ResetPasswordModel
// =====================================================================
public class ProfileModel
{
    public int UserId { get; set; }
    public int? LabManagerId { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";

    /// <summary>grp-04-14 has no employee number for managers, so it is shown as LM-001 (from LabManagerID).</summary>
    public string EmployeeNumber { get; set; } = "";
    public string Department { get; set; } = "";
    public string Email { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string Role { get; set; } = "Laboratory Manager";
    public bool MustChangePassword { get; set; }

    // grp-04-14 doesn't store these, so they stay empty unless the group adds the columns.
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    public string FullName => $"{Name} {Surname}".Trim();
    public string StatusLabel => MustChangePassword ? "Must Change Password" : "Active";
}

/// <summary>Posted by EditProfile: Name, Surname, Email, ContactNumber.</summary>
public class EditProfileModel
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name can be at most 100 characters")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Surname is required")]
    [StringLength(100, ErrorMessage = "Surname can be at most 100 characters")]
    public string? Surname { get; set; }

    [Required(ErrorMessage = "Email address is required")]
    [ValidEmail]
    [StringLength(255, ErrorMessage = "Email can be at most 255 characters")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Contact number is required")]
    public string? ContactNumber { get; set; }

    // read-only on the page
    public string EmployeeNumber { get; set; } = "";
}

/// <summary>Posted by ChangePassword: CurrentPassword, NewPassword, ConfirmPassword.</summary>
public class ChangePasswordModel
{
    [Required(ErrorMessage = "Enter your current password")]
    public string? CurrentPassword { get; set; }

    [Required(ErrorMessage = "Enter a new password")]
    public string? NewPassword { get; set; }

    [Required(ErrorMessage = "Confirm the new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
    public string? ConfirmPassword { get; set; }
}

/// <summary>Posted by ForgotPassword: Email.</summary>
public class ForgotPasswordModel
{
    [Required(ErrorMessage = "Email address is required")]
    [ValidEmail]
    public string? Email { get; set; }
}

/// <summary>Posted by ResetPassword: ResetToken, NewPassword, ConfirmPassword.</summary>
public class ResetPasswordModel
{
    public string? ResetToken { get; set; }

    [Required(ErrorMessage = "Enter a new password")]
    public string? NewPassword { get; set; }

    [Required(ErrorMessage = "Confirm the new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
    public string? ConfirmPassword { get; set; }
}
