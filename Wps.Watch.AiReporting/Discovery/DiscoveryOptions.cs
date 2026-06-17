namespace Wps.Watch.AiReporting.Discovery;

/// <summary>
/// Configuration for the free-form discovery query surface (Rung 1: internal
/// team, devqa). Bound from the <c>Discovery</c> section of appsettings.
/// </summary>
public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    /// <summary>Master switch. When false, the discovery tools/endpoints 503.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hard cap on rows returned from a single free-form query.</summary>
    public int MaxRows { get; set; } = 5000;

    /// <summary>Per-query statement timeout (seconds).</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
}
