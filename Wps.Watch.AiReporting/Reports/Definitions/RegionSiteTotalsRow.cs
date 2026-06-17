namespace Wps.Watch.AiReporting.Reports.Definitions;

/// <summary>
/// One row of the Region &amp; Site Totals report. Shape mirrors the existing
/// <c>wpsWatch.tvf_wpsWatchRegionSiteTotalsV3</c> table-valued function — when
/// we cut over to <c>dbo.vw_ai_region_site_totals</c> in production, this DTO
/// is the projection target. All counts are <c>int</c> to match what SQL
/// <c>COUNT</c>/<c>SUM</c> returns; the original TVF declared some as
/// <c>smallint</c> but values are functionally interchangeable.
/// </summary>
public sealed class RegionSiteTotalsRow
{
    public string RegionName { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public int OrganizationId { get; set; }
    public int HistoricalInventory { get; set; }
    public int Decommissioned { get; set; }
    public int Undeployed { get; set; }
    public int TotalDeployments { get; set; }
    public int CurrentInventory { get; set; }
    public int SiteCount { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
    public int ActiveIssues { get; set; }
    public int Total { get; set; }
    public int SolarPower { get; set; }
}
