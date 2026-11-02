namespace WebApplication2.Models;

public sealed class VerificationQueueItemViewModel
{
    public int TestResultID { get; set; }
    public int TestRequestID { get; set; }
    public int SampleReceiptID { get; set; }
    public int TestTypeID { get; set; }
    public int PatientID { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string DoctorEmail { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;
    public decimal? NormalRangeMin { get; set; }
    public decimal? NormalRangeMax { get; set; }
    public bool IsAbnormal { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public string Urgency { get; set; } = string.Empty;
    public DateTime? CompletionDateTime { get; set; }
    public string CapturedBy { get; set; } = "Unknown";
    public bool HasBeenReturnedForRecapture { get; set; }
    public string LatestVerificationDecision { get; set; } = string.Empty;
    public string LatestVerificationReason { get; set; } = string.Empty;
    public DateTime? LatestVerificationDateTime { get; set; }
    public List<VerificationHistoryItemViewModel> VerificationHistory { get; set; } = new();

    public List<string> MedicalConditions { get; set; } = new();

    public List<string> Allergies { get; set; } = new();
}

public sealed class VerificationHistoryItemViewModel
{
    public int ResultVerificationID { get; set; }
    public int RelatedTestResultID { get; set; }
    public string RelatedResultValue { get; set; } = string.Empty;
    public string TechnicianNotes { get; set; } = string.Empty;
    public DateTime? RelatedCompletionDateTime { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string TechnicianName { get; set; } = "Unknown";
    public DateTime? VerificationDateTime { get; set; }
}