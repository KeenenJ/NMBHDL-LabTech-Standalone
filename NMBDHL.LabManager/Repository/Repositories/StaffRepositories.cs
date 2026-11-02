#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Data.Repositories;

// Tables (grp-04-14):
//   [User](UserID, Name, Surname, IDNumber, Email, ContactNumber, PasswaordHash, IsFirstLogin, IsActive)
//   Doctor(DoctorID, HPCSANumber, UserID)
//   LabTechnician(LabTechnicianID, EmployeeNumber, UserID)
//   LabTechnicianTestType(LabTechnicianID, TestTypeID)      <- 01_LabManagerAdditions.sql
//
// Name, surname, email, contact number and SA ID number live on [User]; the Doctor and
// LabTechnician rows only add the HPCSA number / employee number. So every create and
// update writes both tables in ONE transaction.
//
// IsFirstLogin = 1 means "still on the temporary password, must change it" (AC-06-03 / AC-07-05).
// IsActive = 0 hides an account that can't be deleted because other records point at it.

internal static class StaffDelete
{
    /// <summary>
    /// Deletes the role row and the [User] row. If other records still point at them (a doctor's
    /// test requests, a technician's result verifications), nothing is deleted and the user is
    /// deactivated instead (IsActive = 0), which also stops them logging in.
    /// </summary>
    public static async Task<DeleteResult> DeleteOrDeactivateAsync(DbConnection conn, int userId, string deleteRoleRowSql, object args)
    {
        await conn.OpenAsync();
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await conn.ExecuteAsync(deleteRoleRowSql, args, tx);
                await conn.ExecuteAsync("DELETE FROM dbo.[User] WHERE UserID = @userId;", new { userId }, tx);
                tx.Commit();
                return DeleteResult.Deleted;
            }
            catch (Exception ex) when (LabManagerSqlErrors.IsReferenceViolation(ex))
            {
                tx.Rollback();
            }
        }

        await conn.ExecuteAsync("UPDATE dbo.[User] SET IsActive = 0 WHERE UserID = @userId;", new { userId });
        return DeleteResult.Deactivated;
    }
}

// =====================================================================
// US-LM-06  Doctor
// =====================================================================
public interface IDoctorRepository
{
    Task<List<DoctorRow>> GetAllAsync();
    Task<DoctorRow?> GetByIdAsync(int doctorId);
    Task<bool> HpcsaExistsAsync(string hpcsaNumber, int excludeDoctorId = 0);
    Task<int> CreateAsync(DoctorFormModel form, string passwordHash);
    Task<bool> UpdateAsync(int doctorId, DoctorFormModel form);
    Task<DeleteResult> DeleteAsync(int doctorId);
}

public class DoctorRepository : IDoctorRepository
{
    private readonly ILabManagerDb _db;
    public DoctorRepository(ILabManagerDb db) { _db = db; }

    private const string SelectDoctor = @"
        SELECT d.DoctorID                     AS DoctorId,
               d.UserID                       AS UserId,
               u.Name,
               u.Surname,
               d.HPCSANumber,
               u.Email,
               ISNULL(u.ContactNumber, N'')   AS ContactNumber,
               u.IsFirstLogin                 AS MustChangePassword,
               u.IsActive
          FROM dbo.Doctor d
          JOIN dbo.[User] u ON u.UserID = d.UserID";

