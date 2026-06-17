using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// Photo Count By Camera — last 30 days (admin variant, no parameters).
/// Aggregates dbo.Photo per device for the trailing 30 days.
/// </summary>
internal sealed class PhotoCountByCameraLastMonthReport : IReportDefinition
{
    public string Id => "photo-count-by-camera-last-month";
    public string Name => "Photo Count By Camera (Last 30 Days)";
    public string Description =>
        "Number of photos uploaded per camera in the last 30 days. " +
        "May be slow against raw OLTP without pre-aggregation (see v5 DR-V5-23).";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "slow";

    public Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-30);
        return PhotoCountQuery.RunAsync(db, user, Id, start, end, cancellationToken);
    }
}

/// <summary>
/// Photo Count By Camera — explicit date range (admin variant).
/// Same shape as the last-month report but with @startDate / @endDate.
/// </summary>
internal sealed class PhotoCountByCameraRangeReport : IReportDefinition
{
    public string Id => "photo-count-by-camera-range";
    public string Name => "Photo Count By Camera (Custom Range)";
    public string Description =>
        "Number of photos per camera within an explicit date range. Parameters: " +
        "startDate (inclusive), endDate (exclusive), ISO-8601 dates.";
    public string Operation => ReportOperations.AdminReport;
    public IReadOnlyList<ReportParameter> Parameters => new[]
    {
        new ReportParameter("startDate", ReportParameterType.Date, Required: true,
            Description: "Inclusive lower bound (UTC)."),
        new ReportParameter("endDate", ReportParameterType.Date, Required: true,
            Description: "Exclusive upper bound (UTC)."),
    };
    public string ExpectedCostClass => "slow";

    public Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var start = ParseDate(parameters, "startDate");
        var end = ParseDate(parameters, "endDate");
        return PhotoCountQuery.RunAsync(db, user, Id, start, end, cancellationToken);
    }

    private static DateTime ParseDate(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var raw) || raw is null)
        {
            throw new ArgumentException($"Missing required parameter '{name}'.");
        }
        return raw switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string s when DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var p) => p,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                && DateTime.TryParse(je.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var jp) => jp,
            _ => throw new ArgumentException($"Parameter '{name}' must be a date (got {raw.GetType().Name}).")
        };
    }
}

/// <summary>User-tier variant of Photo Count By Camera (last 30 days).</summary>
internal sealed class PhotoCountByCameraUserReport : IReportDefinition
{
    public string Id => "photo-count-by-camera";
    public string Name => "Photo Count By Camera";
    public string Description =>
        "User-tier view of photo counts per camera for the last 30 days.";
    public string Operation => ReportOperations.UserReport;
    public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
    public string ExpectedCostClass => "slow";

    public Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-30);
        return PhotoCountQuery.RunAsync(db, user, Id, start, end, cancellationToken);
    }
}

file static class PhotoCountQuery
{
    public static async Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        string reportId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var orgFilter = ReportResults.BuildOrgScopeFilter(
            user.OrganizationIds, user.IsSystemAdmin, "o.OrganizationId");

        // Photo table can be very large; cap to TOP 5000 rows to avoid OOM.
        // The aggregation runs server-side then we cap; rows are per-device.
        var sql = $@"
SELECT TOP 5000
    o.OrganizationId,
    o.[Name]            AS OrganizationName,
    s.[Name]            AS SiteName,
    de.DeviceId,
    de.[Name]           AS DeviceName,
    d.DeploymentId,
    d.[Name]            AS DeploymentName,
    COUNT(p.PhotoId)    AS PhotoCount,
    MIN(p.CaptureDateTimeUtc)  AS FirstPhotoUtc,
    MAX(p.CaptureDateTimeUtc)  AS LastPhotoUtc
FROM dbo.Deployment d
JOIN dbo.Site s         ON s.SiteId = d.SiteId AND s.IsActive = 1
JOIN dbo.Organization o ON o.OrganizationId = s.OrganizationId
JOIN dbo.Device de      ON de.DeviceId = d.DeviceId AND de.DeviceTypeId = 1
LEFT JOIN dbo.Photo p   ON p.DeploymentId = d.DeploymentId
                       AND p.CaptureDateTimeUtc >= @startDate
                       AND p.CaptureDateTimeUtc <  @endDate
WHERE d.EndDateTimeUtc IS NULL
  AND o.IsReportedOn = 1
  AND o.IsActive = 1
  AND ({orgFilter})
GROUP BY o.OrganizationId, o.[Name], s.[Name], de.DeviceId, de.[Name], d.DeploymentId, d.[Name]
ORDER BY OrganizationName, SiteName, DeviceName;
";

        var rows = await db.Database
            .SqlQueryRaw<PhotoCountByCameraRow>(sql,
                new SqlParameter("@startDate", startDateUtc),
                new SqlParameter("@endDate", endDateUtc))
            .ToListAsync(cancellationToken);

        return ReportResults.From(
            reportId,
            rows,
            new Dictionary<string, object?>
            {
                ["startDate"] = startDateUtc,
                ["endDate"] = endDateUtc,
            });
    }
}

public sealed class PhotoCountByCameraRow
{
    public int OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public int DeploymentId { get; set; }
    public string? DeploymentName { get; set; }
    public int PhotoCount { get; set; }
    public DateTime? FirstPhotoUtc { get; set; }
    public DateTime? LastPhotoUtc { get; set; }
}
