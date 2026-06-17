namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Report operations mirrored from wps.watch.api's <c>ReportOperationRoles</c>.
/// </summary>
public static class ReportOperations
{
    public const string AdminReport = "AdminReport";
    public const string UserReport = "UserReport";

    /// <summary>
    /// Free-form discovery queries (Rung 1). System-admin-only for now — the
    /// internal-team discovery surface. Empty role list = no org-level role
    /// grants it; only the SystemAdmin bypass in <see cref="UserContext.Can"/>.
    /// </summary>
    public const string DiscoveryQuery = "DiscoveryQuery";

    /// <summary>
    /// Roles allowed to perform each operation. SystemAdmin is always allowed
    /// implicitly (handled by <see cref="UserContext.Can"/>) and not listed here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolesByOperation =
        new Dictionary<string, string[]>
        {
            { AdminReport, Array.Empty<string>() },
            { UserReport, new[] { Roles.OrgAdmin, Roles.IncidentManager, Roles.Viewer, Roles.GpsViewer } },
            { DiscoveryQuery, Array.Empty<string>() },
        };
}
