using System.ComponentModel;
using ModelContextProtocol.Server;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Reports;

namespace Wps.Watch.AiReporting.Mcp;

/// <summary>
/// MCP tool surface for AI Reports. Exposes the same canned-report catalog as
/// the REST controller, via the same <see cref="ReportRunner"/> handler.
/// </summary>
[McpServerToolType]
public sealed class ReportTools
{
    private readonly IReportCatalog _catalog;
    private readonly ReportRunner _runner;
    private readonly IUserContextAccessor _user;

    public ReportTools(
        IReportCatalog catalog,
        ReportRunner runner,
        IUserContextAccessor user)
    {
        _catalog = catalog;
        _runner = runner;
        _user = user;
    }

    [McpServerTool(
        Name = "list_canned_reports",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("List the canned reports visible to the current user, with metadata about each one's parameters and cost class.")]
    public object ListCannedReports()
    {
        var user = _user.Current
            ?? throw new InvalidOperationException("No user context resolved.");

        return _catalog.VisibleTo(user).Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            operation = r.Operation,
            expectedCostClass = r.ExpectedCostClass,
            parameters = r.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type.ToString().ToLowerInvariant(),
                required = p.Required,
                description = p.Description,
                @default = p.Default,
            }).ToList(),
        }).ToList();
    }

    [McpServerTool(
        Name = "run_canned_report",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Run a canned report by id with optional parameters. Returns the executed rows along with timing and audit metadata. Use list_canned_reports first to discover available ids and their parameter schemas. All reports are SELECT-only against the wpsWatch database mirror — no writes.")]
    public async Task<object> RunCannedReport(
        [Description("Stable id of the report to run (kebab-case, from list_canned_reports).")]
        string reportId,
        [Description("Map of parameter name to value. Omit (or pass null) for reports that take no parameters.")]
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _runner.RunAsync(reportId, parameters, cancellationToken);

        return outcome switch
        {
            ReportRunOutcome.Success s => new
            {
                status = "ok",
                reportId = s.Data.ReportId,
                executedAtUtc = s.Data.ExecutedAtUtc,
                durationMs = (int)s.Duration.TotalMilliseconds,
                columns = s.Data.Columns,
                rowCount = s.Data.Rows.Count,
                rows = s.Data.Rows,
                parametersApplied = s.Data.ParametersApplied,
            },
            ReportRunOutcome.NotFound nf => new
            {
                status = "error",
                error = "report_not_found",
                reportId = nf.ReportId,
            },
            ReportRunOutcome.Forbidden f => new
            {
                status = "error",
                error = "forbidden",
                reportId = f.ReportId,
            },
            ReportRunOutcome.InvalidParameters ip => new
            {
                status = "error",
                error = "invalid_parameters",
                reportId = ip.ReportId,
                errors = ip.Errors,
            },
            ReportRunOutcome.Unauthenticated => new
            {
                status = "error",
                error = "unauthenticated",
            },
            _ => new
            {
                status = "error",
                error = "unknown",
            },
        };
    }
}
