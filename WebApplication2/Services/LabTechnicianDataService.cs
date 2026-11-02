using System.Collections.Concurrent;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WebApplication2.Models;

namespace WebApplication2.Services;

public sealed class LabTechnicianDataService
{
    private readonly string _connectionString;

    private static readonly ConcurrentDictionary<string, Assignment> Assignments = new();
    private static readonly ConcurrentDictionary<string, CompletedTest> CompletedTests = new();

    // Track selected tests and captured results during the current application session
    private sealed record Assignment(
        int SampleReceiptID,
        int TestRequestID,
        int TestTypeID,
        string TestName,
        DateTime SelectedAt,
        int TurnAroundMinutes);

    private sealed record CompletedTest(
        int TestResultID,
        int SampleReceiptID,
        int TestRequestID,
        int TestTypeID,
        string TestName,
        string Result,
        string Status,
        DateTime CompletionDateTime);

    private sealed class CaptureTestRow
    {
        public int SampleReceiptID { get; set; }
        public int TestRequestID { get; set; }
        public int TestTypeID { get; set; }
        public int PatientID { get; set; }
        public string Barcode { get; set; } = "";
        public string PatientName { get; set; } = "";
        public string PatientNumber { get; set; } = "";
        public string TestName { get; set; } = "";
        public string Units { get; set; } = "";
        public string Urgency { get; set; } = "";
        public string RequestStatus { get; set; } = "";
        public DateTime RequestDate { get; set; }
        public DateTime? DateTimeReceived { get; set; }
        public decimal? NormalRangeMin { get; set; }
        public decimal? NormalRangeMax { get; set; }
        public int TurnAroundMinutes { get; set; }
    }

    private sealed class TestRangeRow
    {
        public decimal? NormalRangeMin { get; set; }
        public decimal? NormalRangeMax { get; set; }
    }

