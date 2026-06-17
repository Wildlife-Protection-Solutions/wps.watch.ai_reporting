namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Per-request access to the resolved <see cref="UserContext"/>.
/// Returns null before middleware has populated it (or if auth failed).
/// </summary>
public interface IUserContextAccessor
{
    UserContext? Current { get; }
    void Set(UserContext context);
}

internal sealed class UserContextAccessor : IUserContextAccessor
{
    public UserContext? Current { get; private set; }

    public void Set(UserContext context)
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("UserContext is already set for this request.");
        }

        Current = context;
    }
}