    // AC-06-01 (active accounts only)
    public async Task<List<DoctorRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<DoctorRow>(SelectDoctor + " WHERE u.IsActive = 1 ORDER BY u.Surname, u.Name;");
        return rows.ToList();
    }

    public async Task<DoctorRow?> GetByIdAsync(int doctorId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<DoctorRow>(SelectDoctor + " WHERE d.DoctorID = @doctorId;", new { doctorId });
    }

    // AC-06-04
    public async Task<bool> HpcsaExistsAsync(string hpcsaNumber, int excludeDoctorId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Doctor WHERE HPCSANumber = @hpcsaNumber AND DoctorID <> @excludeDoctorId
            ) THEN 1 ELSE 0 END;", new { hpcsaNumber, excludeDoctorId });
    }

    // AC-06-02: [User] (IsFirstLogin = 1 for AC-06-03) + Doctor, in one transaction
    public async Task<int> CreateAsync(DoctorFormModel form, string passwordHash)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int userId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.[User] (Name, Surname, Email, ContactNumber, PasswaordHash, IsFirstLogin, IsActive)
            OUTPUT INSERTED.UserID
            VALUES (@name, @surname, @email, @contact, @passwordHash, 1, 1);",
            new { name = form.Name, surname = form.Surname, email = form.Email, contact = form.ContactNumber, passwordHash }, tx);

        int doctorId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.Doctor (HPCSANumber, UserID)
            OUTPUT INSERTED.DoctorID
            VALUES (@hpcsa, @userId);", new { hpcsa = form.HPCSANumber, userId }, tx);

        tx.Commit();
        return doctorId;
    }

    // AC-06-06
    public async Task<bool> UpdateAsync(int doctorId, DoctorFormModel form)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int? userId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT UserID FROM dbo.Doctor WITH (UPDLOCK) WHERE DoctorID = @doctorId;", new { doctorId }, tx);
        if (userId is null) return false;

        await conn.ExecuteAsync("UPDATE dbo.Doctor SET HPCSANumber = @hpcsa WHERE DoctorID = @doctorId;",
            new { doctorId, hpcsa = form.HPCSANumber }, tx);

        await conn.ExecuteAsync(@"
            UPDATE dbo.[User]
               SET Name = @name, Surname = @surname, Email = @email, ContactNumber = @contact
             WHERE UserID = @userId;",
            new { userId, name = form.Name, surname = form.Surname, email = form.Email, contact = form.ContactNumber }, tx);

        tx.Commit();
        return true;
    }

    public async Task<DeleteResult> DeleteAsync(int doctorId)
    {
        using var conn = _db.CreateConnection();
        int? userId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT UserID FROM dbo.Doctor WHERE DoctorID = @doctorId;", new { doctorId });
        if (userId is null) return DeleteResult.NotFound;

        return await StaffDelete.DeleteOrDeactivateAsync(conn, userId.Value,
            "DELETE FROM dbo.Doctor WHERE DoctorID = @doctorId;", new { doctorId });
    }
}

// =====================================================================
// US-LM-07  LabTechnician (+ LabTechnicianTestType)
// =====================================================================
public interface ITechnicianRepository
{
    Task<List<TechnicianRow>> GetAllAsync();
    Task<TechnicianRow?> GetByIdAsync(int technicianId);
    Task<bool> SaIdExistsAsync(string saIdNumber, int excludeTechnicianId = 0);
    Task<string?> GetSaIdAsync(int technicianId);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, int excludeTechnicianId = 0);
    Task<int> CreateAsync(TechnicianFormModel form, string passwordHash);
    Task<bool> UpdateAsync(int technicianId, TechnicianFormModel form);
    Task<DeleteResult> DeleteAsync(int technicianId);
}

public class TechnicianRepository : ITechnicianRepository
{
    private readonly ILabManagerDb _db;
    public TechnicianRepository(ILabManagerDb db) { _db = db; }

    /// <summary>Also used by TestTypeRepository (aliases lt and u).</summary>
    internal const string SelectTechnician = @"
        SELECT lt.LabTechnicianID           AS TechnicianId,
               lt.UserID                    AS UserId,
               u.Name,
               u.Surname,
               ISNULL(u.IDNumber, N'')      AS SAIDNumber,
               lt.EmployeeNumber,
               u.Email,
               ISNULL(u.ContactNumber, N'') AS ContactNumber,
               u.IsFirstLogin               AS MustChangePassword,
               u.IsActive
          FROM dbo.LabTechnician lt
          JOIN dbo.[User] u ON u.UserID = lt.UserID";

    private const string SelectTestTypeLinks = @"
        SELECT ltt.LabTechnicianID AS TechnicianId, t.TestTypeID AS Id, t.TestName AS Name
          FROM dbo.LabTechnicianTestType ltt
          JOIN dbo.TestType t ON t.TestTypeID = ltt.TestTypeID";

    private sealed class TestTypeLink
    {
        public int TechnicianId { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    // AC-07-01: with their test types. Two queries in one round trip, matched in memory.
    public async Task<List<TechnicianRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            SelectTechnician + " WHERE u.IsActive = 1 ORDER BY u.Surname, u.Name; " + SelectTestTypeLinks + " ORDER BY t.TestName;");

        var technicians = (await multi.ReadAsync<TechnicianRow>()).ToList();
        var links = (await multi.ReadAsync<TestTypeLink>()).ToLookup(l => l.TechnicianId);
        foreach (var tech in technicians)
            tech.TestTypes = links[tech.TechnicianId].Select(l => new LookupItem { Id = l.Id, Name = l.Name }).ToList();
        return technicians;
    }

