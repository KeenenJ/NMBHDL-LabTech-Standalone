#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>US-LM-05 consumable orders. One method per task.</summary>
public interface ILabManagerOrderService
{
    Task<OrderIndexModel> GetOrderListAsync(string? status);                                        // AC-05-09
    Task<OrderCreateModel> GetCreateOrderPageAsync(int? supplierId, OrderCreateModel? posted = null); // AC-05-01

    /// <summary>AC-05-02 / 05-03. Value = the IDs of the orders created (one per supplier).</summary>
    Task<LabManagerTaskResult<List<int>>> PlaceOrdersAsync(OrderCreateModel form, int managerUserId);

    Task<OrderDetailModel?> GetOrderDetailAsync(int orderId);                                       // AC-05-09

    /// <summary>AC-05-04/05/06. Value = the order the item belongs to (for the redirect).</summary>
    Task<LabManagerTaskResult<int>> ReceiveItemAsync(int orderItemId);

    /// <summary>AC-05-08. Value = the order the item belongs to.</summary>
    Task<LabManagerTaskResult<int>> CancelItemAsync(int orderItemId, CancelModel form);

    Task<LabManagerTaskResult> CancelOrderAsync(int orderId, CancelModel form);                    // AC-05-07
}

public class LabManagerOrderService : ILabManagerOrderService
{
    private readonly ILabManagerData _data;
    private readonly ILabManagerEmailService _email;

    public LabManagerOrderService(ILabManagerData data, ILabManagerEmailService email)
    {
        _data = data;
        _email = email;
    }

    public async Task<OrderIndexModel> GetOrderListAsync(string? status)
    {
        string? filter = NormaliseStatus(status);
        return new OrderIndexModel { Orders = await _data.Orders.GetAllAsync(filter), StatusFilter = filter };
    }

    /// <summary>
    /// Every consumable, grouped by supplier, with the ones at or within 10% of their reorder level
    /// ticked and highlighted (TC-LM-05-07). After a failed post, what the manager entered is kept.
    /// </summary>
    public async Task<OrderCreateModel> GetCreateOrderPageAsync(int? supplierId, OrderCreateModel? posted = null)
    {
        var consumables = await _data.Consumables.GetAllAsync(supplierId);

        var lines = consumables.Select(c =>
        {
            bool low = StockRules.NeedsReorder(c.QuantityOnHand, c.ReorderLevel);
            var previous = posted?.OrderItems?.FirstOrDefault(p => p.ConsumableId == c.ConsumableId);
            return new OrderCreateLine
            {
                ConsumableId = c.ConsumableId,
                ConsumableName = c.ConsumableName,
                SupplierId = c.SupplierId,
                SupplierName = c.SupplierName,
                CurrentStock = c.QuantityOnHand,
                ReorderLevel = c.ReorderLevel,
                Selected = previous?.Selected ?? low,
                Quantity = previous?.Quantity ?? SuggestedQuantity(c.QuantityOnHand, c.ReorderLevel)
            };
        })
        .OrderBy(l => l.SupplierName).ThenByDescending(l => l.NeedsReorder).ThenBy(l => l.ConsumableName)
        .ToList();

        return new OrderCreateModel
        {
            OrderNumber = await _data.Orders.PeekNextOrderNumberAsync(),
            SupplierId = supplierId,
            OrderItems = lines,
            SupplierOptions = await _data.Suppliers.GetLookupAsync()
        };
    }

    public async Task<LabManagerTaskResult<List<int>>> PlaceOrdersAsync(OrderCreateModel form, int managerUserId)
    {
        var result = new LabManagerTaskResult();
        var selected = (form.OrderItems ?? new()).Where(l => l.Selected && l.ConsumableId > 0).ToList();

        if (selected.Count == 0)
            result.AddError(nameof(form.OrderItems), "Select at least one consumable to order");
        if (selected.Any(l => l.Quantity is null or < 1))
            result.AddError(nameof(form.OrderItems), "Enter a quantity of at least 1 for every selected consumable");

        // The same consumable twice -> add the quantities together
        var lines = selected
            .Where(l => l.Quantity is >= 1)
            .GroupBy(l => l.ConsumableId)
            .Select(g => new NewOrderLine(g.Key, g.Sum(l => l.Quantity!.Value)))
            .ToList();

        var consumables = await _data.Consumables.GetByIdsAsync(lines.Select(l => l.ConsumableId));
        if (consumables.Count != lines.Count)
            result.AddError(nameof(form.OrderItems), "One or more selected consumables no longer exist");

        // [Order].LabManagerID is required in grp-04-14: the order is recorded against the logged-in manager
        var manager = await _data.Accounts.GetUserByIdAsync(managerUserId);
        if (manager?.LabManagerId is null)
            result.AddError("Only a laboratory manager account can place orders.");

        if (!result.Succeeded) return LabManagerTaskResult<List<int>>.From(result);

        // AC-05-02: one order per supplier. The supplier comes from the database, not the form.
        var newOrders = consumables
            .GroupBy(c => c.SupplierId)
            .Select(g => new NewOrder(g.Key, g.Select(c => lines.First(l => l.ConsumableId == c.ConsumableId)).ToList()))
            .ToList();

        var orderIds = await _data.Orders.CreateOrdersAsync(newOrders, manager!.LabManagerId!.Value);

        // AC-05-03: email each supplier AFTER saving, so a mail problem can't lose the order
        var placed = new List<string>();
        var emailFailed = new List<string>();
        foreach (int orderId in orderIds)
        {
            var order = await _data.Orders.GetByIdAsync(orderId);
            if (order is null) continue;
            placed.Add($"{order.OrderNumber} ({order.SupplierName})");
            if (!await _email.SendOrderPlacedAsync(order)) emailFailed.Add(order.SupplierName);
        }

        var done = LabManagerTaskResult<List<int>>.From(new LabManagerTaskResult(), orderIds);
        done.Message = placed.Count == 1
            ? $"Order {placed[0]} was placed and the supplier was emailed."
            : $"{placed.Count} orders were placed, one per supplier: {string.Join(", ", placed)}.";
        if (emailFailed.Count > 0)
            done.Warning = $"The order was saved, but the email to {string.Join(", ", emailFailed)} could not be sent. Check the email settings.";
        return done;
    }

