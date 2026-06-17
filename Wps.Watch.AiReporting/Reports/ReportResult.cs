namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// Tabular result of a report run. Rows are column-name → value dictionaries
/// (preserving column order via <see cref="Columns"/>). Phase 1 returns raw
/// data; later phases may add typed projections and a freshness badge.
/// </summary>
public sealed record ReportResult(
    string ReportId,
    DateTimeOffset ExecutedAtUtc,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?> ParametersApplied);