    public async Task<TechnicianRow?> GetByIdAsync(int technicianId)
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            SelectTechnician + " WHERE lt.LabTechnicianID = @technicianId; " +
            SelectTestTypeLinks + " WHERE ltt.LabTechnicianID = @technicianId ORDER BY t.TestName;",
            new { technicianId });

        var tech = await multi.ReadFirstOrDefaultAsync<TechnicianRow>();
        if (tech is null) return null;
        tech.TestTypes = (await multi.ReadAsync<TestTypeLink>()).Select(l => new LookupItem { Id = l.Id, Name = l.Name }).ToList();
        return tech;
    }

    // AC-07-04 (the SA ID number is [User].IDNumber)
    public async Task<bool> SaIdExistsAsync(string saIdNumber, int excludeTechnicianId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                  FROM dbo.LabTechnician lt
                  JOIN dbo.[User] u ON u.UserID = lt.UserID
                 WHERE u.IDNumber = @saIdNumber AND lt.LabTechnicianID <> @excludeTechnicianId
            ) THEN 1 ELSE 0 END;", new { saIdNumber, excludeTechnicianId });
    }

    public async Task<string?> GetSaIdAsync(int technicianId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string?>(@"
            SELECT u.IDNumber
              FROM dbo.LabTechnician lt
              JOIN dbo.[User] u ON u.UserID = lt.UserID
             WHERE lt.LabTechnicianID = @technicianId;", new { technicianId });
    }

    public async Task<bool> EmployeeNumberExistsAsync(string employeeNumber, int excludeTechnicianId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.LabTechnician WHERE EmployeeNumber = @employeeNumber AND LabTechnicianID <> @excludeTechnicianId
            ) THEN 1 ELSE 0 END;", new { employeeNumber, excludeTechnicianId });
    }

    // AC-07-02: [User] (IsFirstLogin = 1 for AC-07-05) + LabTechnician + test types, in one transaction
    public async Task<int> CreateAsync(TechnicianFormModel form, string passwordHash)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int userId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.[User] (Name, Surname, IDNumber, Email, ContactNumber, PasswaordHash, IsFirstLogin, IsActive)
            OUTPUT INSERTED.UserID
            VALUES (@name, @surname, @saId, @email, @contact, @passwordHash, 1, 1);",
            new
            {
                name = form.Name, surname = form.Surname, saId = form.SAIDNumber,
                email = form.Email, contact = form.ContactNumber, passwordHash
            }, tx);

        int technicianId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.LabTechnician (EmployeeNumber, UserID)
            OUTPUT INSERTED.LabTechnicianID
            VALUES (@employeeNumber, @userId);", new { employeeNumber = form.EmployeeNumber, userId }, tx);

        await InsertTestTypeLinksAsync(conn, tx, technicianId, form.TestTypes);
        tx.Commit();
        return technicianId;
    }

    // AC-07-06 / AC-07-08
    public async Task<bool> UpdateAsync(int technicianId, TechnicianFormModel form)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int? userId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT UserID FROM dbo.LabTechnician WITH (UPDLOCK) WHERE LabTechnicianID = @technicianId;",
            new { technicianId }, tx);
        if (userId is null) return false;

        await conn.ExecuteAsync("UPDATE dbo.LabTechnician SET EmployeeNumber = @employeeNumber WHERE LabTechnicianID = @technicianId;",
            new { technicianId, employeeNumber = form.EmployeeNumber }, tx);

        await conn.ExecuteAsync(@"
            UPDATE dbo.[User]
               SET Name = @name, Surname = @surname, IDNumber = @saId, Email = @email, ContactNumber = @contact
             WHERE UserID = @userId;",
            new
            {
                userId, name = form.Name, surname = form.Surname, saId = form.SAIDNumber,
                email = form.Email, contact = form.ContactNumber
            }, tx);

        await conn.ExecuteAsync("DELETE FROM dbo.LabTechnicianTestType WHERE LabTechnicianID = @technicianId;", new { technicianId }, tx);
        await InsertTestTypeLinksAsync(conn, tx, technicianId, form.TestTypes);

        tx.Commit();
        return true;
    }

    private static Task InsertTestTypeLinksAsync(IDbConnection conn, IDbTransaction tx, int technicianId, IEnumerable<int> testTypeIds)
    {
        var rows = testTypeIds.Distinct().Select(id => new { technicianId, testTypeId = id }).ToList();
        if (rows.Count == 0) return Task.CompletedTask;

        return conn.ExecuteAsync(@"
            INSERT INTO dbo.LabTechnicianTestType (LabTechnicianID, TestTypeID)
            VALUES (@technicianId, @testTypeId);", rows, tx);
    }

    public async Task<DeleteResult> DeleteAsync(int technicianId)
    {
        using var conn = _db.CreateConnection();
        int? userId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT UserID FROM dbo.LabTechnician WHERE LabTechnicianID = @technicianId;", new { technicianId });
        if (userId is null) return DeleteResult.NotFound;

        // LabTechnicianTestType rows go with it (ON DELETE CASCADE)
        return await StaffDelete.DeleteOrDeactivateAsync(conn, userId.Value,
            "DELETE FROM dbo.LabTechnician WHERE LabTechnicianID = @technicianId;", new { technicianId });
    }
}
