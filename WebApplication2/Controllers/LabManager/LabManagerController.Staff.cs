#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-06  Manage Doctor Users
    // =================================================================

    // AC-06-01
    [HttpGet]
    public async Task<IActionResult> DoctorIndex() => View(await _staff.GetDoctorListAsync());

    [HttpGet]
    public IActionResult DoctorCreate() => View(new DoctorFormModel());

    // AC-06-02 / 06-04 / 06-05 / 06-07 (TC-LM-06-01, 06-02, 06-03, 06-05)
    [HttpPost]
    public async Task<IActionResult> DoctorCreate(DoctorFormModel form)
    {
        var result = await _staff.AddDoctorAsync(form, LoginUrl());
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(DoctorIndex));
    }

    [HttpGet]
    public async Task<IActionResult> DoctorEdit(int id)
    {
        var form = await _staff.GetDoctorFormAsync(id);
        if (form is null) { SetError("That doctor no longer exists."); return RedirectToAction(nameof(DoctorIndex)); }
        return View(form);
    }

    // AC-06-06
    [HttpPost]
    public async Task<IActionResult> DoctorEdit(int? id, DoctorFormModel form)
    {
        var result = await _staff.EditDoctorAsync(PickId(id, form.DoctorId), form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(DoctorIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(DoctorIndex));
    }

    // "Reset Password" on DoctorEdit submits the edit form, so the id arrives as DoctorId
    [HttpPost]
    public async Task<IActionResult> DoctorResetPassword(int? id, int? doctorId)
    {
        int targetId = id ?? doctorId ?? 0;
        var result = await _staff.ResetDoctorPasswordAsync(targetId, LoginUrl());
        Flash(result);
        return result.NotFound
            ? RedirectToAction(nameof(DoctorIndex))
            : RedirectToAction(nameof(DoctorEdit), new { id = targetId });
    }

    [HttpPost]
    public async Task<IActionResult> DoctorDelete(int id)
    {
        Flash(await _staff.DeleteDoctorAsync(id));
        return RedirectToAction(nameof(DoctorIndex));
    }

    // =================================================================
    // US-LM-07  Manage Lab Technician Users
    // =================================================================

    // AC-07-01 (with the Add Technician dialog)
    [HttpGet]
    public async Task<IActionResult> TechnicianIndex() => View(await _staff.GetTechnicianListAsync());

    [HttpGet]
    public IActionResult TechnicianCreate() => RedirectToAction(nameof(TechnicianIndex));

    // AC-07-02 / 07-03 / 07-04 (TC-LM-07-01, 07-02, 07-03)
    [HttpPost]
    public async Task<IActionResult> TechnicianCreate(TechnicianFormModel form)
    {
        var result = await _staff.AddTechnicianAsync(form, LoginUrl());
        if (result.Succeeded) { Flash(result); return RedirectToAction(nameof(TechnicianIndex)); }

        // The form is a dialog on the list page, so show the list again with the errors
        ShowErrors(result);
        return View(nameof(TechnicianIndex), await _staff.GetTechnicianListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> TechnicianEdit(int id)
    {
        var form = await _staff.GetTechnicianFormAsync(id);
        if (form is null) { SetError("That technician no longer exists."); return RedirectToAction(nameof(TechnicianIndex)); }
        return View(form);
    }

    // AC-07-06 / 07-08 (TC-LM-07-05, 07-06)
    [HttpPost]
    public async Task<IActionResult> TechnicianEdit(int? id, TechnicianFormModel form)
    {
        var result = await _staff.EditTechnicianAsync(PickId(id, form.TechnicianId), form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(TechnicianIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(await _staff.FillTechnicianOptionsAsync(form)); }

        Flash(result);
        return RedirectToAction(nameof(TechnicianIndex));
    }

    // "Reset Password" on TechnicianEdit submits the edit form, so the id arrives as TechnicianId
    [HttpPost]
    public async Task<IActionResult> TechnicianResetPassword(int? id, int? technicianId)
    {
        int targetId = id ?? technicianId ?? 0;
        var result = await _staff.ResetTechnicianPasswordAsync(targetId, LoginUrl());
        Flash(result);
        return result.NotFound
            ? RedirectToAction(nameof(TechnicianIndex))
            : RedirectToAction(nameof(TechnicianEdit), new { id = targetId });
    }

    [HttpPost]
    public async Task<IActionResult> TechnicianDelete(int id)
    {
        Flash(await _staff.DeleteTechnicianAsync(id));
        return RedirectToAction(nameof(TechnicianIndex));
    }
}
