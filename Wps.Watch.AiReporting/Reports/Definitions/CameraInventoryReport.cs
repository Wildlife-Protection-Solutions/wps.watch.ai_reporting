using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

internal sealed class CameraInventoryReport : IReportDefinition
{
    public string Id => "camera-inventory";
    public string Name => "Camera Inventory";
    public string Description =>
        "Per-organization camera inventory: total / current / decommissioned / donated / " +
        "site-owned / deployed / undeployed. Ports wpsWatch.tvf_wpsWatchCameraInv.";
    public string Operation => ReportOperations.AdminReport;
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
;WITH camera_inventory AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device WHERE DeviceTypeId = 1
    GROUP BY OrganizationId
),
CurrentInventory AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device WHERE DeviceTypeId = 1 AND DecommissionDate IS NULL
    GROUP BY OrganizationId
),
Decommissioned AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device WHERE DeviceTypeId = 1 AND DecommissionDate IS NOT NULL
    GROUP BY OrganizationId
),
Donated AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device
    WHERE DeviceTypeId = 1 AND DecommissionDate IS NULL AND DeviceSourceId = 1
    GROUP BY OrganizationId
),
SiteOwned AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device
    WHERE DeviceTypeId = 1 AND DecommissionDate IS NULL AND DeviceSourceId <> 1
    GROUP BY OrganizationId
),
DeployedNow AS (
    SELECT c.OrganizationId, COUNT(c.DeviceId) AS Active
    FROM dbo.Device c
    JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId AND d.EndDateTimeUtc IS NULL
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1
    GROUP BY c.OrganizationId
),
Sites AS (
    SELECT OrganizationId, COUNT(SiteId) AS SiteCount
    FROM dbo.Site WHERE IsActive = 1
    GROUP BY OrganizationId
)
SELECT
    o.OrganizationId,
    o.[Name] AS OrganizationName,
    ISNULL(r.[Name], '') AS RegionName,
    ISNULL(ci.Inventory, 0)   AS TotalInventory,
    ISNULL(cui.Inventory, 0)  AS CurrentInventory,
    ISNULL(de.Inventory, 0)   AS Decommissioned,
    ISNULL(don.Inventory, 0)  AS Donated,
    ISNULL(so.Inventory, 0)   AS SiteOwned,
    ISNULL(dn.Active, 0)      AS TotalDeployments,
    ISNULL(ci.Inventory, 0) - ISNULL(dn.Active, 0) AS Undeployed,
    ISNULL(sc.SiteCount, 0)   AS SiteCount
FROM dbo.Organization o
LEFT JOIN dbo.Region r            ON r.RegionId = o.RegionId
LEFT JOIN camera_inventory ci     ON ci.OrganizationId = o.OrganizationId
LEFT JOIN CurrentInventory cui    ON cui.OrganizationId = o.OrganizationId
LEFT JOIN Decommissioned de       ON de.OrganizationId = o.OrganizationId
LEFT JOIN Donated don             ON don.OrganizationId = o.OrganizationId
LEFT JOIN SiteOwned so            ON so.OrganizationId = o.OrganizationId
LEFT JOIN DeployedNow dn          ON dn.OrganizationId = o.OrganizationId
LEFT JOIN Sites sc                ON sc.OrganizationId = o.OrganizationId
WHERE o.IsReportedOn = 1 AND o.IsActive = 1
  AND ISNULL(ci.Inventory, 0) > 0
  AND ({orgFilter})
ORDER BY RegionName, OrganizationName;
";

        var rows = await db.Database
            .SqlQueryRaw<CameraInventoryRow>(sql)
            .ToListAsync(cancellationToken);

        return ReportResults.From(Id, rows);
    }
}

public sealed class CameraInventoryRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int TotalInventory { get; set; }
    public int CurrentInventory { get; set; }
    public int Decommissioned { get; set; }
    public int Donated { get; set; }
    public int SiteOwned { get; set; }
    public int TotalDeployments { get; set; }
    public int Undeployed { get; set; }
    public int SiteCount { get; set; }
}
