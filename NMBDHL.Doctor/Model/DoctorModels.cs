using System;

namespace NMBDHL.Doctor.Model
{
    public class AbnormalAlertModel
    {
        public string Surname { get; set; }
        public string FirstName { get; set; }
        public string IdNumber { get; set; }
        public string TestName { get; set; }
        public string ResultValue { get; set; }
        public string Unit { get; set; }
        public string RefRange { get; set; }
        public string Category { get; set; }
        public string Date { get; set; }
        public string AlertLevel { get; set; }
    }

    public class RecentRequestModel
    {
        public string RequestNumber { get; set; }
        public string PatientName { get; set; }
        public string IdNumber { get; set; }
        public string Tests { get; set; }
        public string Urgency { get; set; }
        public string Status { get; set; }
        public string TimeElapsed { get; set; }
    }

    public class PatientRecordModel
    {
        public int PatientID { get; set; }
        public string Surname { get; set; }
        public string FirstName { get; set; }
        public string IdNumber { get; set; }
        public string Cellphone { get; set; }
        public string Email { get; set; }
        public string Conditions { get; set; }
        public string Allergies { get; set; }
        public string Medication { get; set; }
    }

    public class PatientDropdownModel
    {
        public int PatientID { get; set; }
        public string IdNumber { get; set; }
        public string FullName { get; set; }
    }

    public class TrackProgressModel
    {
        public int RequestId { get; set; }
        public string PatientName { get; set; }
        public string IdNumber { get; set; }
        public string Panels { get; set; }
        public string DateCreated { get; set; }
        public string Urgency { get; set; }
        public string Status { get; set; }
    }


    public class DashboardMetricsModel
    {
        public int TotalActiveRequests { get; set; }
        public int CriticalAlertsCount { get; set; }
        public int PendingCollections { get; set; }
        public string TurnaroundCompliance { get; set; }
    }
}