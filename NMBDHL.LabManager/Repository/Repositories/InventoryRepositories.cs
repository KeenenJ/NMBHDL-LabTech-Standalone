#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Data.Repositories;

// Tables (grp-04-14): Consumable(ConsumableID, Name, ReOrderLevel, QtyOnHand, SupplierID)
//                     Supplier(SupplierID, Name, ContactPerson, Email)
// ReOrderLevel, ContactPerson and Email allow NULL in grp-04-14, so they are read with ISNULL.

// =====================================================================
// US-LM-03  Consumable table
// =====================================================================
public enum StockAdjustOutcome
{
    Adjusted,
    NotFound,
    Rejected   // the rule refused it, e.g. "Stock cannot go below zero"
}

public record StockAdjustResponse(StockAdjustOutcome Outcome, string ConsumableName, int OldQuantity, int NewQuantity, string? Error);

public interface IConsumableRepository
{
    Task<List<ConsumableRow>> GetAllAsync(int? supplierId = null);
    Task<ConsumableRow?> GetByIdAsync(int consumableId);
    Task<List<ConsumableRow>> GetByIdsAsync(IEnumerable<int> consumableIds);
    Task<bool> NameExistsAsync(string name, int excludeConsumableId = 0);
    Task<int> CreateAsync(string name, int supplierId, int reorderLevel, int quantityOnHand);
    Task<bool> UpdateAsync(int consumableId, string name, int supplierId, int reorderLevel);
    Task<StockAdjustResponse> AdjustStockAsync(int consumableId, StockAdjustAction action, int amount);
    Task<List<LookupItem>> GetLookupAsync();
    Task<List<ConsumableRow>> GetNeedingReorderAsync(int top);
}

public class ConsumableRepository : IConsumableRepository
{
    private readonly ILabManagerDb _db;
    public ConsumableRepository(ILabManagerDb db) { _db = db; }

    private const string SelectConsumable = @"
        SELECT c.ConsumableID AS ConsumableId,
               c.Name         AS ConsumableName,
               c.SupplierID   AS SupplierId,
               s.Name         AS SupplierName,
               ISNULL(c.ReOrderLevel, 0) AS ReorderLevel,
               c.QtyOnHand    AS QuantityOnHand
          FROM dbo.Consumable c
          JOIN dbo.Supplier   s ON s.SupplierID = c.SupplierID";

    // AC-03-01: name, quantity on hand, reorder level and supplier.
    // The low-stock highlight (AC-03-08) is worked out by ConsumableRow.NeedsReorder.
    public async Task<List<ConsumableRow>> GetAllAsync(int? supplierId = null)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ConsumableRow>(SelectConsumable + @"
             WHERE (@supplierId IS NULL OR c.SupplierID = @supplierId)
             ORDER BY c.Name;", new { supplierId });
        return rows.ToList();
    }

