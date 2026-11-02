#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NMBHLabSys.LabManagerSubsystem.Rules;

// Business rules as plain functions: no database, no web request.
// The controller and repositories call these, so a rule only ever lives in one place.

/// <summary>Exact status text used in the database and on screen (AC-05-04 / AC-05-05).</summary>
public static class OrderStatus
{
    public const string Ordered = "Ordered";
    public const string PartiallyComplete = "Partially Complete";
    public const string Complete = "Complete";
    public const string Cancelled = "Cancelled";

    /// <summary>The lower-case key the Orders page filter uses (data-status="ordered" etc.).</summary>
    public static string FilterKey(string status) => status switch
    {
        PartiallyComplete => "partial",
        Complete => "complete",
        Cancelled => "cancelled",
        _ => "ordered"
    };
}

public static class OrderItemStatus
{
    public const string Ordered = "Ordered";
    public const string Received = "Received";
    public const string Cancelled = "Cancelled";
}

public static class OrderRules
{
    /// <summary>
    /// Works out an order's status from its items.
    ///   every item cancelled                               -> Cancelled
    ///   nothing outstanding and at least one item received -> Complete            (AC-05-04)
    ///   some received, some still outstanding              -> Partially Complete  (AC-05-05)
    ///   nothing received yet                               -> Ordered
    /// </summary>
    public static string CalculateOrderStatus(IReadOnlyCollection<string> itemStatuses)
    {
        if (itemStatuses.Count == 0) return OrderStatus.Ordered;

        bool allCancelled = itemStatuses.All(s => s == OrderItemStatus.Cancelled);
        bool anyReceived = itemStatuses.Any(s => s == OrderItemStatus.Received);
        bool anyOutstanding = itemStatuses.Any(s => s == OrderItemStatus.Ordered);

        if (allCancelled) return OrderStatus.Cancelled;
        if (!anyOutstanding && anyReceived) return OrderStatus.Complete;
        if (anyReceived) return OrderStatus.PartiallyComplete;
        return OrderStatus.Ordered;
    }

    /// <summary>"ORD-2026-" for orders placed in 2026.</summary>
    public static string NumberPrefix(string prefix, int year) => $"{prefix}-{year}-";

    /// <summary>ORD-2026-001, ORD-2026-002 ... (keeps counting past 999).</summary>
    public static string FormatNumber(string prefix, int year, int sequence) =>
        $"{NumberPrefix(prefix, year)}{sequence:D3}";
}

/// <summary>Stock level of a consumable, as shown on the Consumables page filter.</summary>
public enum StockLevel
{
    Ok,
    Near,   // within 10% above the reorder level
    Below   // at or below the reorder level
}

public enum StockAdjustAction
{
    Increase,
    Decrease,
    Set
}

public readonly record struct StockAdjustResult(bool Success, int NewQuantity, string? Error)
{
    public static StockAdjustResult Ok(int quantity) => new(true, quantity, null);
    public static StockAdjustResult Fail(string error) => new(false, 0, error);
}

public static class StockRules
{
    public const string BelowZeroMessage = LabManagerMessages.StockBelowZero;

    /// <summary>
    /// AC-03-08 / AC-05-01: "at or within 10% above the reorder level" needs reordering.
    /// Written as Qty * 10 &lt;= Reorder * 11 so it is exact whole-number maths,
    /// and it is the SAME test the SQL in ConsumableRepository uses.
    /// Example (TC-LM-03-06): reorder 50, qty 52 -> 520 &lt;= 550 -> flagged.
    /// </summary>
    public static bool NeedsReorder(int quantityOnHand, int reorderLevel) =>
        (long)quantityOnHand * 10 <= (long)reorderLevel * 11;

    public static StockLevel GetLevel(int quantityOnHand, int reorderLevel)
    {
        if (quantityOnHand <= reorderLevel) return StockLevel.Below;
        if (NeedsReorder(quantityOnHand, reorderLevel)) return StockLevel.Near;
        return StockLevel.Ok;
    }

    /// <summary>The lower-case key the Consumables page filter uses (data-status="below" etc.).</summary>
    public static string FilterKey(StockLevel level) => level switch
    {
        StockLevel.Below => "below",
        StockLevel.Near => "near",
        _ => "ok"
    };

    public static string Label(StockLevel level) => level switch
    {
        StockLevel.Below => "Below Reorder",
        StockLevel.Near => "Near Reorder",
        _ => "OK"
    };

