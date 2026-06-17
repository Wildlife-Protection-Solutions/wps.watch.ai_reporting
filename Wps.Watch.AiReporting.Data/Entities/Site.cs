namespace Wps.Watch.AiReporting.Data.Entities;

/// <summary>
/// Narrow Site projection. Sensitive columns NOT declared (Latitude, Longitude,
/// DasToken, SmartIntegrateToken, SlackWebhookUrl, DasBaseUrl,
/// SmartIntegrateBaseUrl) per v5 S-V5-MUST-28. EF cannot SELECT what isn't on
/// the entity, so this is "exclude by absence" — the Phase 1 substitute for the
/// curated vw_ai_* view set + ai_reports_reader column DENYs.
/// </summary>
public class Site
{
    public int SiteId { get; set; }
    public string Name { get; set; } = null!;
    public int OrganizationId { get; set; }
    public string? CountryName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? TimeZone { get; set; }
    public bool IsVolunteerProgram { get; set; }
    public bool IsDefault { get; set; }
}
