using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NMBDHL.DataAccess.SQLDataBaseAccess;
using NMBDHL.Doctor.Model;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace NMBDHL.Doctor.Repository
{
    public class DoctorRepository : IDoctorRepository
    {
        private readonly ISQLDataBaseAccess _db;
        private readonly string _connectionString;

        // IConfiguration is added here to read from appsettings.json
        public DoctorRepository(ISQLDataBaseAccess db, IConfiguration config)
        {
            _db = db;
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<AbnormalAlertModel>> GetAbnormalAlertsAsync(int doctorId)
        {
            return await _db.GetData<AbnormalAlertModel, dynamic>(
                "sp_Doctor_GetAbnormalAlerts",
                new { DoctorID = doctorId });
        }

        public async Task<IEnumerable<RecentRequestModel>> GetRecentRequestsAsync(int doctorId)
        {
            return await _db.GetData<RecentRequestModel, dynamic>(
                "sp_Doctor_GetRecentRequests",
                new { DoctorID = doctorId });
        }

        public async Task<IEnumerable<PatientRecordModel>> GetManagePatientsAsync(int doctorId)
        {
            return await _db.GetData<PatientRecordModel, dynamic>(
                "sp_Doctor_GetManagePatients",
                new { DoctorID = doctorId });
        }

        public async Task<IEnumerable<PatientDropdownModel>> GetPatientsDropdownAsync()
        {
            return await _db.GetData<PatientDropdownModel, dynamic>(
                "sp_Doctor_GetPatientsDropdown",
                new { });
        }

        public async Task<IEnumerable<string>> GetTestPanelsAsync()
        {
            return await _db.GetData<string, dynamic>(
                "sp_Doctor_GetTestPanels",
                new { });
        }

        public async Task<IEnumerable<TrackProgressModel>> GetTrackProgressAsync(int doctorId)
        {
            return await _db.GetData<TrackProgressModel, dynamic>(
                "sp_Doctor_GetTrackProgress",
                new { DoctorID = doctorId });
        }

        public async Task<bool> CreateTestRequestAsync(int patientId, int doctorId, string panels, string urgency, string notes, string barcode)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                var parameters = new
                {
                    PatientID = patientId,
                    DoctorID = doctorId,
                    Type = panels,
                    Urgency = urgency,
                    ClinicalNotes = notes,
                    barcode = barcode,
                    Status = "Submitted"
                };

                // Execute the query without relying on the row count
                await connection.ExecuteAsync(
                    "dbo.sp_Doctor_CreateTestRequest",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                // If ExecuteAsync doesn't throw a SQL exception, the insert was successful
                return true;
            }
        }


        public async Task<DashboardMetricsModel> GetDashboardMetricsAsync(int doctorId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                string sql = @"
            SELECT 
                -- Active Requests: Anything not Completed, Released, or Cancelled
                (SELECT COUNT(*) FROM dbo.TestRequest 
                 WHERE DoctorID = @DoctorID AND Status IN ('Submitted', 'Sample(s) received', 'In progress')) AS TotalActiveRequests,
                
                -- Pending Collections: Doctor submitted, but lab hasn't received samples yet
                (SELECT COUNT(*) FROM dbo.TestRequest 
                 WHERE DoctorID = @DoctorID AND Status = 'Submitted') AS PendingCollections,
                
                -- We will dynamically count abnormal results in the last 5 days
                4 AS CriticalAlertsCount, 
                
                -- Placeholder until Turnaround Time calculation is built
                '94.2%' AS TurnaroundCompliance
            ";

                return await connection.QueryFirstOrDefaultAsync<DashboardMetricsModel>(sql, new { DoctorID = doctorId });
            }
        }


        public async Task<bool> CancelTestRequestAsync(int requestId, string reason)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.ExecuteAsync(
                    "dbo.sp_Doctor_CancelRequest",
                    new { RequestId = requestId, Reason = reason },
                    commandType: CommandType.StoredProcedure);
                return true;
            }
        }
    }
}