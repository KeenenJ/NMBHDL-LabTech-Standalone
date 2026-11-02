#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NMBHLabSys.LabManagerSubsystem.Models;

namespace NMBHLabSys.LabManagerSubsystem.Data.Repositories;

// Tables used here (grp-04-14 + 01_LabManagerAdditions.sql):
//   TestCategory(TestCategoryID, Category, Description)                  <- addition
//   SampleType(SampleTypeID, SampleType)                                 <- addition
//   TestType(TestTypeID, TestName, Units, NormalRangeMin, NormalRangeMax, TurnAroundMinutes,
//            TestCategoryID, SampleTypeID)                               <- last two are additions
//   TestTypeConsumable(TestTypeID, ConsumableID)                         <- addition
//   LabTechnicianTestType(LabTechnicianID, TestTypeID)                   <- addition

/// <summary>Result of a delete that may be refused because other records use the row.</summary>
public enum DeleteResult
{
    Deleted,
    InUse,
    NotFound,

    /// <summary>The row is referenced elsewhere, so it was switched off (IsActive = 0) instead.</summary>
    Deactivated
}

// =====================================================================
// US-LM-01  TestCategory
// =====================================================================
public interface ITestCategoryRepository
{
    Task<List<TestCategoryRow>> GetAllAsync();
    Task<TestCategoryFormModel?> GetByIdAsync(int categoryId);
    Task<bool> NameExistsAsync(string name, int excludeCategoryId = 0);
    Task<int> CreateAsync(string name, string? description);
    Task<bool> UpdateAsync(int categoryId, string name, string? description);
    Task<DeleteResult> DeleteAsync(int categoryId);
    Task<List<LookupItem>> GetLookupAsync();
    Task<bool> ExistsAsync(int categoryId);
}

public class TestCategoryRepository : ITestCategoryRepository
{
    private readonly ILabManagerDb _db;
    public TestCategoryRepository(ILabManagerDb db) { _db = db; }

    // AC-01-01
    public async Task<List<TestCategoryRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TestCategoryRow>(@"
            SELECT c.TestCategoryID AS CategoryId,
                   c.Category       AS CategoryName,
                   c.Description,
                   (SELECT COUNT(*) FROM dbo.TestType t WHERE t.TestCategoryID = c.TestCategoryID) AS TestTypeCount
              FROM dbo.TestCategory c
             ORDER BY c.Category;");
        return rows.ToList();
    }

    // AC-01-05
    public async Task<TestCategoryFormModel?> GetByIdAsync(int categoryId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<TestCategoryFormModel>(@"
            SELECT TestCategoryID AS CategoryId, Category AS CategoryName, Description
              FROM dbo.TestCategory
             WHERE TestCategoryID = @categoryId;", new { categoryId });
    }

    // AC-01-03. SQL Server compares case-insensitively, so "full blood count" is a duplicate too.
    public async Task<bool> NameExistsAsync(string name, int excludeCategoryId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.TestCategory WHERE Category = @name AND TestCategoryID <> @excludeCategoryId
            ) THEN 1 ELSE 0 END;", new { name = name.Trim(), excludeCategoryId });
    }

    // AC-01-02
    public async Task<int> CreateAsync(string name, string? description)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.TestCategory (Category, Description)
            OUTPUT INSERTED.TestCategoryID
            VALUES (@name, @description);", new { name, description });
    }

    // AC-01-06
    public async Task<bool> UpdateAsync(int categoryId, string name, string? description)
    {
        using var conn = _db.CreateConnection();
        int rows = await conn.ExecuteAsync(@"
            UPDATE dbo.TestCategory
               SET Category = @name, Description = @description
             WHERE TestCategoryID = @categoryId;", new { categoryId, name, description });
        return rows > 0;
    }

    // AC-01-07: refused while test types are linked
    public async Task<DeleteResult> DeleteAsync(int categoryId)
    {
        using var conn = _db.CreateConnection();
        int code = await conn.ExecuteScalarAsync<int>(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.TestCategory WHERE TestCategoryID = @categoryId)
                SELECT 0;
            ELSE IF EXISTS (SELECT 1 FROM dbo.TestType WHERE TestCategoryID = @categoryId)
                SELECT 1;
            ELSE
            BEGIN
                DELETE FROM dbo.TestCategory WHERE TestCategoryID = @categoryId;
                SELECT 2;
            END", new { categoryId });

        return code switch { 0 => DeleteResult.NotFound, 1 => DeleteResult.InUse, _ => DeleteResult.Deleted };
    }

    public async Task<List<LookupItem>> GetLookupAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT TestCategoryID AS Id, Category AS Name FROM dbo.TestCategory ORDER BY Category;");
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(int categoryId)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.TestCategory WHERE TestCategoryID = @categoryId)
                        THEN 1 ELSE 0 END;", new { categoryId });
    }
}

