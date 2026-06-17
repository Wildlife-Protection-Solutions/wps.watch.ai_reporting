namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Identity + authorization scope for the current request.
/// </summary>
public sealed record UserContext
{
    public required string UserId { get; init; }
    public required IReadOnlyList<int> OrganizationIds { get; init; }
    public required IReadOnlyList<string> RoleNames { get; init; }
    public required bool IsSystemAdmin { get; init; }

    /// <summary>
    /// Whether this user can perform the named operation. System admins
    /// always pass; otherwise the user must have one of the roles listed
    /// in <see cref="ReportOperations.RolesByOperation"/>.
    /// </summary>
    public bool Can(string operation)
    {
        if (IsSystemAdmin)
        {
            return true;
        }

        if (!ReportOperations.RolesByOperation.TryGetValue(operation, out var allowed))
        {
            return false;
        }

        foreach (var roleName in RoleNames)
        {
            foreach (var allowedRole in allowed)
            {
                if (string.Equals(roleName, allowedRole, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
