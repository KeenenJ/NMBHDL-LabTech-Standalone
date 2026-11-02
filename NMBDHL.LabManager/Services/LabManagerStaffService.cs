#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>
/// US-LM-06 doctor accounts and US-LM-07 lab technician accounts. One method per task.
/// loginUrl is the full address of the login page, put in the emails.
/// </summary>
public interface ILabManagerStaffService
{
    // ---- US-LM-06 ----
    Task<DoctorIndexModel> GetDoctorListAsync();                                                    // AC-06-01
    Task<DoctorFormModel?> GetDoctorFormAsync(int doctorId);
    Task<LabManagerTaskResult> AddDoctorAsync(DoctorFormModel form, string loginUrl);              // AC-06-02/04/05/07
    Task<LabManagerTaskResult> EditDoctorAsync(int doctorId, DoctorFormModel form);                // AC-06-06
    Task<LabManagerTaskResult> ResetDoctorPasswordAsync(int doctorId, string loginUrl);
    Task<LabManagerTaskResult> DeleteDoctorAsync(int doctorId);

    // ---- US-LM-07 ----
    Task<TechnicianIndexModel> GetTechnicianListAsync();                                            // AC-07-01
    Task<TechnicianFormModel?> GetTechnicianFormAsync(int technicianId);
    Task<TechnicianFormModel> FillTechnicianOptionsAsync(TechnicianFormModel form);
    Task<LabManagerTaskResult> AddTechnicianAsync(TechnicianFormModel form, string loginUrl);      // AC-07-02/03/04
    Task<LabManagerTaskResult> EditTechnicianAsync(int technicianId, TechnicianFormModel form);    // AC-07-06/08
    Task<LabManagerTaskResult> ResetTechnicianPasswordAsync(int technicianId, string loginUrl);
    Task<LabManagerTaskResult> DeleteTechnicianAsync(int technicianId);
}

public class LabManagerStaffService : ILabManagerStaffService
{
    private readonly ILabManagerData _data;
    private readonly ILabManagerPasswordService _passwords;
    private readonly ILabManagerEmailService _email;

    public LabManagerStaffService(ILabManagerData data, ILabManagerPasswordService passwords, ILabManagerEmailService email)
    {
        _data = data;
        _passwords = passwords;
        _email = email;
    }

    // =================================================================
    // US-LM-06  Doctors
    // =================================================================
    public async Task<DoctorIndexModel> GetDoctorListAsync() => new() { Doctors = await _data.Doctors.GetAllAsync() };

    public async Task<DoctorFormModel?> GetDoctorFormAsync(int doctorId)
    {
        var d = await _data.Doctors.GetByIdAsync(doctorId);
        if (d is null) return null;
        return new DoctorFormModel
        {
            DoctorId = d.DoctorId,
            Name = d.Name,
            Surname = d.Surname,
            HPCSANumber = d.HPCSANumber,
            Email = d.Email,
            ContactNumber = d.ContactNumber,
            MustChangePassword = d.MustChangePassword
        };
    }

    /// <summary>
    /// Creates [User] + Doctor with an auto-generated password and IsFirstLogin = 1, so the doctor
    /// must change it at first login (AC-06-03), then emails the password (AC-06-02).
    /// </summary>
    public async Task<LabManagerTaskResult> AddDoctorAsync(DoctorFormModel form, string loginUrl)
    {
        var result = await ValidateDoctorAsync(form, excludeDoctorId: 0, excludeUserId: 0);
        if (!result.Succeeded) return result;

        string temporaryPassword = _passwords.GenerateTemporaryPassword();
        try
        {
            await _data.Doctors.CreateAsync(form, _passwords.HashPassword(temporaryPassword));
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(UniqueViolationMessage(ex));
        }

        string fullName = $"Dr {form.Name} {form.Surname}";
        bool emailed = await _email.SendNewAccountAsync(form.Email!, fullName, "doctor", temporaryPassword, loginUrl);

        var ok = LabManagerTaskResult.Ok($"Doctor account created for {fullName}." +
                                         (emailed ? $" A temporary password was emailed to {form.Email}." : ""));
        if (!emailed)
            ok.Warning = "The account was created, but the email with the temporary password could not be sent. " +
                         "Open the doctor and use Reset Password to send a new one.";
        return ok;
    }

