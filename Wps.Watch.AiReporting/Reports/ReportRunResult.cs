namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// Outcome of <see cref="ReportRunner.RunAsync"/>. Discriminated union: callers
/// pattern-match on the concrete type to surface the right HTTP status code.
/// </summary>
public abstract record ReportRunOutcome
{
    public sealed record Success(ReportResult Data, TimeSpan Duration) : ReportRunOutcome;
    public sealed record NotFound(string ReportId) : ReportRunOutcome;
    public sealed record Forbidden(string ReportId) : ReportRunOutcome;
    public sealed record InvalidParameters(string ReportId, IReadOnlyList<string> Errors) : ReportRunOutcome;
    public sealed record Unauthenticated() : ReportRunOutcome;
}
