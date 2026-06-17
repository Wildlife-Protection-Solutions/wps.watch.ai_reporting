namespace Wps.Watch.AiReporting.Data.Entities;

public class Organization
{
    public int OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    public int? RegionId { get; set; }
    public bool IsActive { get; set; }
    public bool IsReportedOn { get; set; }
    public bool IsVolunteerProgram { get; set; }
}
