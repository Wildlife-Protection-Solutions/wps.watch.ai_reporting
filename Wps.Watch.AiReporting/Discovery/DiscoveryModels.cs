namespace Wps.Watch.AiReporting.Discovery;

/// <summary>A queryable table and its (non-sensitive) columns.</summary>
public sealed record SchemaTable(string Name, IReadOnlyList<SchemaColumn> Columns);

/// <summary>A single column in the discovery schema.</summary>
public sealed record SchemaColumn(string Name, string DataType, bool Nullable);

/// <summary>Row shape for the INFORMATION_SCHEMA introspection query.</summary>
internal sealed class SchemaColumnRow
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string IsNullable { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of a free-form discovery query. Discriminated union, mirroring the
/// style of <see cref="Reports.ReportRunOutcome"/>.
/// </summary>
public abstract record DiscoveryQueryOutcome
{
    public sealed record Success(
        IReadOnlyList<string> Columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        int RowCount,
        bool Truncated,
        TimeSpan Duration,
        string ExecutedSql) : DiscoveryQueryOutcome;

    /// <summary>Failed the SqlGuard checks (not a SELECT, multi-statement, etc.).</summary>
    public sealed record Rejected(string Reason) : DiscoveryQueryOutcome;

    /// <summary>Passed the guard but the database rejected it (bad column, syntax, etc.).</summary>
    public sealed record Failed(string Reason) : DiscoveryQueryOutcome;

    public sealed record Forbidden : DiscoveryQueryOutcome;
    public sealed record Unauthenticated : DiscoveryQueryOutcome;
    public sealed record Disabled : DiscoveryQueryOutcome;
}

/// <summary>Outcome of a <c>describe_schema</c> call.</summary>
public abstract record DiscoverySchemaOutcome
{
    public sealed record Success(IReadOnlyList<SchemaTable> Tables) : DiscoverySchemaOutcome;
    public sealed record Forbidden : DiscoverySchemaOutcome;
    public sealed record Unauthenticated : DiscoverySchemaOutcome;
    public sealed record Disabled : DiscoverySchemaOutcome;
}
