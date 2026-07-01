using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Discovery;

/// <summary>
/// Executes free-form, read-only discovery queries and describes the queryable
/// schema. Single entry point for both the REST controller and the MCP tools, so
/// the auth gate, SQL guard, row cap, sensitive-column filter, and audit log all
/// live in one place (mirrors <see cref="Reports.ReportRunner"/>).
///
/// Rung 1 only: internal team, devqa. The real write-prevention guarantee at
/// later rungs is a read-only DB principal; here we layer SqlGuard (SELECT-only)
/// plus an always-rolled-back transaction as belt-and-suspenders.
/// </summary>
public sealed class DiscoveryQueryRunner
{
    private readonly WpsReplicaDbContext _db;
    private readonly IUserContextAccessor _user;
    private readonly DiscoveryOptions _options;
    private readonly ILogger<DiscoveryQueryRunner> _logger;

    public DiscoveryQueryRunner(
        WpsReplicaDbContext db,
        IUserContextAccessor user,
        IOptions<DiscoveryOptions> options,
        ILogger<DiscoveryQueryRunner> logger)
    {
        _db = db;
        _user = user;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DiscoveryQueryOutcome> RunQueryAsync(string? sql, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new DiscoveryQueryOutcome.Disabled();
        }

        var user = _user.Current;
        if (user is null)
        {
            return new DiscoveryQueryOutcome.Unauthenticated();
        }

        if (!user.Can(ReportOperations.DiscoveryQuery))
        {
            _logger.LogWarning("Discovery query denied for user {UserId} (operation not granted)", user.UserId);
            return new DiscoveryQueryOutcome.Forbidden();
        }

        var validation = SqlGuard.Validate(sql);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "Discovery query rejected for user {UserId}: {Reason}. SQL: {Sql}",
                user.UserId, validation.Reason, sql);
            return new DiscoveryQueryOutcome.Rejected(validation.Reason!);
        }

        var query = validation.NormalizedSql!;
        var sw = Stopwatch.StartNew();

        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        var columns = new List<string>();
        var keptOrdinals = new List<int>();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var truncated = false;

        try
        {
            if (openedHere)
            {
                await connection.OpenAsync(cancellationToken);
            }

            // Read-committed transaction we always roll back: nothing should write,
            // but if anything slips the guard it cannot persist.
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            // The reader must be fully read AND closed before the transaction is
            // rolled back. Without MARS, SqlClient refuses to issue any command —
            // including the rollback — while a DataReader is still open on the
            // connection ("There is already an open DataReader associated with this
            // Connection which must be closed first."). Scoping the command + reader
            // in their own block guarantees they are disposed before RollbackAsync.
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = query;
                cmd.CommandTimeout = _options.CommandTimeoutSeconds;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    if (SqlGuard.IsSensitiveColumn(name))
                    {
                        continue; // never surface secret columns, even if explicitly selected
                    }
                    columns.Add(name);
                    keptOrdinals.Add(i);
                }

                while (await reader.ReadAsync(cancellationToken))
                {
                    if (rows.Count >= _options.MaxRows)
                    {
                        truncated = true;
                        break;
                    }

                    var dict = new Dictionary<string, object?>(keptOrdinals.Count);
                    foreach (var ord in keptOrdinals)
                    {
                        dict[reader.GetName(ord)] = reader.IsDBNull(ord) ? null : reader.GetValue(ord);
                    }
                    rows.Add(dict);
                }
            }

            // Command and reader are now disposed; the connection is idle and the
            // belt-and-suspenders rollback can run cleanly.
            await tx.RollbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Surface ANY execution failure through the tool's structured
            // {status:"error", error:"query_failed", reason:…} shape. Previously only
            // DbException was caught, so non-DbException failures (e.g. an open-reader
            // InvalidOperationException, type-mapping errors) escaped as the opaque
            // "An error occurred invoking 'query_data'" message with no actionable reason.
            _logger.LogWarning(ex, "Discovery query failed to execute for user {UserId}. SQL: {Sql}", user.UserId, query);
            return new DiscoveryQueryOutcome.Failed(ex.Message);
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Discovery query by {UserId} returned {RowCount} rows ({Completeness}) in {ElapsedMs}ms. SQL: {Sql}",
            user.UserId, rows.Count, truncated ? "truncated" : "complete", sw.ElapsedMilliseconds, query);

        return new DiscoveryQueryOutcome.Success(columns, rows, rows.Count, truncated, sw.Elapsed, query);
    }

    public async Task<DiscoverySchemaOutcome> DescribeSchemaAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new DiscoverySchemaOutcome.Disabled();
        }

        var user = _user.Current;
        if (user is null)
        {
            return new DiscoverySchemaOutcome.Unauthenticated();
        }

        if (!user.Can(ReportOperations.DiscoveryQuery))
        {
            return new DiscoverySchemaOutcome.Forbidden();
        }

        const string sql = @"
SELECT c.TABLE_NAME  AS TableName,
       c.COLUMN_NAME AS ColumnName,
       c.DATA_TYPE   AS DataType,
       c.IS_NULLABLE AS IsNullable
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t
  ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
WHERE t.TABLE_TYPE = 'BASE TABLE'
  AND c.TABLE_SCHEMA = 'dbo'
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;";

        var raw = await _db.Database.SqlQueryRaw<SchemaColumnRow>(sql).ToListAsync(cancellationToken);

        var tables = raw
            .Where(r => !SqlGuard.IsSensitiveTable(r.TableName))
            .Where(r => !SqlGuard.IsSensitiveColumn(r.ColumnName))
            .GroupBy(r => r.TableName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SchemaTable(
                g.Key,
                g.Select(c => new SchemaColumn(
                        c.ColumnName,
                        c.DataType,
                        string.Equals(c.IsNullable, "YES", StringComparison.OrdinalIgnoreCase)))
                    .ToList()))
            .ToList();

        _logger.LogInformation(
            "Discovery schema described for user {UserId}: {TableCount} tables", user.UserId, tables.Count);

        return new DiscoverySchemaOutcome.Success(tables);
    }
}