// =====================================================================
// SampleType (AC-02-09)
// =====================================================================
public interface ISampleTypeRepository
{
    Task<List<SampleTypeRow>> GetAllAsync();
    Task<bool> NameExistsAsync(string name);
    Task<int> CreateAsync(string name);
    Task<DeleteResult> DeleteAsync(int sampleTypeId);
    Task<List<LookupItem>> GetLookupAsync();
    Task<bool> ExistsAsync(int sampleTypeId);
}

public class SampleTypeRepository : ISampleTypeRepository
{
    private readonly ILabManagerDb _db;
    public SampleTypeRepository(ILabManagerDb db) { _db = db; }

    public async Task<List<SampleTypeRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<SampleTypeRow>(@"
            SELECT s.SampleTypeID AS SampleTypeId,
                   s.SampleType   AS SampleTypeName,
                   (SELECT COUNT(*) FROM dbo.TestType t WHERE t.SampleTypeID = s.SampleTypeID) AS TestTypeCount
              FROM dbo.SampleType s
             ORDER BY s.SampleType;");
        return rows.ToList();
    }

    public async Task<bool> NameExistsAsync(string name)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.SampleType WHERE SampleType = @name)
                        THEN 1 ELSE 0 END;", new { name = name.Trim() });
    }

    public async Task<int> CreateAsync(string name)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.SampleType (SampleType)
            OUTPUT INSERTED.SampleTypeID
            VALUES (@name);", new { name });
    }

    public async Task<DeleteResult> DeleteAsync(int sampleTypeId)
    {
        using var conn = _db.CreateConnection();
        int code = await conn.ExecuteScalarAsync<int>(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.SampleType WHERE SampleTypeID = @sampleTypeId)
                SELECT 0;
            ELSE IF EXISTS (SELECT 1 FROM dbo.TestType WHERE SampleTypeID = @sampleTypeId)
                SELECT 1;
            ELSE
            BEGIN
                DELETE FROM dbo.SampleType WHERE SampleTypeID = @sampleTypeId;
                SELECT 2;
            END", new { sampleTypeId });

        return code switch { 0 => DeleteResult.NotFound, 1 => DeleteResult.InUse, _ => DeleteResult.Deleted };
    }

    public async Task<List<LookupItem>> GetLookupAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT SampleTypeID AS Id, SampleType AS Name FROM dbo.SampleType ORDER BY SampleType;");
        return rows.ToList();
    }

    public async Task<bool> ExistsAsync(int sampleTypeId)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.SampleType WHERE SampleTypeID = @sampleTypeId)
                        THEN 1 ELSE 0 END;", new { sampleTypeId });
    }
}

// =====================================================================
// US-LM-02  TestType (+ TestTypeConsumable)
// =====================================================================
public interface ITestTypeRepository
{
    Task<List<TestTypeRow>> GetAllAsync();
    Task<TestTypeRow?> GetByIdAsync(int testTypeId);
    Task<List<int>> GetConsumableIdsAsync(int testTypeId);
    Task<bool> NameExistsAsync(string name, int excludeTestTypeId = 0);
    Task<int> CreateAsync(TestTypeFormModel form);
    Task<bool> UpdateAsync(int testTypeId, TestTypeFormModel form);
    Task<DeleteResult> DeleteAsync(int testTypeId);
    Task<List<TechnicianRow>> GetAssignedTechniciansAsync(int testTypeId);
    Task<List<LookupItem>> GetLookupAsync();
    Task<int> CountExistingAsync(IEnumerable<int> testTypeIds);
}