    public async Task<ConsumableRow?> GetByIdAsync(int consumableId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<ConsumableRow>(SelectConsumable + @"
             WHERE c.ConsumableID = @consumableId;", new { consumableId });
    }

    public async Task<List<ConsumableRow>> GetByIdsAsync(IEnumerable<int> consumableIds)
    {
        var ids = consumableIds.Distinct().ToList();
        if (ids.Count == 0) return new List<ConsumableRow>();

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ConsumableRow>(SelectConsumable + @"
             WHERE c.ConsumableID IN @ids;", new { ids });
        return rows.ToList();
    }

    // AC-03-03: "Consumable name already exists"
    public async Task<bool> NameExistsAsync(string name, int excludeConsumableId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Consumable WHERE Name = @name AND ConsumableID <> @excludeConsumableId
            ) THEN 1 ELSE 0 END;", new { name = name.Trim(), excludeConsumableId });
    }

    // AC-03-02
    public async Task<int> CreateAsync(string name, int supplierId, int reorderLevel, int quantityOnHand)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.Consumable (Name, ReOrderLevel, QtyOnHand, SupplierID)
            OUTPUT INSERTED.ConsumableID
            VALUES (@name, @reorderLevel, @quantityOnHand, @supplierId);",
            new { name, reorderLevel, quantityOnHand, supplierId });
    }

    // QtyOnHand is NOT changed here: stock only moves through Adjust Stock or receiving orders.
    public async Task<bool> UpdateAsync(int consumableId, string name, int supplierId, int reorderLevel)
    {
        using var conn = _db.CreateConnection();
        int rows = await conn.ExecuteAsync(@"
            UPDATE dbo.Consumable
               SET Name = @name, SupplierID = @supplierId, ReOrderLevel = @reorderLevel
             WHERE ConsumableID = @consumableId;",
            new { consumableId, name, supplierId, reorderLevel });
        return rows > 0;
    }

    private sealed class StockRow
    {
        public string Name { get; set; } = "";
        public int QtyOnHand { get; set; }
    }

    /// <summary>
    /// AC-03-04 increase, AC-03-05 decrease, AC-03-06 never below zero, AC-03-07 set exact value.
    /// The row is read WITH (UPDLOCK) inside a transaction, so two adjustments made at the
    /// same moment can't overwrite each other.
    /// </summary>
    public async Task<StockAdjustResponse> AdjustStockAsync(int consumableId, StockAdjustAction action, int amount)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var row = await conn.QueryFirstOrDefaultAsync<StockRow>(@"
            SELECT Name, QtyOnHand
              FROM dbo.Consumable WITH (UPDLOCK, ROWLOCK)
             WHERE ConsumableID = @consumableId;", new { consumableId }, tx);

        if (row is null)
            return new StockAdjustResponse(StockAdjustOutcome.NotFound, "", 0, 0, "Consumable not found");

        var result = StockRules.Apply(row.QtyOnHand, action, amount);
        if (!result.Success)
            return new StockAdjustResponse(StockAdjustOutcome.Rejected, row.Name, row.QtyOnHand, row.QtyOnHand, result.Error);

        await conn.ExecuteAsync(
            "UPDATE dbo.Consumable SET QtyOnHand = @newQty WHERE ConsumableID = @consumableId;",
            new { newQty = result.NewQuantity, consumableId }, tx);

        tx.Commit();
        return new StockAdjustResponse(StockAdjustOutcome.Adjusted, row.Name, row.QtyOnHand, result.NewQuantity, null);
    }

    public async Task<List<LookupItem>> GetLookupAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT ConsumableID AS Id, Name FROM dbo.Consumable ORDER BY Name;");
        return rows.ToList();
    }

    /// <summary>
    /// AC-03-08 / AC-05-01: at or within 10% above the reorder level, lowest stock first.
    /// QtyOnHand * 10 &lt;= ReorderLevel * 11 is the same rule as StockRules.NeedsReorder.
    /// </summary>
    public async Task<List<ConsumableRow>> GetNeedingReorderAsync(int top)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<ConsumableRow>(@"
            SELECT TOP (@top)
                   c.ConsumableID AS ConsumableId,
                   c.Name         AS ConsumableName,
                   c.SupplierID   AS SupplierId,
                   s.Name         AS SupplierName,
                   ISNULL(c.ReOrderLevel, 0) AS ReorderLevel,
                   c.QtyOnHand    AS QuantityOnHand
              FROM dbo.Consumable c
              JOIN dbo.Supplier   s ON s.SupplierID = c.SupplierID
             WHERE CAST(c.QtyOnHand AS BIGINT) * 10 <= CAST(ISNULL(c.ReOrderLevel, 0) AS BIGINT) * 11
             ORDER BY CASE WHEN ISNULL(c.ReOrderLevel, 0) = 0 THEN 1.0
                           ELSE CAST(c.QtyOnHand AS DECIMAL(18,4)) / c.ReOrderLevel END,
                      c.Name;", new { top });
        return rows.ToList();
    }
}

// =====================================================================
// US-LM-04  Supplier table
// =====================================================================
public enum SupplierDeleteResult
{
    Deleted,
    HasConsumables,
    HasOrders,
    NotFound
}