    public async Task<OrderDetailModel?> GetOrderDetailAsync(int orderId)
    {
        var order = await _data.Orders.GetByIdAsync(orderId);
        return order is null ? null : new OrderDetailModel { Order = order };
    }

    public async Task<LabManagerTaskResult<int>> ReceiveItemAsync(int orderItemId)
    {
        int? orderId = await _data.Orders.GetOrderIdForItemAsync(orderItemId);
        if (orderId is null)
            return LabManagerTaskResult<int>.From(LabManagerTaskResult.Missing("That order item no longer exists."));

        if (await _data.Orders.ReceiveItemAsync(orderItemId) != OrderActionResult.Success)
            return LabManagerTaskResult<int>.From(LabManagerTaskResult.Failed("That item has already been received or cancelled."), orderId.Value);

        var order = await _data.Orders.GetByIdAsync(orderId.Value);
        var item = order?.Items.FirstOrDefault(i => i.OrderItemId == orderItemId);
        var ok = LabManagerTaskResult.Ok(item is null
            ? "Item received."
            : $"{item.ConsumableName} received: stock on hand increased by {item.QtyOrdered}. Order status is now {order!.Status}.");
        return LabManagerTaskResult<int>.From(ok, orderId.Value);
    }

    public async Task<LabManagerTaskResult<int>> CancelItemAsync(int orderItemId, CancelModel form)
    {
        int? orderId = await _data.Orders.GetOrderIdForItemAsync(orderItemId);
        if (orderId is null)
            return LabManagerTaskResult<int>.From(LabManagerTaskResult.Missing("That order item no longer exists."));

        string reason = LmText.Clean(form.CancellationReason);
        if (reason.Length == 0 || reason.Length > 500)
            return LabManagerTaskResult<int>.From(LabManagerTaskResult.Failed(
                reason.Length == 0 ? "A reason for cancellation is required" : "The reason can be at most 500 characters"), orderId.Value);

        if (await _data.Orders.CancelItemAsync(orderItemId, reason) != OrderActionResult.Success)
            return LabManagerTaskResult<int>.From(LabManagerTaskResult.Failed("That item has already been received or cancelled."), orderId.Value);

        var order = await _data.Orders.GetByIdAsync(orderId.Value);
        var item = order?.Items.FirstOrDefault(i => i.OrderItemId == orderItemId);
        bool emailed = order is not null && item is not null && await _email.SendOrderItemCancelledAsync(order, item, reason);

        var ok = LabManagerTaskResult.Ok($"{item?.ConsumableName ?? "The item"} was cancelled. Order status is now {order?.Status}.");
        if (!emailed) ok.Warning = "The item was cancelled, but the email to the supplier could not be sent.";
        return LabManagerTaskResult<int>.From(ok, orderId.Value);
    }

    public async Task<LabManagerTaskResult> CancelOrderAsync(int orderId, CancelModel form)
    {
        string reason = LmText.Clean(form.CancellationReason);
        if (reason.Length == 0) return LabManagerTaskResult.Failed("A reason for cancellation is required");
        if (reason.Length > 500) return LabManagerTaskResult.Failed("The reason can be at most 500 characters");

        switch (await _data.Orders.CancelOrderAsync(orderId, reason))
        {
            case OrderActionResult.NotFound: return LabManagerTaskResult.Missing("That order no longer exists.");
            case OrderActionResult.NotOutstanding: return LabManagerTaskResult.Failed("This order is already complete or cancelled.");
        }

        var order = await _data.Orders.GetByIdAsync(orderId);
        bool emailed = order is not null && await _email.SendOrderCancelledAsync(order, reason);

        var ok = LabManagerTaskResult.Ok($"Order {order?.OrderNumber} was cancelled.");
        if (!emailed) ok.Warning = "The order was cancelled, but the email to the supplier could not be sent.";
        return ok;
    }

    /// <summary>A starting quantity that brings stock back up to twice the reorder level.</summary>
    private static int SuggestedQuantity(int onHand, int reorderLevel)
    {
        int target = Math.Max(reorderLevel * 2, 1);
        return Math.Max(target - onHand, Math.Max(reorderLevel, 1));
    }

    private static string? NormaliseStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => null,
        "ordered" => OrderStatus.Ordered,
        "partial" or "partially complete" => OrderStatus.PartiallyComplete,
        "complete" => OrderStatus.Complete,
        "cancelled" => OrderStatus.Cancelled,
        _ => null
    };
}
