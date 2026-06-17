using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Deployment Health Status — the v5 replacement for the original
/// "Top 10 Offline" report. Tiers every active deployment as
/// online / warning / offline based on how long since its last event.
///
/// Defaults match the dual-threshold model from the v3 exploration notes:
///   • offline: no event in &gt;= 24h (1440 min)
///   • warning: no event in &gt;= 12h but &lt; 24h
///   • online:  event within the last 12h
/// </summary>
internal sealed class DeploymentHealthStatusReport : IReportDefinition
{
    public string Id => "deployment-health-status";
    public string Name => "Deployment Health Status";
    public string Description =>
        "All active camera deployments tiered as online / warning / offline by minutes " +
        "since last event. Replaces the legacy 'Top 10 Offline' report with a tiered view.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => new[]
    {
        new ReportParameter("offlineHours", ReportParameterType.Integer, Required: false,
            Description: "Hours of silence before a deployment is classified offline (default 24).",
            Default: 24),
        new ReportParameter("warningHours", ReportParameterType.Integer, Required: false,
            Description: "Hours of silence before a deployment is classified warning (default 12).",
            Default: 12),
    };
    public string ExpectedCostClass => "fast";

    public async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var offlineHours = ParseInt(parameters, "offlineHours") ?? 24;
        var warningHours = ParseInt(parameters, "warningHours") ?? 12;
        if (warningHours >= offlineHours)
        {
            throw new ArgumentException(
                $"warningHours ({warningHours}) must be less than offlineHours ({offlineHours}).");
        }

        var offlineMinutes = offlineHours * 60;
        var warningMinutes = warningHours * 60;

        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        var sql = $@"
SELECT
    o.OrganizationId,
    o.[Name]            AS OrganizationName,
    s.SiteId,
    s.[Name]            AS SiteName,
    d.DeploymentId,
    d.[Name]            AS DeploymentName,
    de.DeviceId,
    de.[Name]           AS DeviceName,
    d.LastEventDateTimeUtc,
    DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) AS MinutesSinceLastEvent,
    CASE
        WHEN d.LastEventDateTimeUtc IS NULL THEN 'offline'
        WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) >= @offlineMinutes THEN 'offline'
        WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) >= @warningMinutes THEN 'warning'
        ELSE 'online'
    END AS Status
FROM dbo.Deployment d
JOIN dbo.Device de      ON de.DeviceId = d.DeviceId
JOIN dbo.Site s         ON s.SiteId = d.SiteId AND s.IsActive = 1
JOIN dbo.Organization o ON o.OrganizationId = s.OrganizationId
WHERE d.EndDateTimeUtc IS NULL
  AND de.DeviceTypeId = 1
  AND de.DecommissionDate IS NULL
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
ORDER BY
    CASE
        WHEN d.LastEventDateTimeUtc IS NULL THEN 0
        WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) >= @offlineMinutes THEN 1
        WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) >= @warningMinutes THEN 2
        ELSE 3
    END,
    MinutesSinceLastEvent DESC;
";

        var rows = await db.Database
            .SqlQueryRaw<DeploymentHealthRow>(sql,
                new SqlParameter("@offlineMinutes", offlineMinutes),
                new SqlParameter("@warningMinutes", warningMinutes))
            .ToListAsync(cancellationToken);

        return ReportResults.From(Id, rows, new Dictionary<string, object?>
        {
            ["offlineHours"] = offlineHours,
            ["warningHours"] = warningHours,
        });
    }

    private static int? ParseInt(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var raw) || raw is null) return null;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var p) => p,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetInt32(),
            _ => throw new ArgumentException($"Parameter '{name}' must be an integer."),
        };
    }
}

public sealed class DeploymentHealthRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public int DeploymentId { get; set; }
    public string? DeploymentName { get; set; }
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public DateTime? LastEventDateTimeUtc { get; set; }
    public int? MinutesSinceLastEvent { get; set; }
    public string Status { get; set; } = string.Empty;
}
