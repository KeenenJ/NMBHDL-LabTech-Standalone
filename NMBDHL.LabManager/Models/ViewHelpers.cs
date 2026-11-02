#nullable enable
using System;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Models;

/// <summary>
/// Small display helpers the Lab Manager views share, so every page colours
/// the same status the same way (the badge-* classes are in each view's style block).
/// </summary>
public static class LmBadge
{
    /// <summary>[Order].Status -> badge class.</summary>
    public static string ForOrder(string? status) => status switch
    {
        OrderStatus.PartiallyComplete => "badge-near",
        OrderStatus.Complete => "badge-ok",
        OrderStatus.Cancelled => "badge-neutral",
        _ => "badge-ordered"
    };

    /// <summary>OrderItem.Status -> badge class.</summary>
    public static string ForItem(string? status) => status switch
    {
        OrderItemStatus.Received => "badge-ok",
        OrderItemStatus.Cancelled => "badge-neutral",
        _ => "badge-ordered"
    };

    /// <summary>Stock level of a consumable -> badge class.</summary>
    public static string ForStock(StockLevel level) => level switch
    {
        StockLevel.Below => "badge-low",
        StockLevel.Near => "badge-near",
        _ => "badge-ok"
    };

    /// <summary>Doctor / technician account -> badge class.</summary>
    public static string ForAccount(bool mustChangePassword) => mustChangePassword ? "badge-near" : "badge-ok";
}

/// <summary>Date formats used on the Lab Manager pages.</summary>
public static class LmDate
{
    public static string Day(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "";
    public static string DayTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "";
}
