#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Options;
using NMBHLabSys.LabManagerSubsystem.Models;
using NMBHLabSys.LabManagerSubsystem.Rules;

namespace NMBHLabSys.LabManagerSubsystem.Data.Repositories;

/// <summary>
/// A row of the shared [User] table. grp-04-14 has no Role column: the role is worked out from
/// which table the user appears in (LabManager, Doctor, LabTechnician, Admin, Patient).
/// </summary>
public class UserAccount
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool MustChangePassword { get; set; }   // [User].IsFirstLogin
    public bool IsActive { get; set; }
    public int? LabManagerId { get; set; }        // set only for laboratory managers

    public string FullName => $"{Name} {Surname}".Trim();
}

// =====================================================================
// US-LM-09  [User], LabManager, PasswordResetToken
// =====================================================================
public interface IAccountRepository
{
    Task<UserAccount?> GetUserByEmailAsync(string email);
    Task<UserAccount?> GetUserByIdAsync(int userId);

    /// <summary>Adds the dbo.LabManager row for this user if it isn't there yet; returns its LabManagerID.</summary>
    Task<int> EnsureLabManagerRecordAsync(int userId);
    Task<bool> EmailInUseAsync(string email, int excludeUserId = 0);
    Task SetPasswordAsync(int userId, string passwordHash, bool mustChangePassword);
    Task<ProfileModel?> GetManagerProfileAsync(int userId);
    Task UpdateManagerProfileAsync(int userId, EditProfileModel form);
    Task CreateResetTokenAsync(int userId, string tokenHash, int lifetimeMinutes);
    Task<int?> GetUserIdForValidResetTokenAsync(string tokenHash);
    Task<bool> ResetPasswordWithTokenAsync(string tokenHash, string newPasswordHash);
}

public class AccountRepository : IAccountRepository
{
    private readonly ILabManagerDb _db;
    private readonly LabManagerAuthOptions _auth;

    public AccountRepository(ILabManagerDb db, IOptions<LabManagerOptions> options)
    {
        _db = db;
        _auth = options.Value.Auth;
    }

    private const string SelectUser = @"
        SELECT u.UserID                        AS UserId,
               u.Name,
               u.Surname,
               u.Email,
               ISNULL(u.PasswaordHash, N'')    AS PasswordHash,
               CASE WHEN EXISTS (SELECT 1 FROM dbo.LabManager    x WHERE x.UserID = u.UserID) THEN @managerRole
                    WHEN EXISTS (SELECT 1 FROM dbo.Doctor        x WHERE x.UserID = u.UserID) THEN @doctorRole
                    WHEN EXISTS (SELECT 1 FROM dbo.LabTechnician x WHERE x.UserID = u.UserID) THEN @technicianRole
                    WHEN EXISTS (SELECT 1 FROM dbo.Admin         x WHERE x.UserID = u.UserID) THEN N'Admin'
                    WHEN EXISTS (SELECT 1 FROM dbo.Patient       x WHERE x.UserID = u.UserID) THEN N'Patient'
                    ELSE N'' END                AS Role,
               u.IsFirstLogin                  AS MustChangePassword,
               u.IsActive,
               (SELECT TOP (1) m.LabManagerID FROM dbo.LabManager m WHERE m.UserID = u.UserID) AS LabManagerId
          FROM dbo.[User] u";

