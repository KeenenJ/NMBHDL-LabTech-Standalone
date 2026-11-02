#nullable enable
using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace NMBHLabSys.LabManagerSubsystem.Data;

/// <summary>
/// Hands out database connections for the Lab Manager repositories.
/// It has its own name (not IDbConnectionFactory) so it can't clash with a
/// connection factory the other subsystems register.
/// </summary>
public interface ILabManagerDb
{
    DbConnection CreateConnection();
}

public class LabManagerDb : ILabManagerDb
{
    private readonly string _connectionString;

    public LabManagerDb(IConfiguration configuration, IOptions<LabManagerOptions> options)
    {
        string name = options.Value.ConnectionStringName;

        _connectionString =
            configuration.GetConnectionString(name)
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                $"No connection string found. Add \"{name}\" (or \"DefaultConnection\") " +
                "under ConnectionStrings in appsettings.json.");
    }

    // A new connection for every repository call. Dapper opens and closes it
    // for single queries; methods that need a transaction open it themselves.
    public DbConnection CreateConnection() => new SqlConnection(_connectionString);
}

/// <summary>Turns SQL Server error numbers into readable checks.</summary>
public static class LabManagerSqlErrors
{
    /// <summary>2627 = UNIQUE / PRIMARY KEY constraint, 2601 = unique index.</summary>
    public static bool IsUniqueViolation(Exception ex) =>
        ex is SqlException sql && (sql.Number == 2627 || sql.Number == 2601);

    /// <summary>547 = a FOREIGN KEY (or CHECK) constraint stopped the statement.</summary>
    public static bool IsReferenceViolation(Exception ex) =>
        ex is SqlException sql && sql.Number == 547;
}
