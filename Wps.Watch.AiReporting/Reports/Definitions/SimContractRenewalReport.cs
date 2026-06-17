using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Devices with an upcoming or recently-passed SIM contract renewal date.
/// Default window is the next 90 days; tune via the lookaheadDays parameter.
/// </summary>
internal sealed class SimContractRenewalReport : IReportDefinition
{
    public string Id => "sim-contract-renewal";
    public string Name => "SIM Contract Renewal";
    public string Description =>
        "Cameras whose SIM contract is renewing within the lookahead window. " +
        "Default window is 90 days; pass `lookaheadDays` to change.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => new[]
    {
        new ReportParameter("lookaheadDays", ReportParameterType.Integer, Required: false,
            Description: "Number of days into the future to include (default 90).",
            Default: 90),
    };
    public string ExpectedCostClass => "fast";

    public async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var lookaheadDays = ParseInt(parameters, "lookaheadDays") ?? 90;
        var lookaheadDate = DateTime.UtcNow.AddDays(lookaheadDays);

        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        var sql = $@"
SELECT
    o.OrganizationId,
    o.[Name]            AS OrganizationName,
    de.DeviceId,
    de.[Name]           AS DeviceName,
    de.SerialNumber,
    de.CarrierName,
    de.SimContractRenewalDate,
    DATEDIFF(Day, GETUTCDATE(), de.SimContractRenewalDate) AS DaysUntilRenewal,
    s.SiteId,
    s.[Name]            AS SiteName,
    d.DeploymentId,
    d.[Name]            AS DeploymentName
FROM dbo.Device de
JOIN dbo.Organization o ON o.OrganizationId = de.OrganizationId
LEFT JOIN dbo.Deployment d ON d.DeviceId = de.DeviceId AND d.EndDateTimeUtc IS NULL
LEFT JOIN dbo.Site s       ON s.SiteId = d.SiteId
WHERE de.DecommissionDate IS NULL
  AND de.DeviceTypeId = 1
  AND de.SimContractRenewalDate IS NOT NULL
  AND de.SimContractRenewalDate <= @lookaheadDate
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
ORDER BY de.SimContractRenewalDate, OrganizationName;
";

        var rows = await db.Database
            .SqlQueryRaw<SimContractRenewalRow>(sql,
                new Microsoft.Data.SqlClient.SqlParameter("@lookaheadDate", lookaheadDate))
            .ToListAsync(cancellationToken);

        return ReportResults.From(Id, rows,
            new Dictionary<string, object?> { ["lookaheadDays"] = lookaheadDays });
    }

    private static int? ParseInt(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }
        return raw switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var p) => p,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetInt32(),
            _ => throw new ArgumentException($"Parameter '{name}' must be an integer (got {raw.GetType().Name})."),
        };
    }
}

public sealed class SimContractRenewalRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? SerialNumber { get; set; }
    public string? CarrierName { get; set; }
    public DateTime? SimContractRenewalDate { get; set; }
    public int? DaysUntilRenewal { get; set; }
    public int? SiteId { get; set; }
    public string? SiteName { get; set; }
    public int? DeploymentId { get; set; }
    public string? DeploymentName { get; set; }
}
