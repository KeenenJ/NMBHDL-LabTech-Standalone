#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Data.Repositories;

/// <summary>One supplier's new order: (consumable, quantity) lines.</summary>
public record NewOrder(int SupplierId, IReadOnlyList<NewOrderLine> Lines);
public record NewOrderLine(int ConsumableId, int Quantity);

public enum OrderActionResult
{
    Success,
    NotFound,        // the order / item doesn't exist
    NotOutstanding   // already received, cancelled or the order is closed
}

// Tables (grp-04-14):
//   [Order](OrderID, OrderNumber, Status, CreatedAt, DateComplete, DateCancelled, CancelReason, LabManagerID, SupplierID)
//   OrderItem(OrderItemID, QtyOrdered, Status, dateReceived, DateCancelled, CancelReason, OrderID, ConsumableID)
// Status allows NULL in grp-04-14; a NULL status is treated as 'Ordered'.

// =====================================================================
// US-LM-05  [Order] and OrderItem tables
// =====================================================================
public interface IOrderRepository
{
    Task<List<OrderRow>> GetAllAsync(string? status = null, int? top = null);
    Task<OrderRow?> GetByIdAsync(int orderId);
    Task<int?> GetOrderIdForItemAsync(int orderItemId);
    Task<string> PeekNextOrderNumberAsync();
    Task<List<int>> CreateOrdersAsync(IEnumerable<NewOrder> orders, int labManagerId);
    Task<OrderActionResult> ReceiveItemAsync(int orderItemId);
    Task<OrderActionResult> CancelItemAsync(int orderItemId, string reason);
    Task<OrderActionResult> CancelOrderAsync(int orderId, string reason);
    Task<int> CountOpenAsync();
}

public class OrderRepository : IOrderRepository
{
    private readonly ILabManagerDb _db;
    private readonly string _prefix;

    public OrderRepository(ILabManagerDb db, Microsoft.Extensions.Options.IOptions<LabManagerOptions> options)
    {
        _db = db;
        _prefix = options.Value.OrderNumberPrefix;
    }

    // Column list shared by the order queries (FROM/JOIN included)
    private const string OrderColumns = @"
               o.OrderID       AS OrderId,
               o.OrderNumber,
               o.SupplierID    AS SupplierId,
               s.Name          AS SupplierName,
               s.Email         AS SupplierEmail,
               s.ContactPerson AS SupplierContact,
               ISNULL(o.Status, N'Ordered') AS Status,
               o.CreatedAt     AS DateOrdered,
               o.DateComplete,
               o.DateCancelled,
               o.CancelReason
          FROM dbo.[Order]  o
          JOIN dbo.Supplier s ON s.SupplierID = o.SupplierID";

    private const string SelectItem = @"
        SELECT oi.OrderItemID  AS OrderItemId,
               oi.OrderID      AS OrderId,
               oi.ConsumableID AS ConsumableId,
               c.Name          AS ConsumableName,
               oi.QtyOrdered,
               ISNULL(oi.Status, N'Ordered') AS Status,
               oi.dateReceived AS DateReceived,
               oi.DateCancelled,
               oi.CancelReason
          FROM dbo.OrderItem  oi
          JOIN dbo.Consumable c ON c.ConsumableID = oi.ConsumableID";

    // AC-05-09: order number, status, supplier and items with their own statuses.
    // Orders and items come back in one round trip and are matched up in memory.
    public async Task<List<OrderRow>> GetAllAsync(string? status = null, int? top = null)
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync($@"
            SELECT TOP (@take) {OrderColumns}
             WHERE (@status IS NULL OR ISNULL(o.Status, N'Ordered') = @status)
             ORDER BY o.CreatedAt DESC, o.OrderID DESC;

            {SelectItem}
             WHERE oi.OrderID IN (
                    SELECT TOP (@take) o.OrderID
                      FROM dbo.[Order] o
                     WHERE (@status IS NULL OR ISNULL(o.Status, N'Ordered') = @status)
                     ORDER BY o.CreatedAt DESC, o.OrderID DESC)
             ORDER BY c.Name;",
            new { status, take = top ?? int.MaxValue });

        var orders = (await multi.ReadAsync<OrderRow>()).ToList();
        var items = (await multi.ReadAsync<OrderItemRow>()).ToLookup(i => i.OrderId);

        foreach (var order in orders)
            order.Items = items[order.OrderId].ToList();

