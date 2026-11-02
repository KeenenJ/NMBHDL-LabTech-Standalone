#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Controllers;

public partial class LabManagerController
{
    // =================================================================
    // US-LM-05  Manage Consumable Orders
    // =================================================================

    // AC-05-09. Optional ?status=ordered|partial|complete|cancelled
    [HttpGet]
    public async Task<IActionResult> OrderIndex(string? status) => View(await _orders.GetOrderListAsync(status));

    // AC-05-01 / TC-LM-05-07. ?supplierId= shows one supplier's consumables only.
    [HttpGet]
    public async Task<IActionResult> OrderCreate(int? supplierId) => View(await _orders.GetCreateOrderPageAsync(supplierId));

    // AC-05-02 / 05-03 (TC-LM-05-01). Posted: OrderItems[i].Selected / .ConsumableId / .Quantity
    [HttpPost]
    public async Task<IActionResult> OrderCreate(OrderCreateModel form)
    {
        var result = await _orders.PlaceOrdersAsync(form, CurrentUserId);
        if (!result.Succeeded)
        {
            ShowErrors(result);
            return View(await _orders.GetCreateOrderPageAsync(form.SupplierId, posted: form));
        }

        Flash(result);
        var ids = result.Value!;
        return ids.Count == 1
            ? RedirectToAction(nameof(OrderDetail), new { id = ids[0] })
            : RedirectToAction(nameof(OrderIndex));
    }

    // AC-05-09
    [HttpGet]
    public async Task<IActionResult> OrderDetail(int id)
    {
        var model = await _orders.GetOrderDetailAsync(id);
        if (model is null) { SetError("That order no longer exists."); return RedirectToAction(nameof(OrderIndex)); }
        return View(model);
    }

    // AC-05-04 / 05-05 / 05-06 (TC-LM-05-02, 05-03, 05-06). id = the ORDER ITEM id.
    [HttpPost]
    public async Task<IActionResult> OrderItemReceive(int id)
    {
        var result = await _orders.ReceiveItemAsync(id);
        Flash(result);
        return result.NotFound
            ? RedirectToAction(nameof(OrderIndex))
            : RedirectToAction(nameof(OrderDetail), new { id = result.Value });
    }

    // AC-05-08 (TC-LM-05-05). Posted: CancellationReason
    [HttpPost]
    public async Task<IActionResult> OrderItemCancel(int id, CancelModel form)
    {
        var result = await _orders.CancelItemAsync(id, form);
        Flash(result);
        return result.NotFound
            ? RedirectToAction(nameof(OrderIndex))
            : RedirectToAction(nameof(OrderDetail), new { id = result.Value });
    }

    // AC-05-07 (TC-LM-05-04). Posted: CancellationReason
    [HttpPost]
    public async Task<IActionResult> OrderCancel(int id, CancelModel form)
    {
        var result = await _orders.CancelOrderAsync(id, form);
        Flash(result);
        return result.NotFound
            ? RedirectToAction(nameof(OrderIndex))
            : RedirectToAction(nameof(OrderDetail), new { id });
    }
}