public interface ISupplierRepository
{
    Task<List<SupplierRow>> GetAllAsync();
    Task<SupplierFormModel?> GetByIdAsync(int supplierId);
    Task<bool> NameExistsAsync(string name, int excludeSupplierId = 0);
    Task<int> CreateAsync(string name, string contactPerson, string email);
    Task<bool> UpdateAsync(int supplierId, string name, string contactPerson, string email);
    Task<SupplierDeleteResult> DeleteAsync(int supplierId);
    Task<List<LookupItem>> GetLookupAsync();
    Task<bool> ExistsAsync(int supplierId);
}

public class SupplierRepository : ISupplierRepository
{
    private readonly ILabManagerDb _db;
    public SupplierRepository(ILabManagerDb db) { _db = db; }

    // AC-04-01: name, contact person and email
    public async Task<List<SupplierRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<SupplierRow>(@"
            SELECT s.SupplierID AS SupplierId,
                   s.Name       AS SupplierName,
                   ISNULL(s.ContactPerson, N'') AS ContactPerson,
                   ISNULL(s.Email, N'')         AS Email,
                   (SELECT COUNT(*) FROM dbo.Consumable c WHERE c.SupplierID = s.SupplierID) AS ConsumableCount
              FROM dbo.Supplier s
             ORDER BY s.Name;");
        return rows.ToList();
    }

    public async Task<SupplierFormModel?> GetByIdAsync(int supplierId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SupplierFormModel>(@"
            SELECT SupplierID AS SupplierId, Name AS SupplierName, ISNULL(ContactPerson, N'') AS ContactPerson, ISNULL(Email, N'') AS Email
              FROM dbo.Supplier
             WHERE SupplierID = @supplierId;", new { supplierId });
    }

    // AC-04-03: "Supplier name already exists"
    public async Task<bool> NameExistsAsync(string name, int excludeSupplierId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Supplier WHERE Name = @name AND SupplierID <> @excludeSupplierId
            ) THEN 1 ELSE 0 END;", new { name = name.Trim(), excludeSupplierId });
    }

    // AC-04-02
    public async Task<int> CreateAsync(string name, string contactPerson, string email)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.Supplier (Name, ContactPerson, Email)
            OUTPUT INSERTED.SupplierID
            VALUES (@name, @contactPerson, @email);", new { name, contactPerson, email });
    }

    // AC-04-05
    public async Task<bool> UpdateAsync(int supplierId, string name, string contactPerson, string email)
    {
        using var conn = _db.CreateConnection();
        int rows = await conn.ExecuteAsync(@"
            UPDATE dbo.Supplier
               SET Name = @name, ContactPerson = @contactPerson, Email = @email
             WHERE SupplierID = @supplierId;", new { supplierId, name, contactPerson, email });
        return rows > 0;
    }

    // AC-04-06: refused while consumables are linked. Also refused while orders exist,
    // because deleting it would break the order history (it would hit a foreign key error).
    public async Task<SupplierDeleteResult> DeleteAsync(int supplierId)
    {
        using var conn = _db.CreateConnection();
        int code = await conn.ExecuteScalarAsync<int>(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.Supplier WHERE SupplierID = @supplierId)
                SELECT 0;
            ELSE IF EXISTS (SELECT 1 FROM dbo.Consumable WHERE SupplierID = @supplierId)
                SELECT 1;
            ELSE IF EXISTS (SELECT 1 FROM dbo.[Order] WHERE SupplierID = @supplierId)
                SELECT 2;
            ELSE
            BEGIN
                DELETE FROM dbo.Supplier WHERE SupplierID = @supplierId;
                SELECT 3;
            END", new { supplierId });

        return code switch
        {
            0 => SupplierDeleteResult.NotFound,
            1 => SupplierDeleteResult.HasConsumables,
            2 => SupplierDeleteResult.HasOrders,
            _ => SupplierDeleteResult.Deleted
        };
    }

    public async Task<List<LookupItem>> GetLookupAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT SupplierID AS Id, Name FROM dbo.Supplier ORDER BY Name;");
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(int supplierId)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Supplier WHERE SupplierID = @supplierId)
                        THEN 1 ELSE 0 END;", new { supplierId });
    }
}
