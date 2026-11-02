#nullable enable
using System;
using System.Threading.Tasks;
using NMBHLabSys.LabManagerSubsystem.Data;
using NMBHLabSys.LabManagerSubsystem.Data.Repositories;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Services;

/// <summary>US-LM-03 consumables and stock, US-LM-04 suppliers. One method per task.</summary>
public interface ILabManagerInventoryService
{
    // ---- US-LM-03 ----
    Task<ConsumableIndexModel> GetConsumableListAsync();                                     // AC-03-01 / 03-08
    Task<LabManagerTaskResult> AddConsumableAsync(ConsumableCreateModel form);               // AC-03-02 / 03-03
    Task<ConsumableEditModel?> GetConsumableEditAsync(int consumableId);
    Task<ConsumableEditModel> FillConsumableEditAsync(ConsumableEditModel form);
    Task<LabManagerTaskResult> EditConsumableAsync(int consumableId, ConsumableEditModel form);
    Task<LabManagerTaskResult> AdjustStockAsync(AdjustStockModel form);                      // AC-03-04..07

    // ---- US-LM-04 ----
    Task<SupplierIndexModel> GetSupplierListAsync();                                         // AC-04-01
    Task<SupplierFormModel?> GetSupplierAsync(int supplierId);
    Task<LabManagerTaskResult> AddSupplierAsync(SupplierFormModel form);                     // AC-04-02/03/04
    Task<LabManagerTaskResult> EditSupplierAsync(int supplierId, SupplierFormModel form);    // AC-04-05
    Task<LabManagerTaskResult> DeleteSupplierAsync(int supplierId);                          // AC-04-06
}

public class LabManagerInventoryService : ILabManagerInventoryService
{
    private readonly ILabManagerData _data;
    public LabManagerInventoryService(ILabManagerData data) { _data = data; }

    // =================================================================
    // US-LM-03  Consumables
    // =================================================================
    public async Task<ConsumableIndexModel> GetConsumableListAsync() => new()
    {
        Consumables = await _data.Consumables.GetAllAsync(),
        SupplierOptions = await _data.Suppliers.GetLookupAsync()
    };

