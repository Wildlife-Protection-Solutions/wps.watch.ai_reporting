using System.Text;
using Microsoft.AspNetCore.Mvc;
using Wps.Watch.AiReporting.Discovery;

namespace Wps.Watch.AiReporting.Controllers;

/// <summary>
/// REST surface for free-form data discovery (Rung 1: internal team, devqa).
/// Mirrors the MCP <see cref="Mcp.DiscoveryTools"/> so the internal team can also
/// curl/download results. <c>POST /api/discovery/query?format=csv</c> returns a
/// downloadable CSV (UTF-8 with BOM) for verification.
/// </summary>
[ApiController]
[Route("api/discovery")]
public sealed class DiscoveryController : ControllerBase
{
    private readonly DiscoveryQueryRunner _runner;

    public DiscoveryController(DiscoveryQueryRunner runner)
    {
        _runner = runner;
    }

    [HttpGet("schema")]
    public async Task<IActionResult> Schema(CancellationToken cancellationToken)
    {
        var outcome = await _runner.DescribeSchemaAsync(cancellationToken);
        return outcome switch
        {
            DiscoverySchemaOutcome.Success s => Ok(new
            {
                tableCount = s.Tables.Count,
                tables = s.Tables.Select(t => new { name = t.Name, columns = t.Columns }),
            }),
            DiscoverySchemaOutcome.Forbidden => StatusCode(403, new { error = "forbidden" }),
            DiscoverySchemaOutcome.Unauthenticated => Unauthorized(),
            DiscoverySchemaOutcome.Disabled => StatusCode(503, new { error = "discovery_disabled" }),
            _ => StatusCode(500),
        };
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query(
        [FromBody] DiscoveryQueryRequest? request,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var outcome = await _runner.RunQueryAsync(request?.Sql, cancellationToken);
        var wantsCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

        return outcome switch
        {
            DiscoveryQueryOutcome.Success s when wantsCsv => CsvFile(s),
            DiscoveryQueryOutcome.Success s => Ok(new
            {
                rowCount = s.RowCount,
                truncated = s.Truncated,
                durationMs = (int)s.Duration.TotalMilliseconds,
                columns = s.Columns,
                rows = s.Rows,
                executedSql = s.ExecutedSql,
            }),
            DiscoveryQueryOutcome.Rejected r => BadRequest(new { error = "rejected", reason = r.Reason }),
            DiscoveryQueryOutcome.Failed f => BadRequest(new { error = "query_failed", reason = f.Reason }),
            DiscoveryQueryOutcome.Forbidden => StatusCode(403, new { error = "forbidden" }),
            DiscoveryQueryOutcome.Unauthenticated => Unauthorized(),
            DiscoveryQueryOutcome.Disabled => StatusCode(503, new { error = "discovery_disabled" }),
            _ => StatusCode(500),
        };
    }

    private FileContentResult CsvFile(DiscoveryQueryOutcome.Success s)
    {
        var csv = CsvExporter.ToCsv(s.Columns, s.Rows);
        // Prepend a UTF-8 BOM so Excel renders non-ASCII names (e.g. Afrikaans) correctly.
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv))
            .ToArray();
        return File(bytes, "text/csv", "discovery-result.csv");
    }

    public sealed record DiscoveryQueryRequest(string? Sql);
}
