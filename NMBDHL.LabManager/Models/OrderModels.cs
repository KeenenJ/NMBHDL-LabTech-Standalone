#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

// =====================================================================
// US-LM-05  Consumable orders
//   OrderIndex.cshtml  -> OrderIndexModel
//   OrderCreate.cshtml -> OrderCreateModel
//   OrderDetail.cshtml -> OrderDetailModel
//       its cancel dialog posts CancelModel (CancellationReason)
// =====================================================================
public class OrderItemRow
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public int ConsumableId { get; set; }
    public string ConsumableName { get; set; } = "";
    public int QtyOrdered { get; set; }
    public string Status { get; set; } = OrderItemStatus.Ordered;
    public DateTime? DateReceived { get; set; }
    public DateTime? DateCancelled { get; set; }
    public string? CancelReason { get; set; }

    public bool IsOutstanding => Status == OrderItemStatus.Ordered;
    public bool IsReceived => Status == OrderItemStatus.Received;
    public bool IsCancelled => Status == OrderItemStatus.Cancelled;
}

public class OrderRow
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string? SupplierEmail { get; set; }
    public string? SupplierContact { get; set; }
    public string Status { get; set; } = OrderStatus.Ordered;
    public DateTime DateOrdered { get; set; }
    public DateTime? DateComplete { get; set; }
    public DateTime? DateCancelled { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>AC-05-09: every item with its own status.</summary>
    public List<OrderItemRow> Items { get; set; } = new();

    /// <summary>"ordered" / "partial" / "complete" / "cancelled" for the page's filter.</summary>
    public string StatusKey => OrderStatus.FilterKey(Status);

    public bool IsOpen => Status == OrderStatus.Ordered || Status == OrderStatus.PartiallyComplete;
    public int ReceivedCount => Items.Count(i => i.IsReceived);
    public int ItemCount => Items.Count;
}

public class OrderIndexModel
{
    public List<OrderRow> Orders { get; set; } = new();

    /// <summary>Optional ?status= filter (Ordered, Partially Complete, Complete, Cancelled).</summary>
    public string? StatusFilter { get; set; }
}

/// <summary>One row on the Create Order page. Posted as OrderItems[i].Selected / .ConsumableId / .Quantity.</summary>
public class OrderCreateLine
{
    public bool Selected { get; set; }
    public int ConsumableId { get; set; }
    public int? Quantity { get; set; }

    // ----- display only (not posted) -----
    public string ConsumableName { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int CurrentStock { get; set; }
    public int ReorderLevel { get; set; }

    public StockLevel Level => StockRules.GetLevel(CurrentStock, ReorderLevel);
    public bool NeedsReorder => Level != StockLevel.Ok;
    public string StatusLabel => StockRules.Label(Level);
}

/// <summary>AC-05-01: consumables grouped by supplier.</summary>
public class OrderSupplierGroup
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public List<OrderCreateLine> Lines { get; set; } = new();
}

public class OrderCreateModel
{
    /// <summary>Preview of the next number. The real number is given when the order is saved.</summary>
    public string? OrderNumber { get; set; }

    /// <summary>
    /// The supplier dropdown. When it is chosen, the page lists only that supplier's consumables.
    /// When posting, items are grouped by each consumable's own supplier anyway (AC-05-02),
    /// so one order is created per supplier.
    /// </summary>
    public int? SupplierId { get; set; }

    public List<OrderCreateLine> OrderItems { get; set; } = new();

    public List<LookupItem> SupplierOptions { get; set; } = new();

    /// <summary>The same rows as OrderItems, grouped by supplier, low-stock first.</summary>
    public List<OrderSupplierGroup> SupplierGroups =>
        OrderItems
            .GroupBy(l => new { l.SupplierId, l.SupplierName })
            .OrderBy(g => g.Key.SupplierName)
            .Select(g => new OrderSupplierGroup
            {
                SupplierId = g.Key.SupplierId,
                SupplierName = g.Key.SupplierName,
                Lines = g.OrderByDescending(l => l.NeedsReorder).ThenBy(l => l.ConsumableName).ToList()
            })
            .ToList();

    public int SelectedCount => OrderItems.Count(l => l.Selected);
}

public class OrderDetailModel
{
    public OrderRow Order { get; set; } = new();

    public bool CanCancelOrder => Order.IsOpen && Order.Items.Any(i => i.IsOutstanding);
}

/// <summary>Posted by the cancel dialog on OrderDetail (whole order or one item).</summary>
public class CancelModel
{
    [Required(ErrorMessage = "A reason for cancellation is required")]
    [StringLength(500, ErrorMessage = "The reason can be at most 500 characters")]
    public string? CancellationReason { get; set; }
}
