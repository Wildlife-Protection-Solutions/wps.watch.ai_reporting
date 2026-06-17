using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;

namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// A canned report: stable id, role gate, parameter schema, and the query
/// implementation. Implementations should be stateless singletons; the DbContext
/// and current user are supplied per run by <see cref="ReportRunner"/>.
/// </summary>
public interface IReportDefinition
{
    /// <summary>Stable kebab-case identifier, used in URLs and MCP tool args.</summary>
    string Id { get; }

    /// <summary>Human-readable name (displayed in the UI / MCP tool descriptions).</summary>
    string Name { get; }

    /// <summary>One-paragraph description for the report card and MCP tool description.</summary>
    string Description { get; }

    /// <summary>
    /// The authorization operation gating this report. One of
    /// <see cref="ReportOperations.AdminReport"/> or <see cref="ReportOperations.UserReport"/>.
    /// </summary>
    string Operation { get; }

    /// <summary>Declarative parameter schema.</summary>
    IReadOnlyList<ReportParameter> Parameters { get; }

    /// <summary>
    /// Indicative cost class (DR-V5-14): "fast", "medium", or "slow".
    /// Surfaced to chat users before execution; Phase 1 informational only.
    /// </summary>
    string ExpectedCostClass { get; }

    Task<ReportResult> RunAsync(
        WpsReplicaDbContext db,
        UserContext user,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);
}
