#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>US-LM-09: profile, change password, forgot / reset password. One method per task.</summary>
public interface ILabManagerAccountService
{
    Task<ProfileModel?> GetProfileAsync(int userId);
    Task<EditProfileModel?> GetEditProfileAsync(int userId);
    Task<LabManagerTaskResult> EditProfileAsync(int userId, EditProfileModel form);

    /// <summary>AC-09-04 / TC-LM-09-04: every complexity rule that fails is returned separately.</summary>
    Task<LabManagerTaskResult> ChangePasswordAsync(int userId, ChangePasswordModel form);

    /// <summary>
    /// AC-09-03: emails a one-time reset link. buildLink turns the raw token into the full URL.
    /// Always answers the same way, so it can't be used to find out which emails exist.
    /// </summary>
    Task<LabManagerTaskResult> RequestPasswordResetAsync(ForgotPasswordModel form, Func<string, string> buildLink);

    Task<bool> IsResetTokenValidAsync(string? token);
    Task<LabManagerTaskResult> ResetPasswordAsync(string? token, ResetPasswordModel form);
}

public class LabManagerAccountService : ILabManagerAccountService
{
    public const string ResetLinkInvalidMessage = "This reset link is invalid or has expired. Please request a new one.";

    private readonly ILabManagerData _data;
    private readonly ILabManagerPasswordService _passwords;
    private readonly ILabManagerEmailService _email;
    private readonly LabManagerAuthOptions _auth;

    public LabManagerAccountService(ILabManagerData data, ILabManagerPasswordService passwords,
        ILabManagerEmailService email, IOptions<LabManagerOptions> options)
    {
        _data = data;
        _passwords = passwords;
        _email = email;
        _auth = options.Value.Auth;
    }

    public Task<ProfileModel?> GetProfileAsync(int userId) => _data.Accounts.GetManagerProfileAsync(userId);

    public async Task<EditProfileModel?> GetEditProfileAsync(int userId)
    {
        var profile = await _data.Accounts.GetManagerProfileAsync(userId);
        return profile is null ? null : new EditProfileModel
        {
            Name = profile.Name,
            Surname = profile.Surname,
            Email = profile.Email,
            ContactNumber = profile.ContactNumber,
            EmployeeNumber = profile.EmployeeNumber
        };
    }

    public async Task<LabManagerTaskResult> EditProfileAsync(int userId, EditProfileModel form)
    {
        form.Name = LmText.Clean(form.Name);
        form.Surname = LmText.Clean(form.Surname);
        form.Email = LmText.Clean(form.Email);
        form.ContactNumber = IdentityRules.NormalisePhone(form.ContactNumber ?? "");
        form.EmployeeNumber = (await _data.Accounts.GetManagerProfileAsync(userId))?.EmployeeNumber ?? "";

        var result = LabManagerTaskResult.FromAnnotations(form);
        if (!result.HasErrorFor(nameof(form.Email)) && await _data.Accounts.EmailInUseAsync(form.Email, userId))
            result.AddError(nameof(form.Email), LabManagerMessages.EmailInUse);
        if (!result.HasErrorFor(nameof(form.ContactNumber)) && !IdentityRules.IsValidSaPhone(form.ContactNumber))
            result.AddError(nameof(form.ContactNumber), "Enter a valid SA contact number, e.g. 0821234567");
        if (!result.Succeeded) return result;

        await _data.Accounts.UpdateManagerProfileAsync(userId, form);
        return LabManagerTaskResult.Ok("Your profile was updated.");
    }

    public async Task<LabManagerTaskResult> ChangePasswordAsync(int userId, ChangePasswordModel form)
    {
        var user = await _data.Accounts.GetUserByIdAsync(userId);
        if (user is null) return LabManagerTaskResult.Missing("Your account could not be found.");

        var result = LabManagerTaskResult.FromAnnotations(form);

        if (!result.HasErrorFor(nameof(form.CurrentPassword)) && form.CurrentPassword is not null
            && !_passwords.VerifyPassword(form.CurrentPassword, user.PasswordHash))
            result.AddError(nameof(form.CurrentPassword), "Current password is incorrect");

        if (!string.IsNullOrEmpty(form.NewPassword))
        {
            foreach (string error in PasswordRules.Validate(form.NewPassword))
                result.AddError(nameof(form.NewPassword), error);
            if (form.NewPassword == form.CurrentPassword)
                result.AddError(nameof(form.NewPassword), "The new password must be different from the current password");
        }
        if (!result.Succeeded) return result;

        // IsFirstLogin = 0: the forced first-login change is done
        await _data.Accounts.SetPasswordAsync(userId, _passwords.HashPassword(form.NewPassword!), mustChangePassword: false);
        return LabManagerTaskResult.Ok("Your password has been changed.");
    }

