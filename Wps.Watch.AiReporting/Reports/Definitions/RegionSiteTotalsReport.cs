using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Region &amp; Site Totals with Deployment — the v5 tracer-bullet report.
///
/// Ported from <c>wpsWatch.tvf_wpsWatchRegionSiteTotalsV3</c>. Differences:
/// <list type="bullet">
///   <item>Runs against the OLTP tables directly (the replica) rather than
///         pre-aggregated analytics-DB tables.</item>
///   <item>Adds an explicit <c>OrganizationId IN (...)</c> filter scoping the
///         output to the caller's org list (system admins see all orgs).</item>
///   <item>The 1440-minute (24h) online/offline threshold and the
///         <c>DeviceTypeId = 1</c> camera filter match the original.</item>
/// </list>
///
/// When the curated <c>dbo.vw_ai_region_site_totals</c> view lands in the
/// primary OLTP schema, this implementation becomes a one-line
/// <c>SELECT * FROM dbo.vw_ai_region_site_totals WHERE OrganizationId IN (...)</c>.
/// </summary>
internal sealed class RegionSiteTotalsReport : IReportDefinition
{
    public string Id => "region-site-totals";
    public string Name => "Region & Site Totals with Deployment";
    public string Description =>
        "Per-organization inventory, deployment, online/offline, and active-issue totals, " +
        "scoped to the calling user's organizations.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "medium";

    public async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        // Inline the user's org IDs into the SQL as a sanitized literal list.
        // The values come from `OrganizationIds` (typed int), so SQL injection
        // is not reachable — but we still cast through `int` to be explicit.
        // System admins (empty list) skip the org filter entirely.
        var orgIds = user.OrganizationIds.Select(o => (int)o).ToArray();
        var orgFilter = user.IsSystemAdmin
            ? "1 = 1"
            : orgIds.Length == 0
                ? "1 = 0" // Non-admin with no orgs sees nothing.
                : $"o.OrganizationId IN ({string.Join(",", orgIds)})";

        var sql = $@"
;WITH camera_inventory AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device
    WHERE DeviceTypeId = 1
    GROUP BY OrganizationId
),
CurrentInventory AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device
    WHERE DeviceTypeId = 1 AND DecommissionDate IS NULL
    GROUP BY OrganizationId
),
Decommissioned AS (
    SELECT OrganizationId, COUNT(DeviceId) AS Inventory
    FROM dbo.Device
    WHERE DeviceTypeId = 1 AND DecommissionDate IS NOT NULL
    GROUP BY OrganizationId
),
TotalDeployments AS (
    SELECT c.OrganizationId, COUNT(c.DeviceId) AS Active
    FROM dbo.Device c
    JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId AND d.EndDateTimeUtc IS NULL
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1
    GROUP BY c.OrganizationId
),
ActiveDeployments AS (
    SELECT c.OrganizationId, d.SiteId, COUNT(c.DeviceId) AS Active
    FROM dbo.Device c
    JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId
        AND DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) < 1440
        AND d.EndDateTimeUtc IS NULL
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1
    GROUP BY c.OrganizationId, d.SiteId
),
InactiveDeployments AS (
    SELECT c.OrganizationId, d.SiteId, COUNT(c.DeviceId) AS Inactive
    FROM dbo.Device c
    JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId
        AND (DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) >= 1440 OR d.LastEventDateTimeUtc IS NULL)
        AND d.EndDateTimeUtc IS NULL
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1
    GROUP BY c.OrganizationId, d.SiteId
),
Undeployed AS (
    SELECT c.OrganizationId, d.SiteId, COUNT(c.DeviceId) AS Active
    FROM dbo.Device c
    LEFT JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId AND d.EndDateTimeUtc IS NULL
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1 AND d.DeploymentId IS NULL
    GROUP BY c.OrganizationId, d.SiteId
),
Issues AS (
    SELECT COUNT(i.IssueId) AS Issues, o.OrganizationId, d.SiteId
    FROM dbo.Issue i
    JOIN dbo.Deployment d ON d.DeploymentId = i.DeploymentId AND d.EndDateTimeUtc IS NULL
    JOIN dbo.Site s ON s.SiteId = d.SiteId
    JOIN dbo.Organization o ON o.OrganizationId = s.OrganizationId
    WHERE i.IsResolved = 0
    GROUP BY o.OrganizationId, d.SiteId
),
SolarPower AS (
    SELECT o.OrganizationId, COUNT(c.DeviceId) AS SolarPanel, d.SiteId
    FROM dbo.Device c
    JOIN dbo.Deployment d ON d.DeviceId = c.DeviceId AND d.EndDateTimeUtc IS NULL
    JOIN dbo.Site s ON s.SiteId = d.SiteId
    JOIN dbo.Organization o ON o.OrganizationId = s.OrganizationId
    WHERE c.DecommissionDate IS NULL AND c.DeviceTypeId = 1 AND d.SolarPanel = 1
    GROUP BY o.OrganizationId, d.SiteId
),
allrec AS (
    SELECT
        r.[Name] AS RegionName,
        o.[Name] AS OrganizationName,
        o.OrganizationId,
        ci.Inventory AS HistoricalInventory,
        COALESCE(de.Inventory, 0) AS Decommissioned,
        COALESCE(u.Active, 0) AS Undeployed,
        COALESCE(td.Active, 0) AS TotalDeployments,
        COALESCE(cui.Inventory, 0) AS CurrentInventory,
        s.Name AS SiteName,
        COALESCE(d.Active, 0) AS Online,
        COALESCE(i.Inactive, 0) AS Offline,
        COALESCE(iss.Issues, 0) AS ActiveIssues,
        COALESCE(sp.SolarPanel, 0) AS SolarPower
    FROM dbo.Organization o
    JOIN dbo.Site s ON s.OrganizationId = o.OrganizationId AND s.IsActive = 1
    JOIN camera_inventory ci ON o.OrganizationId = ci.OrganizationId
    LEFT JOIN CurrentInventory cui ON o.OrganizationId = cui.OrganizationId
    LEFT JOIN Decommissioned de ON o.OrganizationId = de.OrganizationId
    LEFT JOIN TotalDeployments td ON td.OrganizationId = o.OrganizationId
    LEFT JOIN ActiveDeployments d ON o.OrganizationId = d.OrganizationId AND d.SiteId = s.SiteId
    LEFT JOIN InactiveDeployments i ON o.OrganizationId = i.OrganizationId AND i.SiteId = s.SiteId
    LEFT JOIN Undeployed u ON o.OrganizationId = u.OrganizationId
    LEFT JOIN Issues iss ON iss.OrganizationId = o.OrganizationId AND iss.SiteId = s.SiteId
    LEFT JOIN dbo.Region r ON r.RegionId = o.RegionId
    LEFT JOIN SolarPower sp ON sp.OrganizationId = o.OrganizationId AND sp.SiteId = s.SiteId
    WHERE o.IsReportedOn = 1
      AND o.IsActive = 1
      AND s.IsActive = 1
      AND ({orgFilter})
)
SELECT DISTINCT
    ISNULL(RegionName, '')        AS RegionName,
    ISNULL(OrganizationName, '')  AS OrganizationName,
    OrganizationId,
    HistoricalInventory,
    Decommissioned,
    Undeployed,
    TotalDeployments,
    CurrentInventory,
    COUNT(OrganizationName) AS SiteCount,
    SUM([Online])           AS Online,
    SUM([Offline])          AS Offline,
    SUM(ActiveIssues)       AS ActiveIssues,
    SUM([Online] + [Offline])                 AS Total,
    SUM(SolarPower)                           AS SolarPower
