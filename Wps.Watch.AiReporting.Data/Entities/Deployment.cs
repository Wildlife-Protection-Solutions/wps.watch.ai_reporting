namespace Wps.Watch.AiReporting.Data.Entities;

/// <summary>
/// Narrow Deployment projection. GPS columns (Latitude, Longitude) NOT declared
/// per v5 S-V5-MUST-28.
/// </summary>
public class Deployment
{
    public int DeploymentId { get; set; }
    public string? Name { get; set; }
    public int SiteId { get; set; }
    public int DeviceId { get; set; }
    public DateTime StartDateTimeUtc { get; set; }
    public DateTime? EndDateTimeUtc { get; set; }
    public DateTime? LastEventDateTimeUtc { get; set; }
    public DateTime? BatteryBoxStartDate { get; set; }
    public int EventCount { get; set; }
    public decimal? BatteryLevel { get; set; }
    public bool SolarPanel { get; set; }
    public bool IsVolunteerProgram { get; set; }
    public int? ParentDeploymentId { get; set; }
}
