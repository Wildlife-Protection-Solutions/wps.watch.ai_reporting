using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// SIM Prepaid Data report. Estimates remaining prepaid data per camera using
/// the v5-confirmed heuristic <c>PrepaidDataLimitGb - (0.0001 × photo_count)</c>.
///
/// Phase 1 simplification: photo count is taken over the trailing 30 days
/// rather than the exact billing cycle the original SP computes. When we cut
/// over to the curated <c>dbo.vw_ai_sim_data</c> view, we'll inherit the
/// proper billing-cycle logic.
/// </summary>
internal sealed class SimPrepaidDataReport : IReportDefinition
{
    public string Id => "sim-prepaid-data";
    public string Name => "SIM Prepaid Data";
    public string Description =>
        "Per-camera estimated remaining prepaid data, using the heuristic " +
        "PrepaidDataLimitGb - (0.0001 × photo_count_last_30_days). Approximate.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "slow";

    public async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        var sql = $@"
DECLARE @WindowStart datetime = DATEADD(Day, -30, GETUTCDATE());
DECLARE @Today datetime = GETUTCDATE();

SELECT
    o.OrganizationId,
    o.[Name]            AS OrganizationName,
    s.[Name]            AS SiteName,
    de.DeviceId,
    de.[Name]           AS DeviceName,
    d.[Name]            AS DeploymentName,
    dmk.[Name]          AS DeviceMakeName,
    dmd.[Name]          AS DeviceModelName,
    de.SimCardDataPlanId,
    de.PrepaidDataLimitGb,
    de.PrepaidDataLimitTermId,
    de.PrepaidDataPlanRenewal,
    de.SimContractRenewalDate,
    ISNULL(pc.PhotoCount, 0) AS PhotoTotal,
    CAST(
        COALESCE(de.PrepaidDataLimitGb, 0) - (0.0001 * ISNULL(pc.PhotoCount, 0))
        AS decimal(10,4)
    ) AS EstimatedGbRemaining,
    DATEDIFF(Day, @Today, COALESCE(de.PrepaidDataPlanRenewal, de.SimContractRenewalDate)) AS DaysUntilRenewal
FROM dbo.Device de
JOIN dbo.Deployment d   ON d.DeviceId = de.DeviceId AND d.EndDateTimeUtc IS NULL
JOIN dbo.Site s         ON s.SiteId = d.SiteId AND s.IsActive = 1
JOIN dbo.Organization o ON o.OrganizationId = de.OrganizationId
JOIN dbo.DeviceMake dmk ON dmk.DeviceMakeId = de.DeviceMakeId
JOIN dbo.DeviceModel dmd ON dmd.DeviceModelId = de.DeviceModelId
LEFT JOIN (
    SELECT p.DeploymentId, COUNT(p.PhotoId) AS PhotoCount
    FROM dbo.Photo p
    WHERE p.CaptureDateTimeUtc BETWEEN @WindowStart AND @Today
    GROUP BY p.DeploymentId
) pc ON pc.DeploymentId = d.DeploymentId
WHERE de.DecommissionDate IS NULL
  AND de.DeviceTypeId = 1
  AND de.PrepaidDataLimitGb IS NOT NULL
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
ORDER BY EstimatedGbRemaining ASC, OrganizationName;
";

        var rows = await db.Database
            .SqlQueryRaw<SimPrepaidDataRow>(sql)
            .ToListAsync(cancellationToken);

        return ReportResults.From(Id, rows);
    }
}

public sealed class SimPrepaidDataRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? DeploymentName { get; set; }
    public string? DeviceMakeName { get; set; }
    public string? DeviceModelName { get; set; }
    public int? SimCardDataPlanId { get; set; }
    public decimal? PrepaidDataLimitGb { get; set; }
    public int? PrepaidDataLimitTermId { get; set; }
    public DateTime? PrepaidDataPlanRenewal { get; set; }
    public DateTime? SimContractRenewalDate { get; set; }
    public int PhotoTotal { get; set; }
    public decimal EstimatedGbRemaining { get; set; }
    public int? DaysUntilRenewal { get; set; }
}