    public async Task<LabManagerTaskResult> EditDoctorAsync(int doctorId, DoctorFormModel form)
    {
        form.DoctorId = doctorId;
        var existing = await _data.Doctors.GetByIdAsync(doctorId);
        if (existing is null) return LabManagerTaskResult.Missing("That doctor no longer exists.");
        form.MustChangePassword = existing.MustChangePassword;

        var result = await ValidateDoctorAsync(form, doctorId, existing.UserId);
        if (!result.Succeeded) return result;

        try
        {
            await _data.Doctors.UpdateAsync(doctorId, form);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(UniqueViolationMessage(ex));
        }
        return LabManagerTaskResult.Ok($"Dr {form.Name} {form.Surname} was updated.");
    }

    /// <summary>New temporary password, IsFirstLogin = 1 again, emailed to the doctor.</summary>
    public async Task<LabManagerTaskResult> ResetDoctorPasswordAsync(int doctorId, string loginUrl)
    {
        var doctor = await _data.Doctors.GetByIdAsync(doctorId);
        if (doctor is null) return LabManagerTaskResult.Missing("That doctor no longer exists.");
        return await ResetPasswordAsync(doctor.UserId, doctor.Email, doctor.FullName, loginUrl);
    }

    public async Task<LabManagerTaskResult> DeleteDoctorAsync(int doctorId) =>
        await _data.Doctors.DeleteAsync(doctorId) switch
        {
            DeleteResult.Deleted => LabManagerTaskResult.Ok("Doctor account deleted."),
            DeleteResult.Deactivated => LabManagerTaskResult.Ok(
                "This doctor has test requests on record, so the account was deactivated instead of deleted. They can no longer log in."),
            _ => LabManagerTaskResult.Missing("That doctor no longer exists.")
        };

    private async Task<LabManagerTaskResult> ValidateDoctorAsync(DoctorFormModel form, int excludeDoctorId, int excludeUserId)
    {
        form.Name = LmText.Clean(form.Name);
        form.Surname = LmText.Clean(form.Surname);
        form.Email = LmText.Clean(form.Email);
        form.HPCSANumber = IdentityRules.NormaliseHpcsa(form.HPCSANumber ?? "");
        form.ContactNumber = IdentityRules.NormalisePhone(form.ContactNumber ?? "");

        var result = LabManagerTaskResult.FromAnnotations(form);   // AC-06-07 blank fields

        if (!result.HasErrorFor(nameof(form.HPCSANumber)))
        {
            if (!IdentityRules.IsValidHpcsa(form.HPCSANumber))
                result.AddError(nameof(form.HPCSANumber), "Enter a valid HPCSA number, e.g. MP123456");
            else if (await _data.Doctors.HpcsaExistsAsync(form.HPCSANumber, excludeDoctorId))
                result.AddError(nameof(form.HPCSANumber), LabManagerMessages.HpcsaExists);          // AC-06-04
        }

        if (!result.HasErrorFor(nameof(form.Email)) && await _data.Accounts.EmailInUseAsync(form.Email, excludeUserId))
            result.AddError(nameof(form.Email), LabManagerMessages.EmailInUse);                       // AC-06-05

        if (!result.HasErrorFor(nameof(form.ContactNumber)) && !IdentityRules.IsValidSaPhone(form.ContactNumber))
            result.AddError(nameof(form.ContactNumber), "Enter a valid SA contact number, e.g. 0821234567");

        return result;
    }

    // =================================================================
    // US-LM-07  Lab technicians
    // =================================================================
    public async Task<TechnicianIndexModel> GetTechnicianListAsync() => new()
    {
        Technicians = await _data.Technicians.GetAllAsync(),
        TestTypeOptions = await _data.TestTypes.GetLookupAsync()
    };

