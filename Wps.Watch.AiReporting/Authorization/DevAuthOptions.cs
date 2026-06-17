namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Configuration for the dev-only fake-user shim. Bound from the
/// <c>DevAuth</c> section of appsettings.Development.json. When
/// <see cref="Enabled"/> is true and the environment is Development,
/// every request is treated as coming from this user.
/// </summary>
public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    public bool Enabled { get; set; }
    public string UserId { get; set; } = "dev-user";
    public string DisplayName { get; set; } = "Local dev user";
    public bool IsSystemAdmin { get; set; } = true;
    public List<int> OrganizationIds { get; set; } = new();
    public List<string> RoleNames { get; set; } = new();
}
