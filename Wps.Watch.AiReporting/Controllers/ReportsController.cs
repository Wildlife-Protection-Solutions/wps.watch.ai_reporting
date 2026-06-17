using Microsoft.AspNetCore.Mvc;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Reports;

namespace Wps.Watch.AiReporting.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportCatalog _catalog;
    private readonly ReportRunner _runner;
    private readonly IUserContextAccessor _user;

    public ReportsController(
        IReportCatalog catalog,
        ReportRunner runner,
        IUserContextAccessor user)
    {
        _catalog = catalog;
        _runner = runner;
        _user = user;
    }

    /// <summary>
    /// Reports visible to the calling user.
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var user = _user.Current;
        if (user is null)
        {
            return Unauthorized();
        }

        var reports = _catalog.VisibleTo(user)
            .Select(r => new
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
                }),
            });

        return Ok(reports);
    }

    /// <summary>
    /// Execute a report by id. Body is a JSON object of parameter name → value;
    /// empty object is allowed for reports without parameters.
    /// </summary>
    [HttpPost("{id}/run")]
    public async Task<IActionResult> Run(
        string id,
        [FromBody] Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var outcome = await _runner.RunAsync(id, parameters, cancellationToken);

        return outcome switch
        {
            ReportRunOutcome.Success s => Ok(new
            {
                reportId = s.Data.ReportId,
                executedAtUtc = s.Data.ExecutedAtUtc,
                durationMs = (int)s.Duration.TotalMilliseconds,
                columns = s.Data.Columns,
                rowCount = s.Data.Rows.Count,
                rows = s.Data.Rows,
                parametersApplied = s.Data.ParametersApplied,
            }),
            ReportRunOutcome.NotFound nf => NotFound(new { error = "report_not_found", reportId = nf.ReportId }),
            ReportRunOutcome.Forbidden f => StatusCode(403, new { error = "forbidden", reportId = f.ReportId }),
            ReportRunOutcome.InvalidParameters ip => BadRequest(new { error = "invalid_parameters", reportId = ip.ReportId, errors = ip.Errors }),
            ReportRunOutcome.Unauthenticated => Unauthorized(),
            _ => StatusCode(500),
        };
    }
}
