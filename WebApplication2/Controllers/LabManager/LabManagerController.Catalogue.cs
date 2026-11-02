#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-01  Manage Test Categories
    // =================================================================

    // AC-01-01
    [HttpGet]
    public async Task<IActionResult> TestCategoryIndex() => View(await _catalogue.GetCategoryListAsync());

    [HttpGet]
    public IActionResult TestCategoryCreate() => View(new TestCategoryFormModel());

    // AC-01-02 / 01-03 / 01-04  (TC-LM-01-01, 01-02, 01-03)
    [HttpPost]
    public async Task<IActionResult> TestCategoryCreate(TestCategoryFormModel form)
    {
        var result = await _catalogue.AddCategoryAsync(form);
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(TestCategoryIndex));
    }

    // AC-01-05
    [HttpGet]
    public async Task<IActionResult> TestCategoryEdit(int id)
    {
        var form = await _catalogue.GetCategoryAsync(id);
        if (form is null) { SetError("That category no longer exists."); return RedirectToAction(nameof(TestCategoryIndex)); }
        return View(form);
    }

    // AC-01-06  (TC-LM-01-04)
    [HttpPost]
    public async Task<IActionResult> TestCategoryEdit(int? id, TestCategoryFormModel form)
    {
        var result = await _catalogue.EditCategoryAsync(PickId(id, form.CategoryId), form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(TestCategoryIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(TestCategoryIndex));
    }

    // AC-01-07  (TC-LM-01-05)
    [HttpPost]
    public async Task<IActionResult> TestCategoryDelete(int id)
    {
        Flash(await _catalogue.DeleteCategoryAsync(id));
        return RedirectToAction(nameof(TestCategoryIndex));
    }

    // =================================================================
    // Sample types (AC-02-09)
    // =================================================================
    [HttpGet]
    public async Task<IActionResult> SampleTypeIndex() => View(await _catalogue.GetSampleTypeListAsync());

    // The add form is on the index page
    [HttpGet]
    public IActionResult SampleTypeCreate() => RedirectToAction(nameof(SampleTypeIndex));

    [HttpPost]
    public async Task<IActionResult> SampleTypeCreate(SampleTypeFormModel form)
    {
        var result = await _catalogue.AddSampleTypeAsync(form);
        if (result.Succeeded) { Flash(result); return RedirectToAction(nameof(SampleTypeIndex)); }

        ShowErrors(result);
        var page = await _catalogue.GetSampleTypeListAsync();
        page.SampleTypeName = form.SampleTypeName;
        return View(nameof(SampleTypeIndex), page);
    }

    [HttpPost]
    public async Task<IActionResult> SampleTypeDelete(int id)
    {
        Flash(await _catalogue.DeleteSampleTypeAsync(id));
        return RedirectToAction(nameof(SampleTypeIndex));
    }

    // =================================================================
    // US-LM-02  Manage Test Type Records
    // =================================================================

    // AC-02-01
    [HttpGet]
    public async Task<IActionResult> TestTypeIndex() => View(await _catalogue.GetTestTypeListAsync());

    [HttpGet]
    public async Task<IActionResult> TestTypeCreate() => View(await _catalogue.FillTestTypeOptionsAsync(new TestTypeFormModel()));

    // AC-02-02..06, 08, 09  (TC-LM-02-01, 02-02, 02-03, 02-04, 02-06)
    [HttpPost]
    public async Task<IActionResult> TestTypeCreate(TestTypeFormModel form)
    {
        var result = await _catalogue.AddTestTypeAsync(form);
        if (!result.Succeeded) { ShowErrors(result); return View(await _catalogue.FillTestTypeOptionsAsync(form)); }

        Flash(result);
        return RedirectToAction(nameof(TestTypeIndex));
    }

    [HttpGet]
    public async Task<IActionResult> TestTypeEdit(int id)
    {
        var form = await _catalogue.GetTestTypeFormAsync(id);
        if (form is null) { SetError("That test type no longer exists."); return RedirectToAction(nameof(TestTypeIndex)); }
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> TestTypeEdit(int? id, TestTypeFormModel form)
    {
        int testTypeId = PickId(id, form.TestTypeId);
        var result = await _catalogue.EditTestTypeAsync(testTypeId, form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(TestTypeIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(await _catalogue.FillTestTypeOptionsAsync(form)); }

        Flash(result);
        return RedirectToAction(nameof(TestTypeDetail), new { id = testTypeId });
    }

    // AC-02-07 / AC-07-07  (TC-LM-02-05, TC-LM-07-06)
    [HttpGet]
    public async Task<IActionResult> TestTypeDetail(int id)
    {
        var model = await _catalogue.GetTestTypeDetailAsync(id);
        if (model is null) { SetError("That test type no longer exists."); return RedirectToAction(nameof(TestTypeIndex)); }
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> TestTypeDelete(int id)
    {
        Flash(await _catalogue.DeleteTestTypeAsync(id));
        return RedirectToAction(nameof(TestTypeIndex));
    }
}
