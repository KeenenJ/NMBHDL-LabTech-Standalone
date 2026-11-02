#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-03  Manage Consumables
    // =================================================================

    // AC-03-01 / AC-03-08
    [HttpGet]
    public async Task<IActionResult> ConsumableIndex() => View(await _inventory.GetConsumableListAsync());

    // The "Add Consumable" form is a dialog on the index page
    [HttpGet]
    public IActionResult ConsumableCreate() => RedirectToAction(nameof(ConsumableIndex));

    // AC-03-02 / AC-03-03  (TC-LM-03-01)
    [HttpPost]
    public async Task<IActionResult> ConsumableCreate(ConsumableCreateModel form)
    {
        var result = await _inventory.AddConsumableAsync(form);
        if (result.Succeeded) { Flash(result); return RedirectToAction(nameof(ConsumableIndex)); }

        // Back to the list; the validation summary shows what was wrong
        ShowErrors(result);
        return View(nameof(ConsumableIndex), await _inventory.GetConsumableListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> ConsumableEdit(int id)
    {
        var form = await _inventory.GetConsumableEditAsync(id);
        if (form is null) { SetError("That consumable no longer exists."); return RedirectToAction(nameof(ConsumableIndex)); }
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> ConsumableEdit(int? id, ConsumableEditModel form)
    {
        var result = await _inventory.EditConsumableAsync(PickId(id, form.ConsumableId), form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(ConsumableIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(await _inventory.FillConsumableEditAsync(form)); }

        Flash(result);
        return RedirectToAction(nameof(ConsumableIndex));
    }

    // AC-03-04..07  (TC-LM-03-02, 03-03, 03-04, 03-05). Posted: ConsumableId, Action, Amount.
    [HttpPost]
    public async Task<IActionResult> AdjustStock(AdjustStockModel form)
    {
        Flash(await _inventory.AdjustStockAsync(form));
        return RedirectToAction(nameof(ConsumableIndex));
    }

    // =================================================================
    // US-LM-04  Manage Suppliers
    // =================================================================

    // AC-04-01
    [HttpGet]
    public async Task<IActionResult> SupplierIndex() => View(await _inventory.GetSupplierListAsync());

    [HttpGet]
    public IActionResult SupplierCreate() => View(new SupplierFormModel());

    // AC-04-02 / 04-03 / 04-04  (TC-LM-04-01, 04-02, 04-03)
    [HttpPost]
    public async Task<IActionResult> SupplierCreate(SupplierFormModel form)
    {
        var result = await _inventory.AddSupplierAsync(form);
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(SupplierIndex));
    }

    [HttpGet]
    public async Task<IActionResult> SupplierEdit(int id)
    {
        var form = await _inventory.GetSupplierAsync(id);
        if (form is null) { SetError("That supplier no longer exists."); return RedirectToAction(nameof(SupplierIndex)); }
        return View(form);
    }

    // AC-04-05  (TC-LM-04-05)
    [HttpPost]
    public async Task<IActionResult> SupplierEdit(int? id, SupplierFormModel form)
    {
        var result = await _inventory.EditSupplierAsync(PickId(id, form.SupplierId), form);
        if (result.NotFound) { Flash(result); return RedirectToAction(nameof(SupplierIndex)); }
        if (!result.Succeeded) { ShowErrors(result); return View(form); }

        Flash(result);
        return RedirectToAction(nameof(SupplierIndex));
    }

    // AC-04-06  (TC-LM-04-04)
    [HttpPost]
    public async Task<IActionResult> SupplierDelete(int id)
    {
        Flash(await _inventory.DeleteSupplierAsync(id));
        return RedirectToAction(nameof(SupplierIndex));
    }
}
