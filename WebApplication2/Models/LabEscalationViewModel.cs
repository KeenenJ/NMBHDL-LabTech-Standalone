namespace WebApplication2.Models;

public sealed class LabEscalationViewModel
{
    public int TestRequestID { get; set; }

    public int SampleReceiptID { get; set; }

    public int TestTypeID { get; set; }

    public string PatientName { get; set; } = string.Empty;

    public string TestName { get; set; } = string.Empty;

    public string Urgency { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string DoctorName { get; set; } = string.Empty;

    public DateTime? DateTimeReceived { get; set; }

    public int TurnAroundMinutes { get; set; }

    public int MinutesOverdue { get; set; }
}