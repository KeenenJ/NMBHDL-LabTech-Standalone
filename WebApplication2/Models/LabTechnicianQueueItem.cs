
namespace WebApplication2.Models;

public sealed class LabTechnicianQueueItem
{
    public int TestRequestID { get; set; }

    public string Barcode { get; set; } = "";

    public DateTime RequestDate { get; set; }

    public string Urgency { get; set; } = "";

    public string Status { get; set; } = "";

    public string Type { get; set; } = "";

    public int PatientID { get; set; }

    public string PatientName { get; set; } = "";

    public string PatientNumber { get; set; } = "";

    public DateTime? DateTimeReceived { get; set; }

    public int SampleReceiptID { get; set; }

    public bool IsRejected { get; set; }

    public string? RejectionReason { get; set; }

    // Awaiting Selection fields
    public int TestTypeID { get; set; }

    public string TestName { get; set; } = "";

    public int TurnAroundMinutes { get; set; }

    public DateTime? SelectedAt { get; set; }

    public bool IsSelected { get; set; }

    public bool IsStockAvailable { get; set; }

    public string StockMessage { get; set; } = "";

    public string Consumable { get; set; } = "";

    public List<ConsumableStockItem> Consumables { get; set; } = new();
}

public sealed class ConsumableStockItem
{
    public int ConsumableID { get; set; }

    public string Name { get; set; } = "";

    public int QtyOnHand { get; set; }

    public int ReOrderLevel { get; set; }

    public bool IsAvailable => QtyOnHand > 0;
}