        return orders;
    }

    public async Task<OrderRow?> GetByIdAsync(int orderId)
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync($@"
            SELECT {OrderColumns}
             WHERE o.OrderID = @orderId;

            {SelectItem}
             WHERE oi.OrderID = @orderId
             ORDER BY c.Name;", new { orderId });

        var order = await multi.ReadFirstOrDefaultAsync<OrderRow>();
        if (order is null) return null;

        order.Items = (await multi.ReadAsync<OrderItemRow>()).ToList();
        return order;
    }

    public async Task<int?> GetOrderIdForItemAsync(int orderItemId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT OrderID FROM dbo.OrderItem WHERE OrderItemID = @orderItemId;", new { orderItemId });
    }

    /// <summary>Shown on the Create Order page. The real number is only taken when the order is saved.</summary>
    public async Task<string> PeekNextOrderNumberAsync()
    {
        int year = DateTime.Today.Year;
        string prefix = OrderRules.NumberPrefix(_prefix, year);

        using var conn = _db.CreateConnection();
        int last = await conn.ExecuteScalarAsync<int>(LastSequenceSql, new { prefix });
        return OrderRules.FormatNumber(_prefix, year, last + 1);
    }

    // Highest sequence used this year, e.g. ORD-2026-007 -> 7.
    // TRY_CAST ignores any number that doesn't follow the pattern.
    private const string LastSequenceSql = @"
        SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(OrderNumber, LEN(@prefix) + 1, 20) AS INT)), 0)
          FROM dbo.[Order]
         WHERE OrderNumber LIKE @prefix + '%';";

    /// <summary>
    /// AC-05-02: one order per supplier, all created in ONE transaction, each with a unique
    /// number. The number is worked out inside the transaction while holding a range lock
    /// (UPDLOCK, HOLDLOCK), so two orders placed at the same moment can't get the same number.
    /// </summary>
    public async Task<List<int>> CreateOrdersAsync(IEnumerable<NewOrder> orders, int labManagerId)
    {
        var toCreate = orders.Where(o => o.Lines.Count > 0).ToList();
        var ids = new List<int>();
        if (toCreate.Count == 0) return ids;

        int year = DateTime.Today.Year;
        string prefix = OrderRules.NumberPrefix(_prefix, year);

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int lastSeq = await conn.ExecuteScalarAsync<int>(@"
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(OrderNumber, LEN(@prefix) + 1, 20) AS INT)), 0)
              FROM dbo.[Order] WITH (UPDLOCK, HOLDLOCK)
             WHERE OrderNumber LIKE @prefix + '%';", new { prefix }, tx);

        foreach (var order in toCreate)
        {
            string orderNumber = OrderRules.FormatNumber(_prefix, year, ++lastSeq);

            int orderId = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO dbo.[Order] (OrderNumber, Status, LabManagerID, SupplierID)
                OUTPUT INSERTED.OrderID
                VALUES (@orderNumber, @status, @labManagerId, @supplierId);",
                new { orderNumber, status = OrderStatus.Ordered, labManagerId, supplierId = order.SupplierId }, tx);

            // Dapper runs this INSERT once per line
            await conn.ExecuteAsync(@"
                INSERT INTO dbo.OrderItem (OrderID, ConsumableID, QtyOrdered, Status)
                VALUES (@orderId, @consumableId, @quantity, @status);",
                order.Lines.Select(l => new
                {
                    orderId,
                    consumableId = l.ConsumableId,
                    quantity = l.Quantity,
                    status = OrderItemStatus.Ordered
                }).ToList(), tx);

            ids.Add(orderId);
        }

        tx.Commit();
        return ids;
    }

    private sealed class ReceivedRow
    {
        public int OrderID { get; set; }
        public int ConsumableID { get; set; }
        public int QtyOrdered { get; set; }
    }

    /// <summary>
    /// AC-05-06 / TC-LM-05-06: mark the item received (date captured), add the quantity
    /// ordered to the consumable's stock, then recalculate the order status (AC-05-04/05).
    /// The UPDATE only matches an item that is still 'Ordered', so a double-click or a
    /// page refresh can never add the same stock twice.
    /// </summary>
    public async Task<OrderActionResult> ReceiveItemAsync(int orderItemId)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var item = await conn.QueryFirstOrDefaultAsync<ReceivedRow>(@"
            UPDATE dbo.OrderItem
               SET Status = @received, dateReceived = SYSDATETIME()
            OUTPUT INSERTED.OrderID, INSERTED.ConsumableID, INSERTED.QtyOrdered
             WHERE OrderItemID = @orderItemId
               AND ISNULL(Status, N'Ordered') = @ordered;",
            new { orderItemId, received = OrderItemStatus.Received, ordered = OrderItemStatus.Ordered }, tx);

        if (item is null)
            return await ItemExistsAsync(conn, tx, orderItemId) ? OrderActionResult.NotOutstanding : OrderActionResult.NotFound;

        await conn.ExecuteAsync(
            "UPDATE dbo.Consumable SET QtyOnHand = QtyOnHand + @qty WHERE ConsumableID = @consumableId;",
            new { qty = item.QtyOrdered, consumableId = item.ConsumableID }, tx);

        await RecalculateOrderStatusAsync(conn, tx, item.OrderID, null);
        tx.Commit();
        return OrderActionResult.Success;
    }

    /// <summary>AC-05-08 / TC-LM-05-05: cancel one outstanding item with a reason. Other items are untouched.</summary>
    public async Task<OrderActionResult> CancelItemAsync(int orderItemId, string reason)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int? orderId = await conn.QueryFirstOrDefaultAsync<int?>(@"
            UPDATE dbo.OrderItem
               SET Status = @cancelled, DateCancelled = SYSDATETIME(), CancelReason = @reason
            OUTPUT INSERTED.OrderID
             WHERE OrderItemID = @orderItemId
               AND ISNULL(Status, N'Ordered') = @ordered;",
            new { orderItemId, reason, cancelled = OrderItemStatus.Cancelled, ordered = OrderItemStatus.Ordered }, tx);

        if (orderId is null)
            return await ItemExistsAsync(conn, tx, orderItemId) ? OrderActionResult.NotOutstanding : OrderActionResult.NotFound;

        await RecalculateOrderStatusAsync(conn, tx, orderId.Value, reason);
        tx.Commit();
        return OrderActionResult.Success;
    }

    /// <summary>
    /// AC-05-07 / TC-LM-05-04: cancel the order. Every item still outstanding is cancelled with
    /// the same reason, and the order gets status 'Cancelled', today's date and the reason.
    /// Items that were already received stay received (that stock really arrived).
    /// </summary>
    public async Task<OrderActionResult> CancelOrderAsync(int orderId, string reason)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        string? status = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT ISNULL(Status, N'Ordered') FROM dbo.[Order] WITH (UPDLOCK) WHERE OrderID = @orderId;", new { orderId }, tx);

        if (status is null) return OrderActionResult.NotFound;
        if (status is OrderStatus.Complete or OrderStatus.Cancelled) return OrderActionResult.NotOutstanding;

        await conn.ExecuteAsync(@"
            UPDATE dbo.OrderItem
               SET Status = @cancelled, DateCancelled = SYSDATETIME(), CancelReason = @reason
             WHERE OrderID = @orderId AND ISNULL(Status, N'Ordered') = @ordered;",
            new { orderId, reason, cancelled = OrderItemStatus.Cancelled, ordered = OrderItemStatus.Ordered }, tx);

        await conn.ExecuteAsync(@"
            UPDATE dbo.[Order]
               SET Status = @cancelled, DateCancelled = SYSDATETIME(), CancelReason = @reason
             WHERE OrderID = @orderId;",
            new { orderId, reason, cancelled = OrderStatus.Cancelled }, tx);

        tx.Commit();
        return OrderActionResult.Success;
    }

    public async Task<int> CountOpenAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.[Order] WHERE ISNULL(Status, N'Ordered') IN (@ordered, @partial);",
            new { ordered = OrderStatus.Ordered, partial = OrderStatus.PartiallyComplete });
    }

    // ---------------------------------------------------------------------------------

    private static Task<bool> ItemExistsAsync(IDbConnection conn, IDbTransaction tx, int orderItemId) =>
        conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.OrderItem WHERE OrderItemID = @orderItemId)
                        THEN 1 ELSE 0 END;", new { orderItemId }, tx);

    /// <summary>
    /// AC-05-04 / AC-05-05: works out the order status from its items (OrderRules) and stamps
    /// the completion date when it becomes Complete. Runs inside the caller's transaction.
    /// </summary>
    private static async Task RecalculateOrderStatusAsync(IDbConnection conn, IDbTransaction tx, int orderId, string? reason)
    {
        var statuses = (await conn.QueryAsync<string>(
            "SELECT ISNULL(Status, N'Ordered') FROM dbo.OrderItem WHERE OrderID = @orderId;", new { orderId }, tx)).ToList();

        string newStatus = OrderRules.CalculateOrderStatus(statuses);

        await conn.ExecuteAsync(@"
            UPDATE dbo.[Order]
               SET Status        = @newStatus,
                   DateComplete  = CASE WHEN @newStatus = @complete
                                        THEN COALESCE(DateComplete, SYSDATETIME()) ELSE NULL END,
                   DateCancelled = CASE WHEN @newStatus = @cancelled
                                        THEN COALESCE(DateCancelled, SYSDATETIME()) ELSE NULL END,
                   CancelReason  = CASE WHEN @newStatus = @cancelled
                                        THEN COALESCE(CancelReason, @reason, N'All items cancelled') ELSE NULL END
             WHERE OrderID = @orderId;",
            new { orderId, newStatus, reason, complete = OrderStatus.Complete, cancelled = OrderStatus.Cancelled }, tx);
    }
}