    /// <summary>Reads "Increase" / "Decrease" / "Set" from the form (any case).</summary>
    public static bool TryParseAction(string? value, out StockAdjustAction action) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out action) && Enum.IsDefined(action);

    /// <summary>AC-03-04 to AC-03-07.</summary>
    public static StockAdjustResult Apply(int currentQuantity, StockAdjustAction action, int amount)
    {
        switch (action)
        {
            case StockAdjustAction.Increase:
                if (amount <= 0) return StockAdjustResult.Fail("Enter a positive number to increase stock.");
                if ((long)currentQuantity + amount > int.MaxValue) return StockAdjustResult.Fail("Quantity is too large.");
                return StockAdjustResult.Ok(currentQuantity + amount);

            case StockAdjustAction.Decrease:
                if (amount <= 0) return StockAdjustResult.Fail("Enter a positive number to decrease stock.");
                if (currentQuantity - amount < 0) return StockAdjustResult.Fail(BelowZeroMessage);
                return StockAdjustResult.Ok(currentQuantity - amount);

            case StockAdjustAction.Set:
                if (amount < 0) return StockAdjustResult.Fail("Stock cannot be set to a negative number.");
                return StockAdjustResult.Ok(amount);

            default:
                return StockAdjustResult.Fail("Choose increase, decrease or set.");
        }
    }
}

public static class PasswordRules
{
    public const int MinLength = 8;

    /// <summary>
    /// AC-09-04: returns every rule the password breaks so the page can show the specific errors.
    /// TC-LM-09-04: "1234" -> "Password must be at least 8 characters".
    /// An empty list means the password is fine.
    /// </summary>
    public static List<string> Validate(string? password)
    {
        var errors = new List<string>();
        password ??= string.Empty;

        if (password.Length < MinLength)
            errors.Add($"Password must be at least {MinLength} characters");
        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain at least one uppercase letter");
        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain at least one number");
        if (password.All(char.IsLetterOrDigit))
            errors.Add("Password must contain at least one special character");

        return errors;
    }
}

public static partial class IdentityRules
{
    /// <summary>
    /// SA ID format: 13 digits, starts with a real date of birth (YYMMDD),
    /// citizenship digit (position 11) is 0, 1 or 2. Returns null when the format is fine.
    /// The Luhn check digit is tested separately (see PassesLuhn) so that the duplicate
    /// check can run first: TC-LM-07-03 uses 8501015009087, whose check digit is wrong,
    /// and still expects "ID number already registered".
    /// </summary>
    public static string? ValidateSaIdFormat(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "SA ID number is required";
        id = id.Trim();

        if (id.Length != 13 || !id.All(char.IsAsciiDigit))
            return "SA ID number must be exactly 13 digits";

        if (!DateTime.TryParseExact(id[..6], "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "SA ID number does not start with a valid date of birth (YYMMDD)";

        if (id[10] is not ('0' or '1' or '2'))
            return "SA ID number has an invalid citizenship digit";

        return null;
    }

    /// <summary>The last digit of a real SA ID number is a Luhn check digit.</summary>
    public static bool PassesLuhn(string digits)
    {
        int sum = 0;
        bool doubleIt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    /// <summary>
    /// HPCSA registration number: two letters for the register (MP = medical practitioner)
    /// and 6 or 7 digits, e.g. MP123456 or MP0123456. Stored in upper case.
    /// </summary>
    public static bool IsValidHpcsa(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HpcsaRegex().IsMatch(NormaliseHpcsa(value));

    public static string NormaliseHpcsa(string value) => value.Trim().Replace(" ", "").ToUpperInvariant();

    /// <summary>SA phone number: 0821234567 or +27821234567. Spaces and dashes are ignored.</summary>
    public static bool IsValidSaPhone(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SaPhoneRegex().IsMatch(NormalisePhone(value));

    /// <summary>"082 123 4567" -> "0821234567" (this is what gets saved).</summary>
    public static string NormalisePhone(string value) =>
        new string(value.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());

    /// <summary>Shown on the Technicians list: 850101******* (date of birth only).</summary>
    public static string MaskSaId(string? id) =>
        string.IsNullOrEmpty(id) || id.Length < 6 ? id ?? "" : id[..6] + new string('*', id.Length - 6);

    [GeneratedRegex(@"^[A-Z]{2}\d{6,7}$")]
    private static partial Regex HpcsaRegex();

    [GeneratedRegex(@"^(\+27|0)\d{9}$")]
    private static partial Regex SaPhoneRegex();
}
