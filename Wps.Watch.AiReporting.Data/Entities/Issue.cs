namespace Wps.Watch.AiReporting.Data.Entities;

public class Issue
{
    public int IssueId { get; set; }
    public int DeploymentId { get; set; }
    public DateTime DateCreated { get; set; }
    public int IssueTypeId { get; set; }
    public int IssueSeverityTypeId { get; set; }
    public string? IssueDescription { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolutionDate { get; set; }
    public DateTime LastUpdated { get; set; }
}