    public async Task<LabManagerTaskResult> RequestPasswordResetAsync(ForgotPasswordModel form, Func<string, string> buildLink)
    {
        var result = LabManagerTaskResult.FromAnnotations(form);
        if (!result.Succeeded) return result;

        var user = await _data.Accounts.GetUserByEmailAsync(form.Email!);
        // A laboratory manager = a user with a dbo.LabManager row (doesn't depend on how a role is spelled)
        if (user is not null && user.IsActive && user.LabManagerId is not null)
        {
            var (token, tokenHash) = _passwords.GenerateResetToken();
            int minutes = _auth.ResetTokenLifetimeMinutes;
            await _data.Accounts.CreateResetTokenAsync(user.UserId, tokenHash, minutes);

            if (!await _email.SendForgotPasswordLinkAsync(user.Email, buildLink(token), minutes))
                return LabManagerTaskResult.Failed("The reset email could not be sent. Please try again later or contact the system administrator.");
        }

        return LabManagerTaskResult.Ok("If that email address belongs to a Laboratory Manager account, a password reset link has been sent to it.");
    }

    public async Task<bool> IsResetTokenValidAsync(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && await _data.Accounts.GetUserIdForValidResetTokenAsync(_passwords.HashResetToken(token)) is not null;

    public async Task<LabManagerTaskResult> ResetPasswordAsync(string? token, ResetPasswordModel form)
    {
        if (string.IsNullOrWhiteSpace(token)) return LabManagerTaskResult.Missing(ResetLinkInvalidMessage);

        var result = LabManagerTaskResult.FromAnnotations(form);
        if (!string.IsNullOrEmpty(form.NewPassword))
            foreach (string error in PasswordRules.Validate(form.NewPassword))
                result.AddError(nameof(form.NewPassword), error);
        if (!result.Succeeded) return result;

        bool ok = await _data.Accounts.ResetPasswordWithTokenAsync(_passwords.HashResetToken(token), _passwords.HashPassword(form.NewPassword!));
        return ok
            ? LabManagerTaskResult.Ok("Your password has been reset. You can now log in with the new password.")
            : LabManagerTaskResult.Missing(ResetLinkInvalidMessage);
    }
}

/// <summary>Dashboard and US-LM-08 management report.</summary>
public interface ILabManagerReportService
{
    Task<DashboardModel> GetDashboardAsync();

    /// <summary>AC-08-01/02/03. Errors (missing dates, start after end) come back in the result.</summary>
    Task<LabManagerTaskResult<ReportsModel>> RunReportAsync(DateTime? startDate, DateTime? endDate);

    /// <summary>AC-08-04 / AC-08-05. Value = (PDF bytes, download file name).</summary>
    Task<LabManagerTaskResult<(byte[] Pdf, string FileName)>> BuildReportPdfAsync(DateTime? startDate, DateTime? endDate);
}

public class LabManagerReportService : ILabManagerReportService
{
    private readonly ILabManagerData _data;
    private readonly ILabManagerReportPdf _pdf;
    private readonly LabManagerOptions _options;

    public LabManagerReportService(ILabManagerData data, ILabManagerReportPdf pdf, IOptions<LabManagerOptions> options)
    {
        _data = data;
        _pdf = pdf;
        _options = options.Value;
    }

    public async Task<DashboardModel> GetDashboardAsync()
    {
        var model = await _data.Dashboard.GetCountsAsync();
        model.StockAlerts = await _data.Consumables.GetNeedingReorderAsync(top: 5);
        model.RecentOrders = await _data.Orders.GetAllAsync(top: 5);
        return model;
    }

    public async Task<LabManagerTaskResult<ReportsModel>> RunReportAsync(DateTime? startDate, DateTime? endDate)
    {
        var report = new ReportsModel { StartDate = startDate?.Date, EndDate = endDate?.Date, LaboratoryName = _options.LaboratoryName };

        var result = new LabManagerTaskResult();
        if (startDate is null || endDate is null)
            result.AddError("Select both a start date and an end date");
        else if (startDate.Value.Date > endDate.Value.Date)
            result.AddError(nameof(ReportsModel.EndDate), LabManagerMessages.StartAfterEnd);   // AC-08-03
        if (!result.Succeeded) return LabManagerTaskResult<ReportsModel>.From(result, report);

        report.Rows = await _data.Reports.GetCompletedTestsByCategoryAsync(startDate!.Value, endDate!.Value);
        report.HasResult = true;                                                               // IsEmpty -> AC-08-02
        return LabManagerTaskResult<ReportsModel>.From(new LabManagerTaskResult(), report);
    }

    public async Task<LabManagerTaskResult<(byte[] Pdf, string FileName)>> BuildReportPdfAsync(DateTime? startDate, DateTime? endDate)
    {
        var run = await RunReportAsync(startDate, endDate);
        if (!run.Succeeded) return LabManagerTaskResult<(byte[], string)>.From(run);

        byte[] pdf = _pdf.Build(run.Value!, DateTime.Now);
        string fileName = $"NMBHLabSys_TestsByCategory_{startDate:yyyy-MM-dd}_to_{endDate:yyyy-MM-dd}.pdf";
        return LabManagerTaskResult<(byte[], string)>.From(new LabManagerTaskResult(), (pdf, fileName));
    }
}
