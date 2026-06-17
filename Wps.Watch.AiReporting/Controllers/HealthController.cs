using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly WpsReplicaDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(WpsReplicaDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpGet("db")]
    public async Task<IActionResult> Db(CancellationToken cancellationToken)
    {
        try
        {
            var serverVersion = await _db.Database
                .SqlQuery<string>($"SELECT CAST(@@VERSION AS NVARCHAR(MAX)) AS Value")
                .FirstAsync(cancellationToken);

            return Ok(new
            {
                status = "ok",
                database = _db.Database.GetDbConnection().Database,
                dataSource = _db.Database.GetDbConnection().DataSource,
                serverVersion = serverVersion.Split('\n')[0].Trim(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DB health check failed");
            return StatusCode(503, new
            {
                status = "error",
                error = ex.Message,
                exceptionType = ex.GetType().FullName,
            });
        }
    }
}
