using System.ComponentModel;
using ModelContextProtocol.Server;
using Wps.Watch.AiReporting.Discovery;

namespace Wps.Watch.AiReporting.Mcp;

/// <summary>
/// MCP tool surface for free-form data discovery (Rung 1: internal team, devqa).
/// Complements the canned-report tools in <see cref="ReportTools"/>: where those
/// run a fixed catalog, these let a trusted operator ask the data anything via a
/// read-only SELECT, with results downloadable for verification. All access goes
/// through <see cref="DiscoveryQueryRunner"/> (auth gate + SQL guard + row cap +
/// sensitive-column filter + audit log).
/// </summary>
[McpServerToolType]
public sealed class DiscoveryTools
{
    private readonly DiscoveryQueryRunner _runner;

    public DiscoveryTools(DiscoveryQueryRunner runner)
    {
        _runner = runner;
    }

    [McpServerTool(
        Name = "describe_schema",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "List the queryable tables and their columns in the wpsWatch database mirror. " +
        "Identity/credential tables and sensitive columns (tokens, password hashes, API keys, " +
        "webhook URLs) are excluded. Call this first to learn what query_data can SELECT from.")]
    public async Task<object> DescribeSchema(CancellationToken cancellationToken = default)
    {
        var outcome = await _runner.DescribeSchemaAsync(cancellationToken);
        return outcome switch
        {
            DiscoverySchemaOutcome.Success s => new
            {
                status = "ok",
                tableCount = s.Tables.Count,
                tables = s.Tables.Select(t => new
                {
                    name = t.Name,
                    columns = t.Columns.Select(c => new
                    {
                        name = c.Name,
                        type = c.DataType,
                        nullable = c.Nullable,
                    }),
                }),
            },
            DiscoverySchemaOutcome.Forbidden => Error("forbidden"),
            DiscoverySchemaOutcome.Unauthenticated => Error("unauthenticated"),
            DiscoverySchemaOutcome.Disabled => Error("discovery_disabled"),
            _ => Error("unknown"),
        };
    }

    [McpServerTool(
        Name = "query_data",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "Run a single read-only SELECT (or WITH … SELECT) against the wpsWatch database mirror " +
        "and return the rows. No writes, no multiple statements, no stored procedures. Results are " +
        "capped (default 5000 rows) and sensitive columns are stripped from the output. Call " +
        "describe_schema first to discover tables and columns. Intended for internal discovery " +
        "against test data.")]
    public async Task<object> QueryData(
        [Description("A single read-only T-SQL SELECT statement.")]
        string sql,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _runner.RunQueryAsync(sql, cancellationToken);
        return outcome switch
        {
            DiscoveryQueryOutcome.Success s => new
            {
                status = "ok",
                rowCount = s.RowCount,
                truncated = s.Truncated,
                durationMs = (int)s.Duration.TotalMilliseconds,
                columns = s.Columns,
                rows = s.Rows,
                executedSql = s.ExecutedSql,
            },
            DiscoveryQueryOutcome.Rejected r => new { status = "error", error = "rejected", reason = r.Reason },
            DiscoveryQueryOutcome.Failed f => new { status = "error", error = "query_failed", reason = f.Reason },
            DiscoveryQueryOutcome.Forbidden => Error("forbidden"),
            DiscoveryQueryOutcome.Unauthenticated => Error("unauthenticated"),
            DiscoveryQueryOutcome.Disabled => Error("discovery_disabled"),
            _ => Error("unknown"),
        };
    }

    private static object Error(string code) => new { status = "error", error = code };
}