    public LabTechnicianDataService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Missing DefaultConnection connection string.");
    }

    public async Task<IReadOnlyList<LabEscalationViewModel>>
     GetActiveEscalationsAsync(
         int labTechnicianId,
         CancellationToken cancellationToken = default)
    {
        if (labTechnicianId <= 0)
            return Array.Empty<LabEscalationViewModel>();

        // Escalations must reflect active assignments, not every received sample.
        // The assigned-test queue starts its TAT clock when the technician selects
        // the individual test, so use that same timestamp here.
        const string sql = """
        SELECT
            tr.TestRequestID,
            sr.SampleReceiptID,
            tt.TestTypeID,
            LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
            COALESCE(tt.TestName, '') AS TestName,
            COALESCE(tr.Urgency, '') AS Urgency,
            COALESCE(sr.Barcode, '') AS Barcode,
            LTRIM(RTRIM(CONCAT(
                COALESCE(doctor.Surname, ''),
                CASE WHEN doctor.Surname IS NOT NULL THEN ', ' ELSE '' END,
                COALESCE(doctor.Name, '')
            ))) AS DoctorName,
            sr.DateTimeReceived,
            COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes
        FROM dbo.SampleReceipt sr
        INNER JOIN dbo.TestRequest tr
            ON tr.TestRequestID = sr.TestRequestID
        INNER JOIN dbo.Patient p
            ON p.PatientID = tr.PatientID
        INNER JOIN dbo.[User] u
            ON u.UserID = p.UserID
        LEFT JOIN dbo.Doctor d
            ON d.DoctorID = tr.DoctorID
        LEFT JOIN dbo.[User] doctor
            ON doctor.UserID = d.UserID
        CROSS APPLY STRING_SPLIT(
            REPLACE(
                REPLACE(
                    REPLACE(
                        REPLACE(COALESCE(tr.Type, ''), ';', ','),
                        '+', ','
                    ),
                    CHAR(13), ','
                ),
                CHAR(10), ','
            ),
            ','
        ) requested
        INNER JOIN dbo.TestType tt
            ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
           AND (sr.TestTypeID IS NULL OR sr.TestTypeID = tt.TestTypeID)
        WHERE COALESCE(tt.TurnAroundMinutes, 0) > 0
          AND sr.DateTimeReceived IS NOT NULL
          AND COALESCE(tr.Status, '') NOT IN
              ('Cancelled', 'Canceled', 'Completed', 'Rejected');
        """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<LabEscalationViewModel>(
            new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();

        // Use exactly the same assignment ownership and completion rules as Assigned Tests.
        var assignedTests = await GetAssignedTestsAsync(cancellationToken);
        var activeAssignments = assignedTests
            .Where(test =>
                (test.ReceivedByLabTechnicianID == 0 ||
                 test.ReceivedByLabTechnicianID == labTechnicianId) &&
                !IsTestCompleted(test.SampleReceiptID, test.TestTypeID))
            .ToDictionary(
                test => $"{test.SampleReceiptID}:{test.TestTypeID}",
                test => test,
                StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        return rows
            .Where(row => activeAssignments.ContainsKey(
                $"{row.SampleReceiptID}:{row.TestTypeID}"))
            .Select(row =>
            {
                var assignment = activeAssignments[
                    $"{row.SampleReceiptID}:{row.TestTypeID}"];
                var dueAt = assignment.SelectedAt!.Value
                    .AddMinutes(assignment.TurnAroundMinutes);

                row.AssignedAt = assignment.SelectedAt;
                row.MinutesOverdue = (int)Math.Floor((now - dueAt).TotalMinutes);
                return row;
            })
            .Where(row => row.MinutesOverdue > 0)
            .OrderByDescending(row => row.MinutesOverdue)
            .ToList();
    }

    public async Task<IReadOnlyList<LabTechnicianQueueItem>>
        GetRequestsAwaitingReceiptAsync(
            CancellationToken cancellationToken = default)
    {
        // Expand the request's comma-separated test list into one row per test.
        // Each row can then be received independently under the same parent request.
        const string sql = """
            SELECT
                tr.TestRequestID,
                COALESCE(tr.barcode, '') AS Barcode,
                tr.RequestDate,
                COALESCE(tr.Urgency, '') AS Urgency,
                COALESCE(tr.Status, '') AS Status,
                tt.TestTypeID,
                COALESCE(tt.TestName, '') AS Type,
                p.PatientID,
                LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                COALESCE(u.IDNumber, '') AS PatientNumber,
                CAST(NULL AS datetime2) AS DateTimeReceived
            FROM dbo.TestRequest tr
            INNER JOIN dbo.Patient p
                ON p.PatientID = tr.PatientID
            INNER JOIN dbo.[User] u
                ON u.UserID = p.UserID
            CROSS APPLY STRING_SPLIT(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(COALESCE(tr.Type, ''), ';', ','),
                            '+', ','
                        ),
                        CHAR(13), ','
                    ),
                    CHAR(10), ','
                ),
                ','
            ) requested
            INNER JOIN dbo.TestType tt
                ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
            WHERE COALESCE(tr.Status, '') NOT IN
                ('Cancelled', 'Canceled', 'Completed')
              AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.SampleReceipt sr
                    WHERE sr.TestRequestID = tr.TestRequestID
                      AND sr.TestTypeID = tt.TestTypeID
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.SampleReceiptRejection rejection
                          WHERE rejection.SampleReceiptID = sr.SampleReceiptID
                      )
                )
            ORDER BY
                CASE
                    WHEN UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, '')))) = 'STAT'
                    THEN 0
                    WHEN UPPER(LTRIM(RTRIM(COALESCE(tr.Urgency, '')))) = 'URGENT'
                    THEN 1
                    ELSE 2
                END,
                tr.RequestDate DESC,
                tt.TestName;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var rows = await connection.QueryAsync<LabTechnicianQueueItem>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<LabTechnicianQueueItem>>
        GetReceivedSamplesAsync(
            CancellationToken cancellationToken = default)
    {
        // Keep legacy receipts readable while showing the individual test and receiver
        // for every receipt created by the updated workflow.
        const string sql = """
            SELECT
                sr.SampleReceiptID,
                tr.TestRequestID,
                COALESCE(sr.Barcode, '') AS Barcode,
                tr.RequestDate,
                COALESCE(tr.Urgency, '') AS Urgency,
                COALESCE(tr.Status, '') AS Status,
                COALESCE(tt.TestName, tr.Type, '') AS Type,
                COALESCE(sr.TestTypeID, 0) AS TestTypeID,
                COALESCE(sr.ReceivedByLabTechnicianID, 0) AS ReceivedByLabTechnicianID,
                p.PatientID,
                LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                COALESCE(u.IDNumber, '') AS PatientNumber,
                sr.DateTimeReceived,
                CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejected,
                COALESCE(rejection.Reason, '') AS RejectionReason
            FROM dbo.SampleReceipt sr
            INNER JOIN dbo.TestRequest tr
                ON tr.TestRequestID = sr.TestRequestID
            INNER JOIN dbo.Patient p
                ON p.PatientID = tr.PatientID
            INNER JOIN dbo.[User] u
                ON u.UserID = p.UserID
            LEFT JOIN dbo.TestType tt
                ON tt.TestTypeID = sr.TestTypeID
            LEFT JOIN dbo.SampleReceiptRejection rejection
                ON rejection.SampleReceiptID = sr.SampleReceiptID
            ORDER BY sr.DateTimeReceived DESC, sr.SampleReceiptID DESC;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var rows = await connection.QueryAsync<LabTechnicianQueueItem>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    // Save one physical tube against one requested test on the parent request.
    public async Task<bool> ReceiveSampleAsync(
        int testRequestId,
        int testTypeId,
        string barcode,
        int labTechnicianId,
        CancellationToken cancellationToken = default)
    {
        if (testRequestId <= 0 ||
            testTypeId <= 0 ||
            labTechnicianId <= 0 ||
            string.IsNullOrWhiteSpace(barcode))
            return false;

        const string sql = """
        SET XACT_ABORT ON;

        BEGIN TRY
            BEGIN TRANSACTION;

            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.TestRequest WITH (UPDLOCK, HOLDLOCK)
                WHERE TestRequestID = @TestRequestID
                  AND COALESCE(Status, '') NOT IN
                      ('Cancelled', 'Canceled', 'Completed')
            )
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT CAST(0 AS bit);
            END
            ELSE IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.TestRequest tr
                CROSS APPLY STRING_SPLIT(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                '+', ','
                            ),
                            CHAR(13), ','
                        ),
                        CHAR(10), ','
                    ),
                    ','
                ) requested
                INNER JOIN dbo.TestType tt
                    ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                WHERE tr.TestRequestID = @TestRequestID
                  AND tt.TestTypeID = @TestTypeID
            )
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT CAST(0 AS bit);
            END
            ELSE IF EXISTS
            (
                SELECT 1
                FROM dbo.SampleReceipt WITH (UPDLOCK, HOLDLOCK)
                WHERE LTRIM(RTRIM(COALESCE(Barcode, ''))) = @Barcode
            )
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT CAST(0 AS bit);
            END
            ELSE IF EXISTS
            (
                SELECT 1
                FROM dbo.SampleReceipt sr WITH (UPDLOCK, HOLDLOCK)
                WHERE sr.TestRequestID = @TestRequestID
                  AND sr.TestTypeID = @TestTypeID
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.SampleReceiptRejection rejection
                      WHERE rejection.SampleReceiptID = sr.SampleReceiptID
                  )
            )
            BEGIN
                ROLLBACK TRANSACTION;
                SELECT CAST(0 AS bit);
            END
            ELSE
            BEGIN
                INSERT INTO dbo.SampleReceipt
                    (Barcode, DateTimeReceived, TestRequestID,
                     TestTypeID, ReceivedByLabTechnicianID)
                VALUES
                    (@Barcode, SYSDATETIME(), @TestRequestID,
                     @TestTypeID, @LabTechnicianID);

                -- Mark the parent request received only after every requested
                -- test has an active sample receipt.
                UPDATE tr
                SET Status = 'Samples Received'
                FROM dbo.TestRequest tr
                WHERE tr.TestRequestID = @TestRequestID
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM STRING_SPLIT(
                          REPLACE(
                              REPLACE(
                                  REPLACE(
                                      REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                      '+', ','
                                  ),
                                  CHAR(13), ','
                              ),
                              CHAR(10), ','
                          ),
                          ','
                      ) requested
                      INNER JOIN dbo.TestType tt
                          ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                      WHERE NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.SampleReceipt sr
                          WHERE sr.TestRequestID = tr.TestRequestID
                            AND sr.TestTypeID = tt.TestTypeID
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.SampleReceiptRejection rejection
                                WHERE rejection.SampleReceiptID = sr.SampleReceiptID
                            )
                      )
                  );

                COMMIT TRANSACTION;
                SELECT CAST(1 AS bit);
            END
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0
                ROLLBACK TRANSACTION;

            THROW;
        END CATCH;
        """;

        await using var connection = new SqlConnection(_connectionString);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    TestRequestID = testRequestId,
                    TestTypeID = testTypeId,
                    Barcode = barcode.Trim(),
                    LabTechnicianID = labTechnicianId
                },
                cancellationToken: cancellationToken));
    }


    // Record a sample rejection and clear its active test assignments
    public async Task<bool> RejectSampleAsync(
        int sampleReceiptId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (sampleReceiptId <= 0 || string.IsNullOrWhiteSpace(reason))
            return false;

        const string sql = """
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.SampleReceipt
                WHERE SampleReceiptID = @SampleReceiptID
            )
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.SampleReceiptRejection
                WHERE SampleReceiptID = @SampleReceiptID
            )
            BEGIN
                SELECT CAST(0 AS bit);
                RETURN;
            END;

            INSERT INTO dbo.SampleReceiptRejection
                (SampleReceiptID, Reason, RejectedAt)
            VALUES
                (@SampleReceiptID, @Reason, SYSDATETIME());

            -- If this rejection leaves any requested test without an active
            -- sample, return the parent request to its submitted state.
            UPDATE tr
            SET Status = 'Submitted'
            FROM dbo.TestRequest tr
            INNER JOIN dbo.SampleReceipt rejectedReceipt
                ON rejectedReceipt.TestRequestID = tr.TestRequestID
            WHERE rejectedReceipt.SampleReceiptID = @SampleReceiptID
              AND COALESCE(tr.Status, '') = 'Samples Received'
              AND EXISTS
              (
                  SELECT 1
                  FROM STRING_SPLIT(
                      REPLACE(
                          REPLACE(
                              REPLACE(
                                  REPLACE(COALESCE(tr.Type, ''), ';', ','),
                                  '+', ','
                              ),
                              CHAR(13), ','
                          ),
                          CHAR(10), ','
                      ),
                      ','
                  ) requested
                  INNER JOIN dbo.TestType tt
                      ON LTRIM(RTRIM(requested.value)) = LTRIM(RTRIM(tt.TestName))
                  WHERE NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.SampleReceipt sr
                      WHERE sr.TestRequestID = tr.TestRequestID
                        AND sr.TestTypeID = tt.TestTypeID
                        AND NOT EXISTS
                        (
                            SELECT 1
                            FROM dbo.SampleReceiptRejection rejection
                            WHERE rejection.SampleReceiptID = sr.SampleReceiptID
                        )
                  )
              );

            SELECT CAST(1 AS bit);
            """;

        await using var connection = new SqlConnection(_connectionString);

        bool rejected = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    SampleReceiptID = sampleReceiptId,
                    Reason = reason.Trim()
                },
                cancellationToken: cancellationToken));

        if (rejected)
        {
            string keyPrefix = $"{sampleReceiptId}:";
            foreach (var key in Assignments.Keys.Where(x => x.StartsWith(keyPrefix, StringComparison.Ordinal)))
                Assignments.TryRemove(key, out _);
        }

        return rejected;
    }

    public async Task<IReadOnlyList<LabTechnicianQueueItem>>
        GetAvailableTestTypesAsync(
            CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TestTypeID,
                   COALESCE(TestName, '') AS TestName,
                   COALESCE(TurnAroundMinutes, 0) AS TurnAroundMinutes
            FROM dbo.TestType
            ORDER BY TestName;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var rows = await connection.QueryAsync<LabTechnicianQueueItem>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<ConsumableStockItem>>
        GetConsumableStockAsync(
            CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT ConsumableID,
                   COALESCE(Name, '') AS Name,
                   COALESCE(QtyOnHand, 0) AS QtyOnHand,
                   COALESCE(ReOrderLevel, 0) AS ReOrderLevel
            FROM dbo.Consumable
            ORDER BY Name;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var rows = await connection.QueryAsync<ConsumableStockItem>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> IsConsumableInStockAsync(
        int consumableId,
        int requiredQuantity = 1,
        CancellationToken cancellationToken = default)
    {
        if (consumableId <= 0 || requiredQuantity <= 0)
            return false;

        const string sql = """
            SELECT CASE
                WHEN QtyOnHand >= @RequiredQuantity
                THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END
            FROM dbo.Consumable
            WHERE ConsumableID = @ConsumableID;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var result = await connection.QuerySingleOrDefaultAsync<bool?>(
            new CommandDefinition(
                sql,
                new
                {
                    ConsumableID = consumableId,
                    RequiredQuantity = requiredQuantity
                },
                cancellationToken: cancellationToken));

        return result == true;
    }

    // Validate the requested test before adding it to the assignment queue
    public async Task<(bool Success, string Message)> SelectTestAsync(
        int sampleReceiptId,
        int testTypeId,
        int labTechnicianId,
        CancellationToken cancellationToken = default)
    {
        if (sampleReceiptId <= 0 || testTypeId <= 0 || labTechnicianId <= 0)
            return (false, "Select a valid sample and test.");

        const string sql = """
            SELECT sr.SampleReceiptID,
                   tr.TestRequestID,
                   COALESCE(tr.Type, '') AS RequestType,
                   COALESCE(tr.Status, '') AS RequestStatus,
                   COALESCE(tt.TestName, '') AS TestName,
                   COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,
                   sr.TestTypeID AS ReceiptTestTypeID,
                   sr.ReceivedByLabTechnicianID,
                   CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejected
            FROM dbo.SampleReceipt sr
            INNER JOIN dbo.TestRequest tr
                ON tr.TestRequestID = sr.TestRequestID
            CROSS JOIN dbo.TestType tt
            LEFT JOIN dbo.SampleReceiptRejection rejection
                ON rejection.SampleReceiptID = sr.SampleReceiptID
            WHERE sr.SampleReceiptID = @SampleReceiptID
              AND tt.TestTypeID = @TestTypeID
              AND (sr.TestTypeID IS NULL OR sr.TestTypeID = @TestTypeID)
              AND (sr.ReceivedByLabTechnicianID IS NULL
                   OR sr.ReceivedByLabTechnicianID = @LabTechnicianID);
            """;

        await using var connection = new SqlConnection(_connectionString);

        var row = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                sql,
                new
                {
                    SampleReceiptID = sampleReceiptId,
                    TestTypeID = testTypeId,
                    LabTechnicianID = labTechnicianId
                },
                cancellationToken: cancellationToken));

        if (row == null)
            return (false, "Sample or test type was not found.");

        string requestType = (string)row.RequestType;
        string testName = (string)row.TestName;
        string requestStatus = (string)row.RequestStatus;

        if ((bool)row.IsRejected)
            return (false, "This sample has been rejected. Receive a replacement sample for this request.");

        if (requestStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
            requestStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase) ||
            requestStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "This request is no longer available for assignment.");
        }

        var requestedNames = requestType.Split(
            new[] { ',', ';', '+', '\n', '\r' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!requestedNames.Contains(testName, StringComparer.OrdinalIgnoreCase))
        {
            return (false,
                "This test could not be matched to the request's Type field. No assignment was made.");
        }



        const string linkedConsumablesSql = """
    SELECT
        c.ConsumableID,
        COALESCE(c.Name, '') AS Name,
        COALESCE(c.QtyOnHand, 0) AS QtyOnHand,
        COALESCE(c.ReOrderLevel, 0) AS ReOrderLevel
    FROM dbo.TestTypeConsumable tc
    INNER JOIN dbo.Consumable c
        ON c.ConsumableID = tc.ConsumableID
    WHERE tc.TestTypeID = @TestTypeID
    ORDER BY c.Name;
    """;

        var linkedConsumables = (await connection.QueryAsync<ConsumableStockItem>(
            new CommandDefinition(
                linkedConsumablesSql,
                new { TestTypeID = testTypeId },
                cancellationToken: cancellationToken)))
            .AsList();

        // Do not allow assignment when the test has no linked consumables
        if (linkedConsumables.Count == 0)
        {
            return (false,
                $"'{testName}' cannot be assigned because no consumables are linked to this test type.");
        }

        // Block only when at least one required consumable has zero stock
        var outOfStockItems = linkedConsumables
            .Where(c => c.QtyOnHand <= 0)
            .ToList();

        if (outOfStockItems.Count > 0)
        {
            var itemNames = string.Join(
                ", ",
                outOfStockItems.Select(c => c.Name));

            return (false,
                $"'{testName}' cannot be assigned because required consumables are out of stock: {itemNames}.");
        }

        // Low stock is a warning, not a blocker.

        var lowStockItems = linkedConsumables
            .Where(c => c.QtyOnHand > 0 &&
                        c.QtyOnHand <= c.ReOrderLevel)
            .ToList();

        string stockWarning = "";

        if (lowStockItems.Count > 0)
        {
            var itemNames = string.Join(
                ", ",
                lowStockItems.Select(c =>
                    $"{c.Name} ({c.QtyOnHand} on hand; reorder level {c.ReOrderLevel})"));

            stockWarning =
                $" Warning: low stock for {itemNames}.";
        }



        string key = $"{sampleReceiptId}:{testTypeId}";

        const string latestDecisionSql = """
            SELECT latest.Decision
            FROM
            (
                SELECT TOP (1) tres.TestResultID
                FROM dbo.TestResult tres
                WHERE tres.TestRequestID = @TestRequestID
                  AND tres.TestTypeID = @TestTypeID
                ORDER BY tres.CompletionDateTime DESC, tres.TestResultID DESC
            ) newest
            OUTER APPLY
            (
                SELECT TOP (1) rv.Decision
                FROM dbo.ResultVerification rv
                WHERE rv.TestResultID = newest.TestResultID
                ORDER BY rv.VerificationDateTime DESC, rv.ResultVerificationID DESC
            ) latest;
            """;

        var latestDecision = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                latestDecisionSql,
                new { TestRequestID = (int)row.TestRequestID, TestTypeID = testTypeId },
                cancellationToken: cancellationToken));

        bool isReturnedForRecapture = string.Equals(
            latestDecision, "Returned", StringComparison.OrdinalIgnoreCase);

        if (CompletedTests.ContainsKey(key) && !isReturnedForRecapture)
            return (false, "This test already has a saved result. Only results returned by verification can be captured again.");

        var assignment = new Assignment(
            sampleReceiptId, (int)row.TestRequestID, testTypeId, testName,
            DateTime.Now, (int)row.TurnAroundMinutes);

        if (isReturnedForRecapture)
        {
            Assignments[key] = assignment;
            return (true, $"'{testName}' was returned for recapture and is ready to be captured again.");
        }

        if (!Assignments.TryAdd(key, assignment))
            return (false, "This test has already been assigned to this sample.");

        return (true, $"'{testName}' assigned. Its turnaround timer has started.{stockWarning}");
    }

    // Returns stable sample:test keys for the current in-memory assignment session
    // The Awaiting Selection view uses these keys to avoid offering duplicate assignments
    public IReadOnlyCollection<string> GetAssignedTestKeys()
    {
        return Assignments.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    // Rebuild the assigned-test list using the current received samples

    public async Task<IReadOnlyList<LabTechnicianQueueItem>>
        GetAssignedTestsAsync(CancellationToken cancellationToken = default)
    {
        var assignments = Assignments.Values
            .OrderByDescending(x => x.SelectedAt)
            .ToList();

        if (assignments.Count == 0)
            return Array.Empty<LabTechnicianQueueItem>();

        var samples = await GetReceivedSamplesAsync(cancellationToken);
        var result = new List<LabTechnicianQueueItem>();

        foreach (var assignment in assignments)
        {
            var sample = samples.FirstOrDefault(
                x => x.SampleReceiptID == assignment.SampleReceiptID);

            if (sample == null || sample.IsRejected)
                continue;

            // Create a NEW object for every assigned test.
            // Do not modify and reuse the original sample object.
            var assignedTest = new LabTechnicianQueueItem
            {
                TestRequestID = sample.TestRequestID,
                Barcode = sample.Barcode,
                RequestDate = sample.RequestDate,
                Urgency = sample.Urgency,
                Status = sample.Status,
                Type = sample.Type,
                PatientID = sample.PatientID,
                PatientName = sample.PatientName,
                PatientNumber = sample.PatientNumber,
                DateTimeReceived = sample.DateTimeReceived,
                SampleReceiptID = sample.SampleReceiptID,
                ReceivedByLabTechnicianID = sample.ReceivedByLabTechnicianID,
                IsRejected = sample.IsRejected,
                RejectionReason = sample.RejectionReason,

                // These fields belong to this specific assignment.
                TestTypeID = assignment.TestTypeID,
                TestName = assignment.TestName,
                SelectedAt = assignment.SelectedAt,
                TurnAroundMinutes = assignment.TurnAroundMinutes,
                IsSelected = true,

                // Preserve any stock information available on the sample.
                IsStockAvailable = sample.IsStockAvailable,
                StockMessage = sample.StockMessage,
                Consumable = sample.Consumable,
                Consumables = sample.Consumables
                    .Select(c => new ConsumableStockItem
                    {
                        ConsumableID = c.ConsumableID,
                        Name = c.Name,
                        QtyOnHand = c.QtyOnHand,
                        ReOrderLevel = c.ReOrderLevel
                    })
                    .ToList()
            };

            result.Add(assignedTest);
        }

        return result;
    }


    public async Task<IReadOnlyList<LabTechnicianQueueItem>>
        // Find a patient's active requests and their assigned tests using the registration number
        FindPatientTestsAsync(
            string registrationNumber,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
            return Array.Empty<LabTechnicianQueueItem>();

        const string sql = """
            SELECT sr.SampleReceiptID,
                   tr.TestRequestID,
                   COALESCE(sr.Barcode, '') AS Barcode,
                   tr.RequestDate,
                   COALESCE(tr.Urgency, '') AS Urgency,
                   COALESCE(tr.Status, '') AS Status,
                   COALESCE(tr.Type, '') AS Type,
                   p.PatientID,
                   LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                   COALESCE(u.IDNumber, '') AS PatientNumber,
                   sr.DateTimeReceived,
                   CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejected,
                   COALESCE(rejection.Reason, '') AS RejectionReason
            FROM dbo.[User] u
            INNER JOIN dbo.Patient p ON p.UserID = u.UserID
            INNER JOIN dbo.TestRequest tr ON tr.PatientID = p.PatientID
            INNER JOIN dbo.SampleReceipt sr ON sr.TestRequestID = tr.TestRequestID
            LEFT JOIN dbo.SampleReceiptRejection rejection
                ON rejection.SampleReceiptID = sr.SampleReceiptID
            WHERE LTRIM(RTRIM(COALESCE(u.IDNumber, ''))) = @RegistrationNumber
              AND COALESCE(tr.Status, '') NOT IN
                  ('Cancelled', 'Canceled', 'Completed')
            ORDER BY tr.RequestDate DESC, sr.DateTimeReceived DESC;
            """;

        await using var connection = new SqlConnection(_connectionString);

        var samples = (await connection.QueryAsync<LabTechnicianQueueItem>(
            new CommandDefinition(
                sql,
                new { RegistrationNumber = registrationNumber.Trim() },
                cancellationToken: cancellationToken))).AsList();

        var result = new List<LabTechnicianQueueItem>();

        foreach (var sample in samples)
        {
            var assignments = Assignments.Values
                .Where(a => a.SampleReceiptID == sample.SampleReceiptID)
                .OrderBy(a => a.TestName)
                .ToList();

            foreach (var assignment in assignments)
            {
                result.Add(new LabTechnicianQueueItem
                {
                    SampleReceiptID = sample.SampleReceiptID,
                    TestRequestID = sample.TestRequestID,
                    Barcode = sample.Barcode,
                    RequestDate = sample.RequestDate,
                    Urgency = sample.Urgency,
                    Status = sample.Status,
                    Type = sample.Type,
                    PatientID = sample.PatientID,
                    PatientName = sample.PatientName,
                    PatientNumber = sample.PatientNumber,
                    DateTimeReceived = sample.DateTimeReceived,
                    IsRejected = sample.IsRejected,
                    RejectionReason = sample.RejectionReason,
                    TestTypeID = assignment.TestTypeID,
                    TestName = assignment.TestName,
                    SelectedAt = assignment.SelectedAt,
                    TurnAroundMinutes = assignment.TurnAroundMinutes,
                    IsSelected = true
                });
            }
        }

        return result;
    }
    // Prepare the capture form and prevent duplicate results unless recapture is allowed
    public async Task<CaptureResultsViewModel?> GetCaptureTestAsync(
        int sampleReceiptId,
        int testTypeId,
        CancellationToken cancellationToken = default)
    {
        if (sampleReceiptId <= 0 || testTypeId <= 0)
            return null;

        string key = $"{sampleReceiptId}:{testTypeId}";

        await using var connection = new SqlConnection(_connectionString);

        // Resolve the sample and test from SQL first. Returned tests must remain
        // recapturable even when the app restarts and its in-memory assignment is lost.
        const string assignmentSql = """
            SELECT sr.SampleReceiptID,
                   tr.TestRequestID,
                   COALESCE(tr.Type, '') AS RequestType,
                   COALESCE(tr.Status, '') AS RequestStatus,
                   COALESCE(tt.TestName, '') AS TestName,
                   COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,
                   CAST(CASE WHEN rejection.SampleReceiptID IS NULL THEN 0 ELSE 1 END AS bit) AS IsRejected
            FROM dbo.SampleReceipt sr
            INNER JOIN dbo.TestRequest tr
                ON tr.TestRequestID = sr.TestRequestID
            INNER JOIN dbo.TestType tt
                ON tt.TestTypeID = @TestTypeID
            LEFT JOIN dbo.SampleReceiptRejection rejection
                ON rejection.SampleReceiptID = sr.SampleReceiptID
            WHERE sr.SampleReceiptID = @SampleReceiptID
              AND (sr.TestTypeID IS NULL OR sr.TestTypeID = @TestTypeID);
            """;

        var assignmentRow = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                assignmentSql,
                new { SampleReceiptID = sampleReceiptId, TestTypeID = testTypeId },
                cancellationToken: cancellationToken));

        if (assignmentRow == null || (bool)assignmentRow.IsRejected)
            return null;

        int testRequestId = (int)assignmentRow.TestRequestID;
        string requestType = (string)assignmentRow.RequestType;
        string requestStatus = (string)assignmentRow.RequestStatus;
        string testName = (string)assignmentRow.TestName;

        if (requestStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
            requestStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase) ||
            requestStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            return null;

        var requestedNames = requestType.Split(
            new[] { ',', ';', '+', '\n', '\r' },
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!requestedNames.Contains(testName, StringComparer.OrdinalIgnoreCase))
            return null;

        const string latestResultSql = """
            SELECT TOP (1)
                   tres.TestResultID,
                   latest.Decision
            FROM dbo.TestResult tres
            OUTER APPLY
            (
                SELECT TOP (1) rv.Decision
                FROM dbo.ResultVerification rv
                WHERE rv.TestResultID = tres.TestResultID
                ORDER BY rv.VerificationDateTime DESC, rv.ResultVerificationID DESC
            ) latest
            WHERE tres.TestRequestID = @TestRequestID
              AND tres.TestTypeID = @TestTypeID
            ORDER BY tres.CompletionDateTime DESC, tres.TestResultID DESC;
            """;

        var latestResult = await connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                latestResultSql,
                new { TestRequestID = testRequestId, TestTypeID = testTypeId },
                cancellationToken: cancellationToken));

        bool hasPreviousResult = latestResult != null;
        string? latestDecision = latestResult == null
            ? null
            : (string?)latestResult.Decision;

        bool isReturnedForRecapture =
            string.Equals(latestDecision, "Returned", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(latestDecision, "Return", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(latestDecision, "Rejected", StringComparison.OrdinalIgnoreCase);

        // Existing results may only be captured again when the latest one was
        // explicitly returned/rejected. This also blocks duplicate captures after restart.
        if (hasPreviousResult && !isReturnedForRecapture)
            return null;

        if (!Assignments.TryGetValue(key, out var assignment))
        {
            if (!isReturnedForRecapture)
                return null;

            assignment = new Assignment(
                sampleReceiptId,
                testRequestId,
                testTypeId,
                testName,
                DateTime.Now,
                (int)assignmentRow.TurnAroundMinutes);

            Assignments[key] = assignment;
        }

        const string captureSql = """
            SELECT sr.SampleReceiptID,
                   tr.TestRequestID,
                   tt.TestTypeID,
                   p.PatientID,
                   COALESCE(sr.Barcode, '') AS Barcode,
                   LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
                   COALESCE(u.IDNumber, '') AS PatientNumber,
                   COALESCE(tt.TestName, '') AS TestName,
                   COALESCE(tt.Units, '') AS Units,
                   tt.NormalRangeMin,
                   tt.NormalRangeMax,
                   COALESCE(tt.TurnAroundMinutes, 0) AS TurnAroundMinutes,
                   tr.RequestDate,
                   sr.DateTimeReceived,
                   COALESCE(tr.Urgency, '') AS Urgency,
                   COALESCE(tr.Status, '') AS RequestStatus
            FROM dbo.SampleReceipt sr
            INNER JOIN dbo.TestRequest tr
                ON tr.TestRequestID = sr.TestRequestID
            INNER JOIN dbo.Patient p
                ON p.PatientID = tr.PatientID
            INNER JOIN dbo.[User] u
                ON u.UserID = p.UserID
            INNER JOIN dbo.TestType tt
                ON tt.TestTypeID = @TestTypeID
            WHERE sr.SampleReceiptID = @SampleReceiptID
              AND tr.TestRequestID = @TestRequestID
              AND (sr.TestTypeID IS NULL OR sr.TestTypeID = @TestTypeID);
            """;

        var row = await connection.QuerySingleOrDefaultAsync<CaptureTestRow>(
            new CommandDefinition(
                captureSql,
                new
                {
                    SampleReceiptID = sampleReceiptId,
                    TestTypeID = testTypeId,
                    TestRequestID = assignment.TestRequestID
                },
                cancellationToken: cancellationToken));

        if (row == null)
            return null;

        return new CaptureResultsViewModel
        {
            SampleReceiptID = row.SampleReceiptID,
            TestRequestID = row.TestRequestID,
            TestTypeID = row.TestTypeID,
            RequestNumber = $"REQ-{row.TestRequestID}",
            RegistrationNumber = row.PatientNumber,
            PatientName = row.PatientName,
            Barcode = row.Barcode,
            TestName = row.TestName,
            Units = row.Units,
            NormalRangeMin = row.NormalRangeMin,
            NormalRangeMax = row.NormalRangeMax,
            Status = isReturnedForRecapture ? "Returned for Recapture" : "Awaiting Result",
            IsCompleted = false,
            PatientID = row.PatientID
        };
    }

    public async Task<(bool Success, string Message, int TestResultID)>
        // Validate and save results with transaction, duplicate-capture protection and audit history
        SaveTestResultAsync(
            int sampleReceiptId,
            int testTypeId,
            string result,
            string technicianNotes,
            int labTechnicianId,
            CancellationToken cancellationToken = default)
    {
        if (sampleReceiptId <= 0 || testTypeId <= 0)
            return (false, "Select a valid assigned test.", 0);

        if (string.IsNullOrWhiteSpace(result))
            return (false, "Enter a result value before saving.", 0);

        if (!decimal.TryParse(
                result,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal numericResult))
            return (false, "Enter a valid numeric result.", 0);

        string key = $"{sampleReceiptId}:{testTypeId}";

        if (!Assignments.TryGetValue(key, out var assignment))
            return (false, "This test is not currently assigned. Please select the test again.", 0);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            //deducts consumables 
            const string captureConsumablesSql = """
            SELECT c.ConsumableID,
                   COALESCE(c.Name, '') AS Name,
                   COALESCE(c.QtyOnHand, 0) AS QtyOnHand,
                   COALESCE(c.ReOrderLevel, 0) AS ReOrderLevel
            FROM dbo.TestTypeConsumable tc
            INNER JOIN dbo.Consumable c WITH (UPDLOCK, HOLDLOCK)
                ON c.ConsumableID = tc.ConsumableID
            WHERE tc.TestTypeID = @TestTypeID
            ORDER BY c.ConsumableID;
            """;

            var captureConsumables = (await connection
                .QueryAsync<ConsumableStockItem>(
                    new CommandDefinition(
                        captureConsumablesSql,
                        new { TestTypeID = testTypeId },
                        transaction,
                        cancellationToken: cancellationToken)))
                .AsList();

            if (captureConsumables.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return (false,
                    "Cannot save this result because no consumables are linked to this test type.",
                    0);
            }

            var outOfStock = captureConsumables
                .Where(c => c.QtyOnHand <= 0)
                .ToList();

            if (outOfStock.Count > 0)
            {
                var names = string.Join(", ", outOfStock.Select(c => c.Name));

                await transaction.RollbackAsync(cancellationToken);

                return (false,
                    $"Cannot save this result. Required consumables are out of stock: {names}.",
                    0);
            }

            var lowStockWarnings = new List<string>();

            // Application lock serializes capture/verification workflow for this request + test
            // across concurrent requests and application instances using this service.
            const string lockSql = """
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                SELECT @LockResult;
                """;

            int lockResult = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    lockSql,
                    new { Resource = $"LabTechWorkflow:{assignment.TestRequestID}:{testTypeId}" },
                    transaction,
                    cancellationToken: cancellationToken));

            if (lockResult < 0)
                throw new InvalidOperationException("Could not obtain the test workflow lock. Please retry.");

            const string rejectedSampleSql = """
                SELECT COUNT(1)
                FROM dbo.SampleReceiptRejection WITH (UPDLOCK, HOLDLOCK)
                WHERE SampleReceiptID = @SampleReceiptID;
                """;

            int rejectedSample = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    rejectedSampleSql,
                    new { SampleReceiptID = sampleReceiptId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (rejectedSample > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "This sample has been rejected. Receive a replacement sample for this request.", 0);
            }

            // Re-check the current state AFTER obtaining the lock.
            const string latestResultSql = """
                SELECT TOP (1)
                    tres.TestResultID,
                    latest.Decision
                FROM dbo.TestResult tres WITH (UPDLOCK, HOLDLOCK)
                OUTER APPLY
                (
                    SELECT TOP (1) rv.Decision
                    FROM dbo.ResultVerification rv
                    WHERE rv.TestResultID = tres.TestResultID
                    ORDER BY rv.VerificationDateTime DESC, rv.ResultVerificationID DESC
                ) latest
                WHERE tres.TestRequestID = @TestRequestID
                  AND tres.TestTypeID = @TestTypeID
                ORDER BY tres.CompletionDateTime DESC, tres.TestResultID DESC;
                """;

            var latest = await connection.QuerySingleOrDefaultAsync<dynamic>(
                new CommandDefinition(
                    latestResultSql,
                    new { TestRequestID = assignment.TestRequestID, TestTypeID = testTypeId },
                    transaction,
                    cancellationToken: cancellationToken));

            int? previousResultId = latest == null ? null : (int?)latest.TestResultID;
            string? latestDecision = latest == null ? null : (string?)latest.Decision;

            bool isReturned =
                string.Equals(latestDecision, "Returned", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(latestDecision, "Return", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(latestDecision, "Rejected", StringComparison.OrdinalIgnoreCase);

            if (previousResultId.HasValue && !isReturned)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false,
                    "This test already has a saved result. A new result is allowed only after the latest result is returned for recapture.",
                    0);
            }

            // If this is a recapture, require the predecessor's most recent decision to be
            // Returned/Return/Rejected and ensure no link already exists for that predecessor.
            if (previousResultId.HasValue)
            {
                const string existingLinkSql = """
                    SELECT COUNT(1)
                    FROM dbo.TestResultRecaptureLink WITH (UPDLOCK, HOLDLOCK)
                    WHERE PreviousTestResultID = @PreviousTestResultID;
                    """;

                int existingLink = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        existingLinkSql,
                        new { PreviousTestResultID = previousResultId.Value },
                        transaction,
                        cancellationToken: cancellationToken));

                if (existingLink > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "This returned result already has a recapture linked to it.", 0);
                }
            }

            const string rangeSql = """
                SELECT NormalRangeMin, NormalRangeMax
                FROM dbo.TestType WITH (HOLDLOCK)
                WHERE TestTypeID = @TestTypeID;
                """;

            var range = await connection.QuerySingleOrDefaultAsync<TestRangeRow>(
                new CommandDefinition(
                    rangeSql,
                    new { TestTypeID = testTypeId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (range == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "The selected test type could not be found.", 0);
            }

            bool isLow = range.NormalRangeMin.HasValue && numericResult < range.NormalRangeMin.Value;
            bool isHigh = range.NormalRangeMax.HasValue && numericResult > range.NormalRangeMax.Value;
            bool isAbnormal = isLow || isHigh;

            string status = isLow
                ? "Abnormally Low"
                : isHigh
                    ? "Abnormally High"
                    : "Within Normal Range";

            const string insertResultSql = """
                INSERT INTO dbo.TestResult
                (
                    Result, IsAbnormal, Status, CompletionDateTime,
                    TestTypeID, TestRequestID, TechnicianNotes
                )
                OUTPUT INSERTED.TestResultID
                VALUES
                (
                    @Result, @IsAbnormal, @Status, SYSDATETIME(),
                    @TestTypeID, @TestRequestID, @TechnicianNotes
                );
                """;

            int newResultId = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    insertResultSql,
                    new
                    {
                        Result = result.Trim(),
                        IsAbnormal = isAbnormal,
                        Status = status,
                        TestTypeID = testTypeId,
                        TestRequestID = assignment.TestRequestID,
                        TechnicianNotes = string.IsNullOrWhiteSpace(technicianNotes)
                            ? null
                            : technicianNotes.Trim()
                    },
                    transaction,
                    cancellationToken: cancellationToken));


            const string deductConsumableSql = """
                UPDATE dbo.Consumable
                SET QtyOnHand = QtyOnHand - 1
                WHERE ConsumableID = @ConsumableID
                  AND QtyOnHand > 0;
                """;

            foreach (var consumable in captureConsumables)
            {
                int affected = await connection.ExecuteAsync(
                    new CommandDefinition(
                        deductConsumableSql,
                        new { consumable.ConsumableID },
                        transaction,
                        cancellationToken: cancellationToken));

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Could not deduct stock for {consumable.Name}. The transaction will be rolled back.");
                }

                int remainingStock = consumable.QtyOnHand - 1;

                if (remainingStock <= consumable.ReOrderLevel)
                {
                    lowStockWarnings.Add(
                        $"{consumable.Name}: {remainingStock} remaining; reorder level {consumable.ReOrderLevel}");
                }
            }


            if (previousResultId.HasValue)
            {
                const string insertLinkSql = """
                    INSERT INTO dbo.TestResultRecaptureLink
                        (PreviousTestResultID, NewTestResultID)
                    VALUES
                        (@PreviousTestResultID, @NewTestResultID);
                    """;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertLinkSql,
                        new
                        {
                            PreviousTestResultID = previousResultId.Value,
                            NewTestResultID = newResultId
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }


            const string auditSql = """
                INSERT INTO dbo.TestResultAuditHistory
                    (TestResultID, EventType, LabTechnicianID, Details)
                VALUES
                    (@TestResultID, @EventType, @LabTechnicianID, @Details);
                """;


            await connection.ExecuteAsync(
                new CommandDefinition(
                    auditSql,
                    new
                    {
                        TestResultID = newResultId,
                        EventType = previousResultId.HasValue ? "Recaptured" : "Captured",
                        LabTechnicianID = labTechnicianId,
                        Details = previousResultId.HasValue
                            ? $"Recaptured {assignment.TestName}; previous result ID {previousResultId.Value} retained and linked."
                            : $"Captured {assignment.TestName}; result status: {status}."
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            CompletedTests[key] = new CompletedTest(
                newResultId,
                sampleReceiptId,
                assignment.TestRequestID,
                testTypeId,
                assignment.TestName,
                result.Trim(),
                status,
                DateTime.Now);


            string stockWarning = lowStockWarnings.Count > 0
                ? " Stock warning: " + string.Join("; ", lowStockWarnings) + "."
                : "";

            return (
                true,
                previousResultId.HasValue
                    ? $"Recaptured result saved for {assignment.TestName}: {status}. It is ready for verification.{stockWarning}"
                    : $"Result saved for {assignment.TestName}: {status}.{stockWarning}",
                newResultId);

        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            if (transaction.Connection != null)
                await transaction.RollbackAsync(CancellationToken.None);

            return (false, "A duplicate recapture link was detected. No partial result was saved; refresh the queue and try again if appropriate.", 0);
        }
        catch (Exception ex)
        {
            if (transaction.Connection != null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { /* Preserve the original failure. */ }
            }

            Console.Error.WriteLine($"SaveTestResultAsync failed: {ex}");
            return (false, "The result could not be saved. No partial result, link, or audit event should remain. Check application logs.", 0);
        }
    }
    public bool IsTestCompleted(int sampleReceiptId, int testTypeId)
    {
        return CompletedTests.ContainsKey($"{sampleReceiptId}:{testTypeId}");
    }

    public bool IsTestResultLinkedToRequest(
        int testResultId,
        int testRequestId)
    {
        return CompletedTests.Values.Any(
            x => x.TestResultID == testResultId &&
                 x.TestRequestID == testRequestId);
    }



    public async Task<IReadOnlyList<VerificationQueueItemViewModel>>

        // Load results awaiting verification and assemble their verification histories
        GetVerificationQueueAsync(
            CancellationToken cancellationToken = default)
    {
        const string sql = """
        SELECT
            tr.TestRequestID,
            sr.SampleReceiptID,
            tt.TestTypeID,
            p.PatientID,
            COALESCE(sr.Barcode, '') AS Barcode,
            LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name))) AS PatientName,
            COALESCE(u.IDNumber, '') AS PatientNumber,
            COALESCE(tt.TestName, '') AS TestName,
            COALESCE(tt.Units, '') AS Units,
            tt.NormalRangeMin,
            tt.NormalRangeMax,
            COALESCE(tr.Urgency, '') AS Urgency,
            COALESCE(tr.ClinicalNotes, '') AS DoctorNotes,
            COALESCE(tres.TechnicianNotes, '') AS TechnicianNotes,
            COALESCE(tres.Result, '') AS Result,
            COALESCE(tres.Status, '') AS ResultStatus,
            COALESCE(tres.IsAbnormal, 0) AS IsAbnormal,
            tres.CompletionDateTime,
            tres.TestResultID,
            CONCAT('REQ-', tr.TestRequestID) AS RequestNumber,
            COALESCE(latest.Decision, '') AS LatestVerificationDecision,
            COALESCE(latest.Reason, '') AS LatestVerificationReason,
            latest.VerificationDateTime AS LatestVerificationDateTime,
            'Unknown' AS CapturedBy
        FROM dbo.TestResult tres
        INNER JOIN dbo.TestRequest tr
            ON tr.TestRequestID = tres.TestRequestID
        CROSS APPLY
        (
            SELECT TOP (1) sr1.SampleReceiptID, sr1.Barcode, sr1.DateTimeReceived
            FROM dbo.SampleReceipt sr1
            WHERE sr1.TestRequestID = tr.TestRequestID
              AND (sr1.TestTypeID IS NULL OR sr1.TestTypeID = tres.TestTypeID)
            ORDER BY sr1.DateTimeReceived DESC, sr1.SampleReceiptID DESC
        ) sr
        INNER JOIN dbo.Patient p
            ON p.PatientID = tr.PatientID
        INNER JOIN dbo.[User] u
            ON u.UserID = p.UserID
        INNER JOIN dbo.TestType tt
            ON tt.TestTypeID = tres.TestTypeID
        OUTER APPLY
        (
            SELECT TOP (1)
                rv.Decision,
                rv.Reason,
                rv.VerificationDateTime
            FROM dbo.ResultVerification rv
            WHERE rv.TestResultID = tres.TestResultID
            ORDER BY rv.VerificationDateTime DESC,
                     rv.ResultVerificationID DESC
        ) latest
        WHERE tres.TestRequestID IS NOT NULL
        ORDER BY tres.CompletionDateTime DESC,
                 tres.TestResultID DESC;
        """;

        await using var connection = new SqlConnection(_connectionString);

        var items = (await connection.QueryAsync<VerificationQueueItemViewModel>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken)))
            .AsList();

        const string historySql = """
        SELECT
            COALESCE(rv.ResultVerificationID, 0) AS ResultVerificationID,
            COALESCE(rv.Decision, '') AS Decision,
            COALESCE(rv.Reason, '') AS Reason,
            rv.VerificationDateTime,
            COALESCE(
                NULLIF(LTRIM(RTRIM(CONCAT(usr.Surname, ', ', usr.Name))), ','),
                'Unknown'
            ) AS TechnicianName,
            related.TestResultID,
            related.TestRequestID,
            related.TestTypeID,
            COALESCE(related.Result, '') AS RelatedResultValue,
            COALESCE(related.TechnicianNotes, '') AS RelatedTechnicianNotes,
            related.CompletionDateTime AS RelatedCompletionDateTime
        FROM dbo.TestResult related
        LEFT JOIN dbo.ResultVerification rv
            ON rv.TestResultID = related.TestResultID
        LEFT JOIN dbo.LabTechnician lt
            ON lt.LabTechnicianID = rv.LabTechnicianID
        LEFT JOIN dbo.[User] usr
            ON usr.UserID = lt.UserID
        WHERE related.TestRequestID IS NOT NULL
        ORDER BY related.CompletionDateTime, related.TestResultID,
                 rv.VerificationDateTime, rv.ResultVerificationID;
        """;

        var historyRows = await connection.QueryAsync<VerificationHistoryItemViewModelWithResultId>(
            new CommandDefinition(historySql, cancellationToken: cancellationToken));

        var historiesByTest = historyRows
            .GroupBy(x => (x.TestRequestID, x.TestTypeID))
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => new VerificationHistoryItemViewModel
                {
                    ResultVerificationID = x.ResultVerificationID,
                    RelatedTestResultID = x.TestResultID,
                    RelatedResultValue = x.RelatedResultValue ?? string.Empty,
                    TechnicianNotes = x.RelatedTechnicianNotes ?? string.Empty,
                    Decision = x.Decision ?? string.Empty,
                    Reason = x.Reason ?? string.Empty,
                    TechnicianName = x.TechnicianName ?? "Unknown",
                    VerificationDateTime = x.VerificationDateTime,
                    RelatedCompletionDateTime = x.RelatedCompletionDateTime
                }).ToList());

        foreach (var item in items)
        {
            if (historiesByTest.TryGetValue((item.TestRequestID, item.TestTypeID), out var history))
            {
                item.VerificationHistory = history;
                item.HasBeenReturnedForRecapture = history.Any(x =>
                    string.Equals(x.Decision, "Returned", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Decision, "Rejected", StringComparison.OrdinalIgnoreCase));
            }
        }

        return items;
    }

    private sealed class VerificationHistoryItemViewModelWithResultId
    {
        public int TestResultID { get; set; }
        public int TestRequestID { get; set; }
        public int TestTypeID { get; set; }
        public int ResultVerificationID { get; set; }
        public string? RelatedResultValue { get; set; }
        public string? RelatedTechnicianNotes { get; set; }
        public string? Decision { get; set; }
        public string? Reason { get; set; }
        public string? TechnicianName { get; set; }
        public DateTime? VerificationDateTime { get; set; }
        public DateTime? RelatedCompletionDateTime { get; set; }
    }




    public async Task<VerificationQueueItemViewModel?>

        // Load an individual result with its complete verification history
        GetVerificationResultAsync(
            int testResultId,
            CancellationToken cancellationToken = default)
    {
        if (testResultId <= 0)
            return null;

        const string sql = """
        SELECT
            tr.TestRequestID,
            sr.SampleReceiptID,
            tt.TestTypeID,
            p.PatientID,
            COALESCE(sr.Barcode, '') AS Barcode,
            LTRIM(RTRIM(CONCAT(u.Surname, ', ', u.Name)))
                AS PatientName,
            COALESCE(u.IDNumber, '') AS PatientNumber,
            COALESCE(doctorUser.Email, '') AS DoctorEmail,
            COALESCE(tt.TestName, '') AS TestName,
            COALESCE(tt.Units, '') AS Units,
            tt.NormalRangeMin,
            tt.NormalRangeMax,
            COALESCE(tr.Urgency, '') AS Urgency,
            COALESCE(tr.ClinicalNotes, '') AS DoctorNotes,
            COALESCE(tres.TechnicianNotes, '') AS TechnicianNotes,
            COALESCE(tres.Result, '') AS Result,
            COALESCE(tres.Status, '') AS ResultStatus,
            COALESCE(tres.IsAbnormal, 0) AS IsAbnormal,
            tres.CompletionDateTime,
            tres.TestResultID,
            CONCAT('REQ-', tr.TestRequestID) AS RequestNumber,
            'Unknown' AS CapturedBy
        FROM dbo.TestResult tres
        INNER JOIN dbo.TestRequest tr
            ON tr.TestRequestID = tres.TestRequestID
        CROSS APPLY
        (
            SELECT TOP (1) sr1.SampleReceiptID, sr1.Barcode, sr1.DateTimeReceived
            FROM dbo.SampleReceipt sr1
            WHERE sr1.TestRequestID = tr.TestRequestID
              AND (sr1.TestTypeID IS NULL OR sr1.TestTypeID = tres.TestTypeID)
            ORDER BY sr1.DateTimeReceived DESC, sr1.SampleReceiptID DESC
        ) sr
        INNER JOIN dbo.Patient p
            ON p.PatientID = tr.PatientID
        INNER JOIN dbo.[User] u
            ON u.UserID = p.UserID
        LEFT JOIN dbo.Doctor d
            ON d.DoctorID = tr.DoctorID
        LEFT JOIN dbo.[User] doctorUser
            ON doctorUser.UserID = d.UserID
        INNER JOIN dbo.TestType tt
            ON tt.TestTypeID = tres.TestTypeID
        WHERE tres.TestResultID = @TestResultID;
        """;

        await using var connection =
            new SqlConnection(_connectionString);


        var result = await connection.QuerySingleOrDefaultAsync<
            VerificationQueueItemViewModel>(
            new CommandDefinition(
                sql,
                new { TestResultID = testResultId },
                cancellationToken: cancellationToken));

        if (result == null)
            return null;

        const string historySql = """
        SELECT
            COALESCE(rv.ResultVerificationID, 0) AS ResultVerificationID,
            related.TestResultID,
            COALESCE(rv.Decision, '') AS Decision,
            COALESCE(rv.Reason, '') AS Reason,
            rv.VerificationDateTime,
            COALESCE(
                NULLIF(LTRIM(RTRIM(CONCAT(usr.Surname, ', ', usr.Name))), ','),
                'Unknown'
            ) AS TechnicianName,
            related.TestRequestID,
            related.TestTypeID,
            COALESCE(related.Result, '') AS RelatedResultValue,
            COALESCE(related.TechnicianNotes, '') AS RelatedTechnicianNotes,
            related.CompletionDateTime AS RelatedCompletionDateTime
        FROM dbo.TestResult related
        LEFT JOIN dbo.ResultVerification rv
            ON rv.TestResultID = related.TestResultID
        LEFT JOIN dbo.LabTechnician lt
            ON lt.LabTechnicianID = rv.LabTechnicianID
        LEFT JOIN dbo.[User] usr
            ON usr.UserID = lt.UserID
        WHERE related.TestRequestID = @TestRequestID
          AND related.TestTypeID = @TestTypeID
        ORDER BY related.CompletionDateTime, related.TestResultID,
                 rv.VerificationDateTime, rv.ResultVerificationID;
        """;

        var history = await connection.QueryAsync<VerificationHistoryItemViewModelWithResultId>(
            new CommandDefinition(
                historySql,
                new { TestRequestID = result.TestRequestID, TestTypeID = result.TestTypeID },
                cancellationToken: cancellationToken));

        result.VerificationHistory = history.Select(x => new VerificationHistoryItemViewModel
        {
            ResultVerificationID = x.ResultVerificationID,
            RelatedTestResultID = x.TestResultID,
            RelatedResultValue = x.RelatedResultValue ?? string.Empty,
            TechnicianNotes = x.RelatedTechnicianNotes ?? string.Empty,
            Decision = x.Decision ?? string.Empty,
            Reason = x.Reason ?? string.Empty,
            TechnicianName = x.TechnicianName ?? "Unknown",
            VerificationDateTime = x.VerificationDateTime,
            RelatedCompletionDateTime = x.RelatedCompletionDateTime
        }).ToList();

        result.HasBeenReturnedForRecapture = result.VerificationHistory.Any(x =>
            string.Equals(x.Decision, "Returned",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Decision, "Rejected",
                StringComparison.OrdinalIgnoreCase));

        var latest = result.VerificationHistory
            .Where(x => x.RelatedTestResultID == result.TestResultID)
            .LastOrDefault();

        if (latest != null)
        {
            result.LatestVerificationDecision = latest.Decision;
            result.LatestVerificationReason = latest.Reason;
            result.LatestVerificationDateTime = latest.VerificationDateTime;
        }

        return result;

    }

    // Validate and save verification decisions while enforcing the independent-review workflow
    public async Task<(bool Success, string Message)> SaveVerificationDecisionAsync(
        int testResultId,
        int labTechnicianId,
        string decision,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (testResultId <= 0)
            return (false, "A valid test result is required.");

        if (labTechnicianId <= 0)
            return (false, "A valid lab technician must be identified before verification.");

        if (string.IsNullOrWhiteSpace(decision))
            return (false, "Select a verification decision.");

        string normalizedDecision = decision.Trim();
        if (!normalizedDecision.Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
            !normalizedDecision.Equals("Returned", StringComparison.OrdinalIgnoreCase))
            return (false, "The decision must be Approved or Returned.");

        if (normalizedDecision.Equals("Returned", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(reason))
            return (false, "A reason is required when returning a result for recapture.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Resolve workflow key, then lock it. The key is revalidated after the lock.
            const string keySql = """
                SELECT TestRequestID, TestTypeID
                FROM dbo.TestResult WITH (UPDLOCK, HOLDLOCK)
                WHERE TestResultID = @TestResultID;
                """;

            var key = await connection.QuerySingleOrDefaultAsync<dynamic>(
                new CommandDefinition(
                    keySql,
                    new { TestResultID = testResultId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (key == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "The test result was not found. No decision was saved.");
            }

            int requestId = (int)key.TestRequestID;
            int typeId = (int)key.TestTypeID;

            const string lockSql = """
                DECLARE @LockResult INT;
                EXEC @LockResult = sys.sp_getapplock
                    @Resource = @Resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                SELECT @LockResult;
                """;

            int lockResult = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    lockSql,
                    new { Resource = $"LabTechWorkflow:{requestId}:{typeId}" },
                    transaction,
                    cancellationToken: cancellationToken));

            if (lockResult < 0)
                throw new InvalidOperationException("Could not obtain the test workflow lock. Please retry.");

            const string technicianSql = """
                SELECT COUNT(1)
                FROM dbo.LabTechnician WITH (UPDLOCK, HOLDLOCK)
                WHERE LabTechnicianID = @LabTechnicianID;
                """;

            int technicianExists = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    technicianSql,
                    new { LabTechnicianID = labTechnicianId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (technicianExists == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "The lab technician record was not found. No decision was saved.");
            }

            const string latestResultSql = """
                SELECT TOP (1) tres.TestResultID
                FROM dbo.TestResult tres WITH (UPDLOCK, HOLDLOCK)
                WHERE tres.TestRequestID = @TestRequestID
                  AND tres.TestTypeID = @TestTypeID
                ORDER BY tres.CompletionDateTime DESC, tres.TestResultID DESC;
                """;

            int? latestResultId = await connection.QuerySingleOrDefaultAsync<int?>(
                new CommandDefinition(
                    latestResultSql,
                    new { TestRequestID = requestId, TestTypeID = typeId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (latestResultId != testResultId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "This is no longer the latest result for this test. Refresh the verification queue before deciding.");
            }


            const string captureTechnicianSql = """
                SELECT TOP (1) LabTechnicianID
                FROM dbo.TestResultAuditHistory WITH (UPDLOCK, HOLDLOCK)
                WHERE TestResultID = @TestResultID
                  AND EventType IN ('Captured', 'Recaptured')
                ORDER BY EventDateTime DESC, AuditHistoryID DESC;
                """;

            int? captureTechnicianId =
                await connection.QuerySingleOrDefaultAsync<int?>(
                    new CommandDefinition(
                        captureTechnicianSql,
                        new { TestResultID = testResultId },
                        transaction,
                        cancellationToken: cancellationToken));

            if (captureTechnicianId.HasValue &&
                captureTechnicianId.Value == labTechnicianId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false,
                    "You cannot verify a result that you captured. Another lab technician must verify it.");
            }


            const string existingDecisionSql = """
                SELECT COUNT(1)
                FROM dbo.ResultVerification WITH (UPDLOCK, HOLDLOCK)
                WHERE TestResultID = @TestResultID;
                """;

            int existingDecisionCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    existingDecisionSql,
                    new { TestResultID = testResultId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (existingDecisionCount > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "A verification decision already exists for this result. It cannot be decided again.");
            }

            const string insertDecisionSql = """
                INSERT INTO dbo.ResultVerification
                    (VerificationDateTime, Decision, TestResultID, LabTechnicianID, Reason)
                VALUES
                    (SYSDATETIME(), @Decision, @TestResultID, @LabTechnicianID, @Reason);
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertDecisionSql,
                    new
                    {
                        Decision = normalizedDecision,
                        TestResultID = testResultId,
                        LabTechnicianID = labTechnicianId,
                        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            const string auditSql = """
                INSERT INTO dbo.TestResultAuditHistory
                    (TestResultID, EventType, LabTechnicianID, Details)
                VALUES
                    (@TestResultID, @EventType, @LabTechnicianID, @Details);
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    auditSql,
                    new
                    {
                        TestResultID = testResultId,
                        EventType = normalizedDecision,
                        LabTechnicianID = labTechnicianId,
                        Details = string.IsNullOrWhiteSpace(reason)
                            ? $"Verification decision: {normalizedDecision}."
                            : $"Verification decision: {normalizedDecision}. Reason: {reason.Trim()}"
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return (true, $"Verification decision '{normalizedDecision}' was saved.");
        }
        catch (Exception ex)
        {
            if (transaction.Connection != null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch { /* Preserve the original failure. */ }
            }

            Console.Error.WriteLine($"SaveVerificationDecisionAsync failed: {ex}");
            return (false, "The verification decision could not be saved. No partial decision/audit record should remain. Check application logs.");
        }
    }
    public async Task<int?> GetLabTechnicianIdForUserAsync(
    int userId,
    CancellationToken cancellationToken = default)
    {
        const string sql = @"
        SELECT TOP (1) LabTechnicianID
        FROM dbo.LabTechnician
        WHERE UserID = @UserID;";

        await using var connection = new SqlConnection(_connectionString);

        return await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                sql,
                new { UserID = userId },
                cancellationToken: cancellationToken));
    }

    public async Task RecordAuditEventAsync(
    int testResultId,
    string eventType,
    int? labTechnicianId = null,
    string? details = null,
    CancellationToken cancellationToken = default)
    {
        if (testResultId <= 0)
            throw new ArgumentOutOfRangeException(nameof(testResultId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException(
                "An audit event type is required.",
                nameof(eventType));

        const string sql = """
        INSERT INTO dbo.TestResultAuditHistory
        (
            TestResultID,
            EventType,
            LabTechnicianID,
            Details
        )
        VALUES
        (
            @TestResultID,
            @EventType,
            @LabTechnicianID,
            @Details
        );
        """;

        await using var connection = new SqlConnection(_connectionString);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    TestResultID = testResultId,
                    EventType = eventType.Trim(),
                    LabTechnicianID = labTechnicianId,
                    Details = string.IsNullOrWhiteSpace(details)
                        ? null
                        : details.Trim()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyDictionary<int, List<ConsumableStockItem>>>
        GetConsumablesByTestTypeAsync(
            CancellationToken cancellationToken = default)
    {
        const string sql = """
        SELECT
            tc.TestTypeID,
            c.ConsumableID,
            COALESCE(c.Name, '') AS Name,
            COALESCE(c.QtyOnHand, 0) AS QtyOnHand,
            COALESCE(c.ReOrderLevel, 0) AS ReOrderLevel
        FROM dbo.TestTypeConsumable tc
        INNER JOIN dbo.Consumable c
            ON c.ConsumableID = tc.ConsumableID
        ORDER BY tc.TestTypeID, c.Name;
        """;

        await using var connection = new SqlConnection(_connectionString);

        var rows = (await connection.QueryAsync<
            (int TestTypeID, int ConsumableID, string Name,
             int QtyOnHand, int ReOrderLevel)>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken)))
            .ToList();

        return rows
            .GroupBy(x => x.TestTypeID)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => new ConsumableStockItem
                {
                    ConsumableID = x.ConsumableID,
                    Name = x.Name,
                    QtyOnHand = x.QtyOnHand,
                    ReOrderLevel = x.ReOrderLevel
                }).ToList());
    }

    public async Task<PatientMedicalInfoViewModel> GetPatientMedicalInfoAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        var result = new PatientMedicalInfoViewModel();

        if (patientId <= 0)
            return result;

        const string conditionsSql = """
        SELECT
            mc.MedicalCondition AS [Condition],
            COALESCE(pmc.Chronic, 0) AS Chronic,
            COALESCE(pmc.Severity, '') AS Severity,
            COALESCE(pmc.Description, '') AS Description
        FROM dbo.PatientMedicalCondition pmc
        INNER JOIN dbo.MedicalCondition mc
            ON mc.MedicalConditionID = pmc.MedicalConditionID
        WHERE pmc.PatientID = @PatientID
        ORDER BY mc.MedicalCondition;
        """;

        const string allergiesSql = """
        SELECT
            a.Allergy,
            COALESCE(pa.Reaction, '') AS Reaction,
            COALESCE(pa.Severity, '') AS Severity,
            COALESCE(pa.Emergency, 0) AS Emergency
        FROM dbo.PatientAllergies pa
        INNER JOIN dbo.Allergies a
            ON a.AllergiesID = pa.AllergiesID
        WHERE pa.PatientID = @PatientID
        ORDER BY a.Allergy;
        """;

        await using var connection = new SqlConnection(_connectionString);

        var conditions = await connection.QueryAsync<PatientConditionInfo>(
            new CommandDefinition(
                conditionsSql,
                new { PatientID = patientId },
                cancellationToken: cancellationToken));

        var allergies = await connection.QueryAsync<PatientAllergyInfo>(
            new CommandDefinition(
                allergiesSql,
                new { PatientID = patientId },
                cancellationToken: cancellationToken));

        result.Conditions = conditions.AsList();
        result.Allergies = allergies.AsList();

        return result;
    }

}