FROM allrec
GROUP BY
    RegionName, OrganizationName, OrganizationId,
    HistoricalInventory, Decommissioned, Undeployed,
    TotalDeployments, CurrentInventory
ORDER BY RegionName, OrganizationName;
";

        var rows = await db.Database
            .SqlQueryRaw<RegionSiteTotalsRow>(sql)
            .ToListAsync(cancellationToken);

        var columns = new[]
        {
            nameof(RegionSiteTotalsRow.RegionName),
            nameof(RegionSiteTotalsRow.OrganizationName),
            nameof(RegionSiteTotalsRow.OrganizationId),
            nameof(RegionSiteTotalsRow.HistoricalInventory),
            nameof(RegionSiteTotalsRow.Decommissioned),
            nameof(RegionSiteTotalsRow.Undeployed),
            nameof(RegionSiteTotalsRow.TotalDeployments),
            nameof(RegionSiteTotalsRow.CurrentInventory),
            nameof(RegionSiteTotalsRow.SiteCount),
            nameof(RegionSiteTotalsRow.Online),
            nameof(RegionSiteTotalsRow.Offline),
            nameof(RegionSiteTotalsRow.ActiveIssues),
            nameof(RegionSiteTotalsRow.Total),
            nameof(RegionSiteTotalsRow.SolarPower),
        };

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rowsAsDicts = rows
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                [nameof(r.RegionName)] = r.RegionName,
                [nameof(r.OrganizationName)] = r.OrganizationName,
                [nameof(r.OrganizationId)] = r.OrganizationId,
                [nameof(r.HistoricalInventory)] = r.HistoricalInventory,
                [nameof(r.Decommissioned)] = r.Decommissioned,
                [nameof(r.Undeployed)] = r.Undeployed,
                [nameof(r.TotalDeployments)] = r.TotalDeployments,
                [nameof(r.CurrentInventory)] = r.CurrentInventory,
                [nameof(r.SiteCount)] = r.SiteCount,
                [nameof(r.Online)] = r.Online,
                [nameof(r.Offline)] = r.Offline,
                [nameof(r.ActiveIssues)] = r.ActiveIssues,
                [nameof(r.Total)] = r.Total,
                [nameof(r.SolarPower)] = r.SolarPower,
            })
            .ToList();

        return new ReportResult(
            ReportId: Id,
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            Columns: columns,
            Rows: rowsAsDicts,
            ParametersApplied: new Dictionary<string, object?>());
    }
}
