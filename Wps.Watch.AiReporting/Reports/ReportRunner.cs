using System.Diagnostics;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// Single entry point for executing reports. Validates auth + parameters,
/// dispatches to the matching <see cref="IReportDefinition"/>, and records an
/// audit log entry. Used by both the REST controller and the MCP tool handler.
/// </summary>
public sealed class ReportRunner
{
    private readonly IReportCatalog _catalog;
    private readonly WpsReplicaDbContext _db;
    private readonly IUserContextAccessor _user;
    private readonly ILogger<ReportRunner> _logger;

    public ReportRunner(
        IReportCatalog catalog,
        WpsReplicaDbContext db,
        IUserContextAccessor user,
        ILogger<ReportRunner> logger)
    {
        _catalog = catalog;
        _db = db;
        _user = user;
        _logger = logger;
    }

    public async Task<ReportRunOutcome> RunAsync(
        string reportId,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        parameters ??= new Dictionary<string, object?>();

        var user = _user.Current;
        if (user is null)
        {
            return new ReportRunOutcome.Unauthenticated();
        }

        var report = _catalog.Find(reportId);
        if (report is null)
        {
            return new ReportRunOutcome.NotFound(reportId);
        }

        if (!user.Can(report.Operation))
        {
            _logger.LogWarning(
                "Report {ReportId} requested by user {UserId} but operation {Operation} not granted",
                reportId, user.UserId, report.Operation);
            return new ReportRunOutcome.Forbidden(reportId);
        }

        var errors = ValidateParameters(report, parameters);
        if (errors.Count > 0)
        {
            return new ReportRunOutcome.InvalidParameters(reportId, errors);
        }

        var sw = Stopwatch.StartNew();
        ReportResult data;
        try
        {
            data = await report.RunAsync(_db, user, parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Report {ReportId} threw for user {UserId} after {ElapsedMs}ms",
                reportId, user.UserId, sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        _logger.LogInformation(
            "Report {ReportId} ran for user {UserId} in {ElapsedMs}ms, returned {RowCount} rows",
            reportId, user.UserId, sw.ElapsedMilliseconds, data.Rows.Count);

        return new ReportRunOutcome.Success(data, sw.Elapsed);
    }

    private static List<string> ValidateParameters(
        IReportDefinition report,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var errors = new List<string>();
        foreach (var p in report.Parameters)
        {
            var present = parameters.TryGetValue(p.Name, out var value) && value is not null;
            if (p.Required && !present)
            {
                errors.Add($"Missing required parameter '{p.Name}'.");
            }
        }
        return errors;
    }
}
