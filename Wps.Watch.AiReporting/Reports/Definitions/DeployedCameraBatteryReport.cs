using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Per-deployment battery level for every active camera. Ports
/// <c>wpsWatch.vw_wpsWatchDeployedCameraBatteryLevel</c>: battery is stored
/// as a fractional decimal (0.0–1.0) and normalised to a percentage here.
/// </summary>
internal sealed class DeployedCameraBatteryReport : IReportDefinition
{
    public string Id => "deployed-camera-battery";
    public string Name => "Deployed Camera Battery Level";
    public string Description =>
        "Battery level (0–100%) for every active deployment, with site and device metadata. " +
        "Includes a flag indicating whether the deployment has reported within the last 24h.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "medium";

    public Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
        => DeployedCameraBatteryQuery.RunAsync(db, user, Id, cancellationToken);
}

internal sealed class CameraBatteryLevelUserReport : IReportDefinition
{
    public string Id => "camera-battery-level";
    public string Name => "Camera Battery Level";
    public string Description =>
        "Battery level for every active camera deployment. User-tier view of the same data " +
        "as the admin Deployed Camera Battery report.";
    public string Operation => ReportOperations.UserReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "medium";

    public Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
        => DeployedCameraBatteryQuery.RunAsync(db, user, Id, cancellationToken);
}

/// <summary>Shared query body for the admin + user battery reports.</summary>
file static class DeployedCameraBatteryQuery
{
    public static async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        string reportId,
        CancellationToken cancellationToken)
    {
        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        var sql = $@"
SELECT
    o.OrganizationId,
    o.[Name]              AS OrganizationName,
    s.[Name]              AS SiteName,
    de.DeviceId,
    de.[Name]             AS DeviceName,
    d.DeploymentId,
    d.[Name]              AS DeploymentName,
    dmk.[Name]            AS DeviceMakeName,
    dmd.[Name]            AS DeviceModelName,
    d.LastEventDateTimeUtc,
    CAST(ROUND(ISNULL(d.BatteryLevel, 0) * 100, 0) AS int) AS BatteryPercent,
    CASE WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) < 1440 THEN 1 ELSE 0 END AS IsReportingActive
FROM dbo.Device de
JOIN dbo.Deployment d  ON d.DeviceId = de.DeviceId AND d.EndDateTimeUtc IS NULL
JOIN dbo.Organization o ON o.OrganizationId = de.OrganizationId
JOIN dbo.Site s         ON s.SiteId = d.SiteId AND s.IsActive = 1
JOIN dbo.DeviceMake dmk ON dmk.DeviceMakeId = de.DeviceMakeId
JOIN dbo.DeviceModel dmd ON dmd.DeviceModelId = de.DeviceModelId
WHERE de.DecommissionDate IS NULL
  AND de.DeviceTypeId = 1
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
ORDER BY OrganizationName, SiteName, DeviceName;
";

        var rows = await db.Database
            .SqlQueryRaw<DeployedCameraBatteryRow>(sql)
            .ToListAsync(cancellationToken);

        return ReportResults.From(reportId, rows);
    }
}

public sealed class DeployedCameraBatteryRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public int DeploymentId { get; set; }
    public string? DeploymentName { get; set; }
    public string? DeviceMakeName { get; set; }
    public string? DeviceModelName { get; set; }
    public DateTime? LastEventDateTimeUtc { get; set; }
    public int BatteryPercent { get; set; }
    public int IsReportingActive { get; set; }
}
