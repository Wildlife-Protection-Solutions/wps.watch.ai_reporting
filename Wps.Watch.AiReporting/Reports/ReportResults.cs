using System.Reflection;

namespace Wps.Watch.AiReporting.Reports;

/// <summary>
/// Helpers for shaping strongly-typed row DTOs into <see cref="ReportResult"/>.
/// Reduces boilerplate so each report definition can focus on its SQL and
/// return a typed list rather than hand-rolled dictionaries.
/// </summary>
public static class ReportResults
{
    public static ReportResult From<TRow>(
        string reportId,
        IEnumerable<TRow> rows,
        IReadOnlyDictionary<string, object?>? parametersApplied = null)
    {
        var props = typeof(TRow).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columns = props.Select(p => p.Name).ToList().AsReadOnly();

        var rowDicts = rows
            .Select(row =>
            {
                IReadOnlyDictionary<string, object?> dict = props.ToDictionary(
                    p => p.Name,
                    p => p.GetValue(row));
                return dict;
            })
            .ToList()
            .AsReadOnly();

        return new ReportResult(
            ReportId: reportId,
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            Columns: columns,
            Rows: rowDicts,
            ParametersApplied: parametersApplied ?? new Dictionary<string, object?>());
    }

    /// <summary>
    /// Builds the <c>WHERE</c> clause fragment that scopes a report query to the
    /// caller's organizations. System admins see everything (returns <c>"1 = 1"</c>);
    /// non-admins with no orgs see nothing (returns <c>"1 = 0"</c>). The fragment
    /// is composed of integer literals from <see cref="Authorization.UserContext.OrganizationIds"/>,
    /// so SQL injection is not reachable.
    /// </summary>
    public static string BuildOrgScopeFilter(
        IReadOnlyList<int> organizationIds,
        bool isSystemAdmin,
        string orgIdColumn)
    {
        if (isSystemAdmin)
        {
            return "1 = 1";
        }
        if (organizationIds.Count == 0)
        {
            return "1 = 0";
        }
        return $"{orgIdColumn} IN ({string.Join(",", organizationIds)})";
    }
}
