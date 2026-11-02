#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NMBHLabSys.LabManagerSubsystem.Infrastructure;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

// =====================================================================
// US-LM-03  Consumables
//   ConsumableIndex.cshtml -> ConsumableIndexModel
//       its "Add Consumable" dialog posts ConsumableCreateModel
//       its "Confirm stock adjustment" dialog posts AdjustStockModel
//   ConsumableEdit.cshtml  -> ConsumableEditModel
// =====================================================================
public class ConsumableRow
{
    public int ConsumableId { get; set; }
    public string ConsumableName { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int ReorderLevel { get; set; }
    public int QuantityOnHand { get; set; }

    public StockLevel Level => StockRules.GetLevel(QuantityOnHand, ReorderLevel);

    /// <summary>AC-03-08: highlight the row (class "row-alert").</summary>
    public bool NeedsReorder => Level != StockLevel.Ok;

    /// <summary>"below" / "near" / "ok" for data-status (the page's filter).</summary>
    public string StatusKey => StockRules.FilterKey(Level);

    /// <summary>"Below Reorder" / "Near Reorder" / "OK" for the badge.</summary>
    public string StatusLabel => StockRules.Label(Level);
}

public class ConsumableIndexModel
{
    public List<ConsumableRow> Consumables { get; set; } = new();

    /// <summary>Supplier dropdown in the "Add Consumable" dialog.</summary>
    public List<LookupItem> SupplierOptions { get; set; } = new();
}

/// <summary>Posted by the Add Consumable dialog: ConsumableName, SupplierId, ReorderLevel, QuantityOnHand.</summary>
public class ConsumableCreateModel
{
    [Required(ErrorMessage = "Consumable name is required")]
    [StringLength(200, ErrorMessage = "Consumable name can be at most 200 characters")]
    public string? ConsumableName { get; set; }

    [Required(ErrorMessage = "Select a supplier")]
    public int? SupplierId { get; set; }

    [Required(ErrorMessage = "Reorder level is required")]
    [Range(0, int.MaxValue, ErrorMessage = "Reorder level cannot be negative")]
    public int? ReorderLevel { get; set; }

    [Required(ErrorMessage = "Quantity on hand is required")]
    [Range(0, int.MaxValue, ErrorMessage = "Quantity on hand cannot be negative")]
    public int? QuantityOnHand { get; set; }
}

/// <summary>Posted by ConsumableEdit: ConsumableId, ConsumableName, SupplierId, ReorderLevel.</summary>
public class ConsumableEditModel
{
    public int ConsumableId { get; set; }

    [Required(ErrorMessage = "Consumable name is required")]
    [StringLength(200, ErrorMessage = "Consumable name can be at most 200 characters")]
    public string? ConsumableName { get; set; }

    [Required(ErrorMessage = "Select a supplier")]
    public int? SupplierId { get; set; }

    [Required(ErrorMessage = "Reorder level is required")]
    [Range(0, int.MaxValue, ErrorMessage = "Reorder level cannot be negative")]
    public int? ReorderLevel { get; set; }

    /// <summary>Read-only on the page. Stock only changes through Adjust Stock or receiving orders.</summary>
    public int CurrentStock { get; set; }

    public List<LookupItem> SupplierOptions { get; set; } = new();
}

/// <summary>Posted by the stock adjustment dialog: ConsumableId, Action (Increase/Decrease/Set), Amount.</summary>
public class AdjustStockModel
{
    public int ConsumableId { get; set; }

    [Required(ErrorMessage = "Choose increase, decrease or set")]
    public string? Action { get; set; }

    [Required(ErrorMessage = "Enter a quantity")]
    public int? Amount { get; set; }
}

// =====================================================================
// US-LM-04  Suppliers
//   SupplierIndex.cshtml  -> SupplierIndexModel
//   SupplierCreate.cshtml -> SupplierFormModel
//   SupplierEdit.cshtml   -> SupplierFormModel
// =====================================================================
public class SupplierRow
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string Email { get; set; } = "";
    public int ConsumableCount { get; set; }
}

public class SupplierIndexModel
{
    public List<SupplierRow> Suppliers { get; set; } = new();
}

/// <summary>Posted by SupplierCreate and SupplierEdit: SupplierId, SupplierName, ContactPerson, Email.</summary>
public class SupplierFormModel
{
    public int SupplierId { get; set; }

    [Required(ErrorMessage = "Supplier name is required")]
    [StringLength(200, ErrorMessage = "Supplier name can be at most 200 characters")]
    public string? SupplierName { get; set; }

    [Required(ErrorMessage = "Contact person is required")]
    [StringLength(200, ErrorMessage = "Contact person can be at most 200 characters")]
    public string? ContactPerson { get; set; }

    // AC-04-04 / TC-LM-04-03: the exact message from the test plan.
    [Required(ErrorMessage = "Email address is required")]
    [ValidEmail]
    [StringLength(255, ErrorMessage = "Email can be at most 255 characters")]
    public string? Email { get; set; }
}
