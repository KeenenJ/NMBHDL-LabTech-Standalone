using NMBDHL.Doctor.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NMBDHL.Doctor.Repository
{
    public interface IDoctorRepository
    {
        Task<IEnumerable<AbnormalAlertModel>> GetAbnormalAlertsAsync(int doctorId);
        Task<IEnumerable<RecentRequestModel>> GetRecentRequestsAsync(int doctorId);
        Task<IEnumerable<PatientRecordModel>> GetManagePatientsAsync(int doctorId);
        Task<IEnumerable<PatientDropdownModel>> GetPatientsDropdownAsync();
        Task<IEnumerable<string>> GetTestPanelsAsync();
        Task<IEnumerable<TrackProgressModel>> GetTrackProgressAsync(int doctorId);



        Task<bool> CancelTestRequestAsync(int requestId, string reason);
        Task<DashboardMetricsModel> GetDashboardMetricsAsync(int doctorId);
        Task<bool> CreateTestRequestAsync(int patientId, int doctorId, string panels, string urgency, string notes, string barcode);
    }
}