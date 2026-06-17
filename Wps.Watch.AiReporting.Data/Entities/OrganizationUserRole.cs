namespace Wps.Watch.AiReporting.Data.Entities;

public class OrganizationUserRole
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int OrganizationId { get; set; }
    public string RoleId { get; set; } = null!;
}
