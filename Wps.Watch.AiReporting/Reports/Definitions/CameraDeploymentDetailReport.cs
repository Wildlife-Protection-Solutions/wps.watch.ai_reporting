using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Flat list of every active camera deployment. User-tier report; one of the
/// three currently surfaced to regular users in Looker.
/// </summary>
internal sealed class CameraDeploymentDetailReport : IReportDefinition
{
    public string Id => "camera-deployment-detail";
    public string Name => "Camera Deployment Detail";
    public string Description =>
        "Every active camera deployment with site, device, start date, last event, and " +
        "current battery percentage. One row per deployment.";
    public string Operation => ReportOperations.UserReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "medium";

    public async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        var sql = $@"
SELECT
    o.OrganizationId,
    o.[Name]            AS OrganizationName,
    s.SiteId,
    s.[Name]            AS SiteName,
    s.CountryName,
    d.DeploymentId,
    d.[Name]            AS DeploymentName,
    d.StartDateTimeUtc,
    d.LastEventDateTimeUtc,
    d.EventCount,
    de.DeviceId,
    de.[Name]           AS DeviceName,
    de.SerialNumber,
    dmk.[Name]          AS DeviceMakeName,
    dmd.[Name]          AS DeviceModelName,
    CAST(ROUND(ISNULL(d.BatteryLevel, 0) * 100, 0) AS int) AS BatteryPercent,
    d.SolarPanel,
    CASE WHEN DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) < 1440 THEN 1 ELSE 0 END AS IsReportingActive
FROM dbo.Deployment d
JOIN dbo.Site s         ON s.SiteId = d.SiteId AND s.IsActive = 1
JOIN dbo.Organization o ON o.OrganizationId = s.OrganizationId
JOIN dbo.Device de      ON de.DeviceId = d.DeviceId
JOIN dbo.DeviceMake dmk ON dmk.DeviceMakeId = de.DeviceMakeId
JOIN dbo.DeviceModel dmd ON dmd.DeviceModelId = de.DeviceModelId
WHERE d.EndDateTimeUtc IS NULL
  AND de.DeviceTypeId = 1
  AND de.DecommissionDate IS NULL
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
ORDER BY OrganizationName, SiteName, DeploymentName;
";

        var rows = await db.Database
            .SqlQueryRaw<CameraDeploymentDetailRow>(sql)
            .ToListAsync(cancellationToken);

        return ReportResults.From(Id, rows);
    }
}

public sealed class CameraDeploymentDetailRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int SiteId { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string? CountryName { get; set; }
    public int DeploymentId { get; set; }
    public string? DeploymentName { get; set; }
    public DateTime StartDateTimeUtc { get; set; }
    public DateTime? LastEventDateTimeUtc { get; set; }
    public int EventCount { get; set; }
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceMakeName { get; set; }
    public string? DeviceModelName { get; set; }
    public int BatteryPercent { get; set; }
    public bool SolarPanel { get; set; }
    public int IsReportingActive { get; set; }
}
