using Wps.Watch.AiReporting.Authorization;

namespace Wps.Watch.AiReporting.Reports;

public interface IReportCatalog
{
    IReadOnlyList<IReportDefinition> All { get; }
    IReportDefinition? Find(string id);
    IReadOnlyList<IReportDefinition> VisibleTo(UserContext user);
}

internal sealed class ReportCatalog : IReportCatalog
{
    private readonly Dictionary<string, IReportDefinition> _byId;

    public ReportCatalog(IEnumerable<IReportDefinition> reports)
    {
        All = reports.ToList().AsReadOnly();

        var dupes = All.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate report IDs registered: {string.Join(", ", dupes)}");
        }

        _byId = All.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IReportDefinition> All { get; }

    public IReportDefinition? Find(string id) =>
        _byId.TryGetValue(id, out var report) ? report : null;

    public IReadOnlyList<IReportDefinition> VisibleTo(UserContext user) =>
        All.Where(r => user.Can(r.Operation)).ToList().AsReadOnly();
}