    public async Task<UserAccount?> GetUserByEmailAsync(string email)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<UserAccount>(SelectUser + " WHERE u.Email = @email;",
            new { email = email.Trim(), managerRole = _auth.ManagerRole, doctorRole = _auth.DoctorRole, technicianRole = _auth.TechnicianRole });
    }

    public async Task<UserAccount?> GetUserByIdAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<UserAccount>(SelectUser + " WHERE u.UserID = @userId;",
            new { userId, managerRole = _auth.ManagerRole, doctorRole = _auth.DoctorRole, technicianRole = _auth.TechnicianRole });
    }

    public async Task<int> EnsureLabManagerRecordAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        // UQ_LabManager_UserID stops a duplicate if two requests arrive at once
        return await conn.ExecuteScalarAsync<int>(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.LabManager WHERE UserID = @userId)
                INSERT INTO dbo.LabManager (UserID) VALUES (@userId);
            SELECT LabManagerID FROM dbo.LabManager WHERE UserID = @userId;", new { userId });
    }

    // AC-06-05: checked against EVERY user (patients, doctors, technicians, managers); UQ_User_Email backs it up
    public async Task<bool> EmailInUseAsync(string email, int excludeUserId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.[User] WHERE Email = @email AND UserID <> @excludeUserId
            ) THEN 1 ELSE 0 END;", new { email = email.Trim(), excludeUserId });
    }

    /// <summary>Change Password (mustChange = false) or a manager's reset of a staff password (mustChange = true).</summary>
    public async Task SetPasswordAsync(int userId, string passwordHash, bool mustChangePassword)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.[User]
               SET PasswaordHash = @passwordHash, IsFirstLogin = @mustChangePassword
             WHERE UserID = @userId;", new { userId, passwordHash, mustChangePassword });
    }

    // Profile page. grp-04-14's LabManager table only has LabManagerID + UserID; the rest is on [User].
    public async Task<ProfileModel?> GetManagerProfileAsync(int userId)
    {
        using var conn = _db.CreateConnection();
        var profile = await conn.QueryFirstOrDefaultAsync<ProfileModel>(@"
            SELECT u.UserID                        AS UserId,
                   m.LabManagerID                  AS LabManagerId,
                   u.Name,
                   u.Surname,
                   u.Email,
                   ISNULL(u.ContactNumber, N'')    AS ContactNumber,
                   u.IsFirstLogin                  AS MustChangePassword
              FROM dbo.[User] u
              LEFT JOIN dbo.LabManager m ON m.UserID = u.UserID
             WHERE u.UserID = @userId;", new { userId });

        if (profile is not null)
        {
            profile.EmployeeNumber = profile.LabManagerId is int id ? $"LM-{id:D3}" : "";
            profile.Department = "Laboratory";
        }
        return profile;
    }

    public async Task UpdateManagerProfileAsync(int userId, EditProfileModel form)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.[User]
               SET Name = @name, Surname = @surname, Email = @email, ContactNumber = @contact
             WHERE UserID = @userId;",
            new { userId, name = form.Name, surname = form.Surname, email = form.Email, contact = form.ContactNumber });
    }

    /// <summary>AC-09-03: stores only the HASH of a new token; older unused links for the user stop working.</summary>
    public async Task CreateResetTokenAsync(int userId, string tokenHash, int lifetimeMinutes)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE dbo.PasswordResetToken
               SET UsedAt = SYSDATETIME()
             WHERE UserID = @userId AND UsedAt IS NULL;

            INSERT INTO dbo.PasswordResetToken (UserID, TokenHash, ExpiresAt)
            VALUES (@userId, @tokenHash, DATEADD(MINUTE, @lifetimeMinutes, SYSDATETIME()));",
            new { userId, tokenHash, lifetimeMinutes });
    }

    public async Task<int?> GetUserIdForValidResetTokenAsync(string tokenHash)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<int?>(@"
            SELECT UserID
              FROM dbo.PasswordResetToken
             WHERE TokenHash = @tokenHash
               AND UsedAt IS NULL
               AND ExpiresAt > SYSDATETIME();", new { tokenHash });
    }

    /// <summary>Uses up the token and sets the new password in one transaction. False = link invalid / expired / used.</summary>
    public async Task<bool> ResetPasswordWithTokenAsync(string tokenHash, string newPasswordHash)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int? userId = await conn.QueryFirstOrDefaultAsync<int?>(@"
            UPDATE dbo.PasswordResetToken
               SET UsedAt = SYSDATETIME()
            OUTPUT INSERTED.UserID
             WHERE TokenHash = @tokenHash
               AND UsedAt IS NULL
               AND ExpiresAt > SYSDATETIME();", new { tokenHash }, tx);

        if (userId is null) return false;

        await conn.ExecuteAsync(@"
            UPDATE dbo.[User] SET PasswaordHash = @newPasswordHash, IsFirstLogin = 0 WHERE UserID = @userId;",
            new { userId, newPasswordHash }, tx);

        tx.Commit();
        return true;
    }
}

// =====================================================================
// US-LM-08  Management report
// =====================================================================
public interface IReportRepository
{
    Task<List<ReportRow>> GetCompletedTestsByCategoryAsync(DateTime startDate, DateTime endDate);
}