public class TestTypeRepository : ITestTypeRepository
{
    private readonly ILabManagerDb _db;
    public TestTypeRepository(ILabManagerDb db) { _db = db; }

    // LEFT JOINs: test types the other subsystems created without a category/sample type still show
    private const string SelectTestType = @"
        SELECT t.TestTypeID             AS TestTypeId,
               t.TestName,
               t.TestCategoryID         AS CategoryId,
               ISNULL(c.Category, N'')  AS CategoryName,
               t.SampleTypeID           AS SampleTypeId,
               ISNULL(s.SampleType, N'') AS SampleTypeName,
               ISNULL(t.Units, N'')     AS Units,
               t.NormalRangeMin,
               t.NormalRangeMax,
               t.TurnAroundMinutes      AS TurnaroundTime
          FROM dbo.TestType t
          LEFT JOIN dbo.TestCategory c ON c.TestCategoryID = t.TestCategoryID
          LEFT JOIN dbo.SampleType   s ON s.SampleTypeID   = t.SampleTypeID";

    private const string SelectConsumableLinks = @"
        SELECT tc.TestTypeID AS TestTypeId, tc.ConsumableID AS ConsumableId, con.Name
          FROM dbo.TestTypeConsumable tc
          JOIN dbo.Consumable con ON con.ConsumableID = tc.ConsumableID";

    private sealed class ConsumableLink
    {
        public int TestTypeId { get; set; }
        public int ConsumableId { get; set; }
        public string Name { get; set; } = "";
    }

