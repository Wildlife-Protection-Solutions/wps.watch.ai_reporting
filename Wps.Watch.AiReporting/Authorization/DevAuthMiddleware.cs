using Microsoft.Extensions.Options;

namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Populates <see cref="IUserContextAccessor"/> with a fake user from
/// <see cref="DevAuthOptions"/>. Only used when DevAuth:Enabled is true
/// (typically in Development). Real JWT validation replaces this in
/// production deployments.
/// </summary>
public sealed class DevAuthMiddleware
{
    private readonly RequestDelegate _next;

    public DevAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<DevAuthOptions> options,
        IUserContextAccessor accessor,
        ILogger<DevAuthMiddleware> logger)
    {
        var opts = options.Value;

        if (opts.Enabled && accessor.Current is null)
        {
            var ctx = new UserContext
            {
                UserId = opts.UserId,
                IsSystemAdmin = opts.IsSystemAdmin,
                OrganizationIds = opts.OrganizationIds.AsReadOnly(),
                RoleNames = opts.RoleNames.AsReadOnly(),
            };

            accessor.Set(ctx);

            logger.LogInformation(
                "DevAuth shim: request from {UserId} (IsSystemAdmin={IsSystemAdmin}, OrgIds=[{OrgIds}], Roles=[{Roles}])",
                ctx.UserId,
                ctx.IsSystemAdmin,
                string.Join(",", ctx.OrganizationIds),
                string.Join(",", ctx.RoleNames));
        }

        await _next(context);
    }
}