/// <summary>
/// In grp-04-14 a performed test is a TestResult row (TestTypeID, Status, CompletionDateTime).
/// A result counts when its CompletionDateTime falls in the range. If the Lab Technician subsystem
/// also sets a status for finished results (e.g. 'Completed', 'Verified'), list those in
/// LabManager:Report:CountedResultStatuses and only those statuses are counted.
/// Test types without a category are grouped as "Uncategorised".
/// </summary>
public class ReportRepository : IReportRepository
{
    private readonly ILabManagerDb _db;
    private readonly string[] _statuses;

    public ReportRepository(ILabManagerDb db, IOptions<LabManagerOptions> options)
    {
        _db = db;
        _statuses = (options.Value.Report.CountedResultStatuses ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
    }

    /// <summary>Both dates are INCLUDED: from the start of startDate up to (not including) the day after endDate.</summary>
    public async Task<List<ReportRow>> GetCompletedTestsByCategoryAsync(DateTime startDate, DateTime endDate)
    {
        DateTime from = startDate.Date;
        DateTime toExclusive = endDate.Date.AddDays(1);

        string statusFilter = _statuses.Length > 0 ? " AND tr.Status IN @statuses" : "";

        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<ReportRow>($@"
            SELECT ISNULL(c.Category, N'Uncategorised') AS CategoryName,
                   COUNT(*)                            AS TestCount
              FROM dbo.TestResult tr
              JOIN dbo.TestType   t ON t.TestTypeID = tr.TestTypeID
              LEFT JOIN dbo.TestCategory c ON c.TestCategoryID = t.TestCategoryID
             WHERE tr.CompletionDateTime >= @from
               AND tr.CompletionDateTime <  @toExclusive{statusFilter}
             GROUP BY ISNULL(c.Category, N'Uncategorised')
             ORDER BY TestCount DESC, CategoryName;",
            new { from, toExclusive, statuses = _statuses })).ToList();

        int total = rows.Sum(r => r.TestCount);
        foreach (var row in rows)
            row.SharePercent = total == 0 ? 0 : Math.Round(row.TestCount * 100m / total, 1);

        return rows;
    }
}

// =====================================================================
// Dashboard counts
// =====================================================================
public interface IDashboardRepository
{
    Task<DashboardModel> GetCountsAsync();
}

public class DashboardRepository : IDashboardRepository
{
    private readonly ILabManagerDb _db;
    public DashboardRepository(ILabManagerDb db) { _db = db; }

    public async Task<DashboardModel> GetCountsAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleAsync<DashboardModel>(@"
            SELECT
                (SELECT COUNT(*) FROM dbo.Consumable WHERE QtyOnHand <= ISNULL(ReOrderLevel, 0)) AS BelowReorderCount,
                (SELECT COUNT(*) FROM dbo.Consumable
                  WHERE QtyOnHand > ISNULL(ReOrderLevel, 0)
                    AND CAST(QtyOnHand AS BIGINT) * 10 <= CAST(ISNULL(ReOrderLevel, 0) AS BIGINT) * 11) AS NearReorderCount,
                (SELECT COUNT(*) FROM dbo.[Order] WHERE ISNULL(Status, N'Ordered') IN (@ordered, @partial)) AS OpenOrderCount,
                (SELECT COUNT(*) FROM dbo.LabTechnician lt JOIN dbo.[User] u ON u.UserID = lt.UserID WHERE u.IsActive = 1) AS TechnicianCount,
                (SELECT COUNT(*) FROM dbo.Doctor d         JOIN dbo.[User] u ON u.UserID = d.UserID  WHERE u.IsActive = 1) AS DoctorCount,
                (SELECT COUNT(*) FROM dbo.[User] u
                  WHERE u.IsFirstLogin = 1 AND u.IsActive = 1
                    AND (EXISTS (SELECT 1 FROM dbo.Doctor d         WHERE d.UserID = u.UserID)
                      OR EXISTS (SELECT 1 FROM dbo.LabTechnician lt WHERE lt.UserID = u.UserID))) AS PendingPasswordChangeCount,
                (SELECT COUNT(*) FROM dbo.TestCategory) AS CategoryCount,
                (SELECT COUNT(*) FROM dbo.TestType)     AS TestTypeCount,
                (SELECT COUNT(*) FROM dbo.SampleType)   AS SampleTypeCount,
                (SELECT COUNT(*) FROM dbo.Supplier)     AS SupplierCount;",
            new { ordered = OrderStatus.Ordered, partial = OrderStatus.PartiallyComplete });
    }
}
