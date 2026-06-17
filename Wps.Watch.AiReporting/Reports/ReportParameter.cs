namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// Declarative description of a single report parameter. Used by the catalog
/// to expose the report's schema to REST clients and to MCP tool definitions.
/// </summary>
public sealed record ReportParameter(
    string Name,
    ReportParameterType Type,
    bool Required,
    string? Description = null,
    object? Default = null);

public enum ReportParameterType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
}