    public async Task<LabManagerTaskResult> AddConsumableAsync(ConsumableCreateModel form)
    {
        form.ConsumableName = LmText.Clean(form.ConsumableName);
        var result = LabManagerTaskResult.FromAnnotations(form);

        if (!result.HasErrorFor(nameof(form.ConsumableName)) && await _data.Consumables.NameExistsAsync(form.ConsumableName))
            result.AddError(nameof(form.ConsumableName), LabManagerMessages.ConsumableExists);

        if (form.SupplierId is int supplierId && !await _data.Suppliers.ExistsAsync(supplierId))
            result.AddError(nameof(form.SupplierId), "Select a supplier from the list");

        if (!result.Succeeded) return result;

        try
        {
            await _data.Consumables.CreateAsync(form.ConsumableName, form.SupplierId!.Value, form.ReorderLevel!.Value, form.QuantityOnHand!.Value);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.ConsumableName), LabManagerMessages.ConsumableExists);
        }
        return LabManagerTaskResult.Ok($"Consumable '{form.ConsumableName}' was added with {form.QuantityOnHand} in stock.");
    }

    public async Task<ConsumableEditModel?> GetConsumableEditAsync(int consumableId)
    {
        var row = await _data.Consumables.GetByIdAsync(consumableId);
        if (row is null) return null;

        return new ConsumableEditModel
        {
            ConsumableId = row.ConsumableId,
            ConsumableName = row.ConsumableName,
            SupplierId = row.SupplierId,
            ReorderLevel = row.ReorderLevel,
            CurrentStock = row.QuantityOnHand,
            SupplierOptions = await _data.Suppliers.GetLookupAsync()
        };
    }

    public async Task<ConsumableEditModel> FillConsumableEditAsync(ConsumableEditModel form)
    {
        var row = await _data.Consumables.GetByIdAsync(form.ConsumableId);
        form.CurrentStock = row?.QuantityOnHand ?? form.CurrentStock;
        form.SupplierOptions = await _data.Suppliers.GetLookupAsync();
        return form;
    }

    /// <summary>Name, supplier and reorder level. Stock only changes through AdjustStock or receiving orders.</summary>
    public async Task<LabManagerTaskResult> EditConsumableAsync(int consumableId, ConsumableEditModel form)
    {
        form.ConsumableId = consumableId;
        form.ConsumableName = LmText.Clean(form.ConsumableName);

        if (await _data.Consumables.GetByIdAsync(consumableId) is null)
            return LabManagerTaskResult.Missing("That consumable no longer exists.");

        var result = LabManagerTaskResult.FromAnnotations(form);
        if (!result.HasErrorFor(nameof(form.ConsumableName))
            && await _data.Consumables.NameExistsAsync(form.ConsumableName, consumableId))
            result.AddError(nameof(form.ConsumableName), LabManagerMessages.ConsumableExists);

        if (form.SupplierId is int supplierId && !await _data.Suppliers.ExistsAsync(supplierId))
            result.AddError(nameof(form.SupplierId), "Select a supplier from the list");

        if (!result.Succeeded) return result;

        try
        {
            await _data.Consumables.UpdateAsync(consumableId, form.ConsumableName, form.SupplierId!.Value, form.ReorderLevel!.Value);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.ConsumableName), LabManagerMessages.ConsumableExists);
        }
        return LabManagerTaskResult.Ok($"Consumable '{form.ConsumableName}' was updated.");
    }

    /// <summary>
    /// TC-LM-03-02: 200 + 50 = 250.  TC-LM-03-03: 200 - 30 = 170.
    /// TC-LM-03-04: 10 - 50 -> "Stock cannot go below zero".  TC-LM-03-05: set to 75.
    /// </summary>
    public async Task<LabManagerTaskResult> AdjustStockAsync(AdjustStockModel form)
    {
        if (!StockRules.TryParseAction(form.Action, out var action))
            return LabManagerTaskResult.Failed("Choose increase, decrease or set.");
        if (form.Amount is null)
            return LabManagerTaskResult.Failed("Enter a quantity.");

        var outcome = await _data.Consumables.AdjustStockAsync(form.ConsumableId, action, form.Amount.Value);
        return outcome.Outcome switch
        {
            StockAdjustOutcome.Adjusted => LabManagerTaskResult.Ok(
                $"{outcome.ConsumableName}: quantity on hand changed from {outcome.OldQuantity} to {outcome.NewQuantity}."),
            StockAdjustOutcome.Rejected => LabManagerTaskResult.Failed($"{outcome.ConsumableName}: {outcome.Error}"),
            _ => LabManagerTaskResult.Missing("That consumable no longer exists.")
        };
    }

    // =================================================================
    // US-LM-04  Suppliers
    // =================================================================
    public async Task<SupplierIndexModel> GetSupplierListAsync() => new() { Suppliers = await _data.Suppliers.GetAllAsync() };

    public Task<SupplierFormModel?> GetSupplierAsync(int supplierId) => _data.Suppliers.GetByIdAsync(supplierId);

    public async Task<LabManagerTaskResult> AddSupplierAsync(SupplierFormModel form)
    {
        var result = await ValidateSupplierAsync(form, excludeSupplierId: 0);
        if (!result.Succeeded) return result;

        try
        {
            await _data.Suppliers.CreateAsync(form.SupplierName!, form.ContactPerson!, form.Email!);
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.SupplierName), LabManagerMessages.SupplierExists);
        }
        return LabManagerTaskResult.Ok($"Supplier '{form.SupplierName}' was added.");
    }

    public async Task<LabManagerTaskResult> EditSupplierAsync(int supplierId, SupplierFormModel form)
    {
        form.SupplierId = supplierId;
        var result = await ValidateSupplierAsync(form, excludeSupplierId: supplierId);
        if (!result.Succeeded) return result;

        try
        {
            if (!await _data.Suppliers.UpdateAsync(supplierId, form.SupplierName!, form.ContactPerson!, form.Email!))
                return LabManagerTaskResult.Missing("That supplier no longer exists.");
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsUniqueViolation(ex))
        {
            return result.AddError(nameof(form.SupplierName), LabManagerMessages.SupplierExists);
        }
        return LabManagerTaskResult.Ok($"Supplier '{form.SupplierName}' was updated.");
    }

    public async Task<LabManagerTaskResult> DeleteSupplierAsync(int supplierId) =>
        await _data.Suppliers.DeleteAsync(supplierId) switch
        {
            SupplierDeleteResult.Deleted => LabManagerTaskResult.Ok("Supplier deleted."),
            SupplierDeleteResult.HasConsumables => LabManagerTaskResult.Failed(LabManagerMessages.SupplierHasConsumables),
            SupplierDeleteResult.HasOrders => LabManagerTaskResult.Failed("Supplier has orders on record and cannot be deleted"),
            _ => LabManagerTaskResult.Missing("That supplier no longer exists.")
        };

    private async Task<LabManagerTaskResult> ValidateSupplierAsync(SupplierFormModel form, int excludeSupplierId)
    {
        form.SupplierName = LmText.Clean(form.SupplierName);
        form.ContactPerson = LmText.Clean(form.ContactPerson);
        form.Email = LmText.Clean(form.Email);

        var result = LabManagerTaskResult.FromAnnotations(form);   // blanks + "Invalid email address" (AC-04-04)
        if (!result.HasErrorFor(nameof(form.SupplierName))
            && await _data.Suppliers.NameExistsAsync(form.SupplierName, excludeSupplierId))
            result.AddError(nameof(form.SupplierName), LabManagerMessages.SupplierExists);   // AC-04-03
        return result;
    }
}
