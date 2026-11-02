
namespace WebApplication2.Models
{
    public class CaptureResultsViewModel
    {
        // Request, patient and sample identification
        public int SampleReceiptID { get; set; }
        public int TestRequestID { get; set; }
        public int TestTypeID { get; set; }

        public string RequestNumber { get; set; } = string.Empty;
        public string RegistrationNumber { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public int PatientID { get; set; }
        public int PatientAge { get; set; }

        public string Barcode { get; set; } = string.Empty;

        // Selected individual test
        public string TestName { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;

        public decimal? NormalRangeMin { get; set; }
        public decimal? NormalRangeMax { get; set; }

        // Captured result
        public string ResultValue { get; set; } = string.Empty;
        public string TechnicianNotes { get; set; } = string.Empty;

        // Result evaluation
        public bool? IsAbnormal { get; set; }
        public string Status { get; set; } = "Awaiting Result";

        public bool IsCompleted { get; set; }
        public DateTime? CompletionDateTime { get; set; }

        public List<string> MedicalConditions { get; set; } = new();
        public List<string> Allergies { get; set; } = new();
    }
}