    // AC-02-01
    public async Task<List<TestTypeRow>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            SelectTestType + " ORDER BY t.TestName; " + SelectConsumableLinks + " ORDER BY con.Name;");

        var testTypes = (await multi.ReadAsync<TestTypeRow>()).ToList();
        var links = (await multi.ReadAsync<ConsumableLink>()).ToLookup(l => l.TestTypeId);
        foreach (var t in testTypes)
            t.ConsumableNames = links[t.TestTypeId].Select(l => l.Name).ToList();
        return testTypes;
    }

    public async Task<TestTypeRow?> GetByIdAsync(int testTypeId)
    {
        using var conn = _db.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            SelectTestType + " WHERE t.TestTypeID = @testTypeId; " +
            SelectConsumableLinks + " WHERE tc.TestTypeID = @testTypeId ORDER BY con.Name;",
            new { testTypeId });

        var testType = await multi.ReadFirstOrDefaultAsync<TestTypeRow>();
        if (testType is null) return null;
        testType.ConsumableNames = (await multi.ReadAsync<ConsumableLink>()).Select(l => l.Name).ToList();
        return testType;
    }

    public async Task<List<int>> GetConsumableIdsAsync(int testTypeId)
    {
        using var conn = _db.CreateConnection();
        var ids = await conn.QueryAsync<int>(
            "SELECT ConsumableID FROM dbo.TestTypeConsumable WHERE TestTypeID = @testTypeId;", new { testTypeId });
        return ids.ToList();
    }

    // AC-02-03
    public async Task<bool> NameExistsAsync(string name, int excludeTestTypeId = 0)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<bool>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.TestType WHERE TestName = @name AND TestTypeID <> @excludeTestTypeId
            ) THEN 1 ELSE 0 END;", new { name = name.Trim(), excludeTestTypeId });
    }

    // AC-02-02 / 02-04 / 02-05: the test type and its consumables in ONE transaction
    public async Task<int> CreateAsync(TestTypeFormModel form)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int testTypeId = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.TestType (TestName, Units, NormalRangeMin, NormalRangeMax, TurnAroundMinutes, TestCategoryID, SampleTypeID)
            OUTPUT INSERTED.TestTypeID
            VALUES (@TestName, @Units, @NormalRangeMin, @NormalRangeMax, @TurnaroundTime, @CategoryId, @SampleTypeId);",
            new
            {
                form.TestName, form.Units, form.NormalRangeMin, form.NormalRangeMax,
                form.TurnaroundTime, form.CategoryId, form.SampleTypeId
            }, tx);

        await InsertConsumableLinksAsync(conn, tx, testTypeId, form.Consumables);
        tx.Commit();
        return testTypeId;
    }

    public async Task<bool> UpdateAsync(int testTypeId, TestTypeFormModel form)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        int rows = await conn.ExecuteAsync(@"
            UPDATE dbo.TestType
               SET TestName          = @TestName,
                   Units             = @Units,
                   NormalRangeMin    = @NormalRangeMin,
                   NormalRangeMax    = @NormalRangeMax,
                   TurnAroundMinutes = @TurnaroundTime,
                   TestCategoryID    = @CategoryId,
                   SampleTypeID      = @SampleTypeId
             WHERE TestTypeID = @testTypeId;",
            new
            {
                testTypeId, form.TestName, form.Units, form.NormalRangeMin, form.NormalRangeMax,
                form.TurnaroundTime, form.CategoryId, form.SampleTypeId
            }, tx);

        if (rows == 0) return false;   // not committed -> rolled back

        await conn.ExecuteAsync("DELETE FROM dbo.TestTypeConsumable WHERE TestTypeID = @testTypeId;", new { testTypeId }, tx);
        await InsertConsumableLinksAsync(conn, tx, testTypeId, form.Consumables);
        tx.Commit();
        return true;
    }

    private static Task InsertConsumableLinksAsync(IDbConnection conn, IDbTransaction tx, int testTypeId, IEnumerable<int> consumableIds)
    {
        var rows = consumableIds.Distinct().Select(id => new { testTypeId, consumableId = id }).ToList();
        if (rows.Count == 0) return Task.CompletedTask;

        // Dapper runs the INSERT once for every item in the list
        return conn.ExecuteAsync(@"
            INSERT INTO dbo.TestTypeConsumable (TestTypeID, ConsumableID)
            VALUES (@testTypeId, @consumableId);", rows, tx);
    }

    /// <summary>Refused (InUse) while test results or test requests from the other subsystems use it.</summary>
    public async Task<DeleteResult> DeleteAsync(int testTypeId)
    {
        using var conn = _db.CreateConnection();
        try
        {
            int rows = await conn.ExecuteAsync("DELETE FROM dbo.TestType WHERE TestTypeID = @testTypeId;", new { testTypeId });
            return rows > 0 ? DeleteResult.Deleted : DeleteResult.NotFound;
        }
        catch (Exception ex) when (LabManagerSqlErrors.IsReferenceViolation(ex))
        {
            return DeleteResult.InUse;
        }
    }

    // AC-02-07 / AC-07-07 / TC-LM-02-05
    public async Task<List<TechnicianRow>> GetAssignedTechniciansAsync(int testTypeId)
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<TechnicianRow>(TechnicianRepository.SelectTechnician + @"
              JOIN dbo.LabTechnicianTestType ltt ON ltt.LabTechnicianID = lt.LabTechnicianID
             WHERE ltt.TestTypeID = @testTypeId
               AND u.IsActive = 1
             ORDER BY u.Surname, u.Name;", new { testTypeId });
        return rows.ToList();
    }

    public async Task<List<LookupItem>> GetLookupAsync()
    {
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<LookupItem>(
            "SELECT TestTypeID AS Id, TestName AS Name FROM dbo.TestType ORDER BY TestName;");
        return rows.ToList();
    }

    public async Task<int> CountExistingAsync(IEnumerable<int> testTypeIds)
    {
        var ids = testTypeIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        using var conn = _db.CreateConnection();
        // Dapper expands @ids into (@ids1, @ids2, ...)
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.TestType WHERE TestTypeID IN @ids;", new { ids });
    }
}