    public async Task<TechnicianFormModel?> GetTechnicianFormAsync(int technicianId)
    {
        var t = await _data.Technicians.GetByIdAsync(technicianId);
        if (t is null) return null;
        return await FillTechnicianOptionsAsync(new TechnicianFormModel
        {
            TechnicianId = t.TechnicianId,
            Name = t.Name,
            Surname = t.Surname,
            SAIDNumber = t.SAIDNumber,
            EmployeeNumber = t.EmployeeNumber,
            Email = t.Email,
            ContactNumber = t.ContactNumber,
            TestTypes = t.TestTypes.Select(x => x.Id).ToList(),
            MustChangePassword = t.MustChangePassword
        });
    }

    public async Task<TechnicianFormModel> FillTechnicianOptionsAsync(TechnicianFormModel form)
    {
        form.TestTypeOptions = await _data.TestTypes.GetLookupAsync();
        return form;
    }

    public async Task<LabManagerTaskResult> AddTechnicianAsync(TechnicianFormModel form, string loginUrl)
    {
        var result = await ValidateTechnicianAsync(form, excludeTechnicianId: 0, excludeUserId: 0, currentSaId: null);
        if (!result.Succeeded) return result;

        string temporaryPassword = _passwords.GenerateTemporaryPassword();
        try
        {
            await _data.Technicians.CreateAsync(form, _passwords.HashPassword(temporaryPassword));
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(UniqueViolationMessage(ex));
        }

        string fullName = $"{form.Name} {form.Surname}";
        bool emailed = await _email.SendNewAccountAsync(form.Email!, fullName, "lab technician", temporaryPassword, loginUrl);

        var ok = LabManagerTaskResult.Ok($"Technician account created for {fullName}." +
                                         (emailed ? $" A temporary password was emailed to {form.Email}." : ""));
        if (!emailed)
            ok.Warning = "The account was created, but the email with the temporary password could not be sent. " +
                         "Open the technician and use Reset Password to send a new one.";
        return ok;
    }

    public async Task<LabManagerTaskResult> EditTechnicianAsync(int technicianId, TechnicianFormModel form)
    {
        form.TechnicianId = technicianId;
        var existing = await _data.Technicians.GetByIdAsync(technicianId);
        if (existing is null) return LabManagerTaskResult.Missing("That technician no longer exists.");
        form.MustChangePassword = existing.MustChangePassword;

        var result = await ValidateTechnicianAsync(form, technicianId, existing.UserId, existing.SAIDNumber);
        if (!result.Succeeded) return result;

        try
        {
            await _data.Technicians.UpdateAsync(technicianId, form);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(UniqueViolationMessage(ex));
        }
        return LabManagerTaskResult.Ok($"{form.Name} {form.Surname} was updated and is assigned to {form.TestTypes.Count} test type(s).");
    }

    public async Task<LabManagerTaskResult> ResetTechnicianPasswordAsync(int technicianId, string loginUrl)
    {
        var tech = await _data.Technicians.GetByIdAsync(technicianId);
        if (tech is null) return LabManagerTaskResult.Missing("That technician no longer exists.");
        return await ResetPasswordAsync(tech.UserId, tech.Email, tech.FullName, loginUrl);
    }

    public async Task<LabManagerTaskResult> DeleteTechnicianAsync(int technicianId) =>
        await _data.Technicians.DeleteAsync(technicianId) switch
        {
            DeleteResult.Deleted => LabManagerTaskResult.Ok("Technician account deleted."),
            DeleteResult.Deactivated => LabManagerTaskResult.Ok(
                "This technician has verified results on record, so the account was deactivated instead of deleted. They can no longer log in."),
            _ => LabManagerTaskResult.Missing("That technician no longer exists.")
        };

    private async Task<LabManagerTaskResult> ValidateTechnicianAsync(TechnicianFormModel form, int excludeTechnicianId, int excludeUserId, string? currentSaId)
    {
        form.Name = LmText.Clean(form.Name);
        form.Surname = LmText.Clean(form.Surname);
        form.Email = LmText.Clean(form.Email);
        form.EmployeeNumber = LmText.Clean(form.EmployeeNumber);
        form.SAIDNumber = LmText.Clean(form.SAIDNumber).Replace(" ", "");
        form.ContactNumber = IdentityRules.NormalisePhone(form.ContactNumber ?? "");
        form.TestTypes = (form.TestTypes ?? new()).Where(t => t > 0).Distinct().ToList();

        var result = LabManagerTaskResult.FromAnnotations(form);

        // AC-07-03 / AC-07-06
        if (form.TestTypes.Count == 0)
            result.AddError(nameof(form.TestTypes), LabManagerMessages.NoTestType);
        else if (await _data.TestTypes.CountExistingAsync(form.TestTypes) != form.TestTypes.Count)
            result.AddError(nameof(form.TestTypes), "One or more selected test types no longer exist");

        // SA ID: format -> AC-07-04 duplicate -> check digit. The duplicate check comes before the check
        // digit on purpose: TC-LM-07-03 uses 8501015009087 (wrong check digit) and expects "ID number already registered".
        if (!result.HasErrorFor(nameof(form.SAIDNumber)))
        {
            string? formatError = IdentityRules.ValidateSaIdFormat(form.SAIDNumber);
            bool unchanged = currentSaId is not null && currentSaId.Trim() == form.SAIDNumber;

            if (formatError is not null)
                result.AddError(nameof(form.SAIDNumber), formatError);
            else if (await _data.Technicians.SaIdExistsAsync(form.SAIDNumber, excludeTechnicianId))
                result.AddError(nameof(form.SAIDNumber), LabManagerMessages.SaIdExists);
            else if (!unchanged && !IdentityRules.PassesLuhn(form.SAIDNumber))
                result.AddError(nameof(form.SAIDNumber), "SA ID number is not valid (check digit does not match)");
        }

        if (!result.HasErrorFor(nameof(form.EmployeeNumber))
            && await _data.Technicians.EmployeeNumberExistsAsync(form.EmployeeNumber, excludeTechnicianId))
            result.AddError(nameof(form.EmployeeNumber), "Employee number already registered");

        if (!result.HasErrorFor(nameof(form.Email)) && await _data.Accounts.EmailInUseAsync(form.Email, excludeUserId))
            result.AddError(nameof(form.Email), LabManagerMessages.EmailInUse);

        if (!result.HasErrorFor(nameof(form.ContactNumber)) && !IdentityRules.IsValidSaPhone(form.ContactNumber))
            result.AddError(nameof(form.ContactNumber), "Enter a valid SA contact number, e.g. 0821234567");

        return result;
    }

    // -----------------------------------------------------------------

    private async Task<LabManagerTaskResult> ResetPasswordAsync(int userId, string email, string fullName, string loginUrl)
    {
        string temporaryPassword = _passwords.GenerateTemporaryPassword();
        await _data.Accounts.SetPasswordAsync(userId, _passwords.HashPassword(temporaryPassword), mustChangePassword: true);

        return await _email.SendPasswordResetByManagerAsync(email, fullName, temporaryPassword, loginUrl)
            ? LabManagerTaskResult.Ok($"A new temporary password was emailed to {email}.")
            : LabManagerTaskResult.Failed("The password was reset, but the email could not be sent. Check the email settings and try again.");
    }

    /// <summary>Two people saving at the same moment: the database's UNIQUE constraint catches the second one.</summary>
    internal static string UniqueViolationMessage(Exception ex)
    {
        string text = ex.Message;
        if (text.Contains("UQ_User_Email")) return LabManagerMessages.EmailInUse;
        if (text.Contains("UQ_Doctor_HPCSANumber")) return LabManagerMessages.HpcsaExists;
        if (text.Contains("UQ_LabTechnician_EmployeeNumber")) return "Employee number already registered";
        return "That record already exists";
    }
}
