using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;
using Wps.Watch.AiReporting.Reports;

namespace Wps.Watch.AiReporting.Test;

[TestClass]
public class ReportCatalogTests
{
    private sealed class FakeReport : IReportDefinition
    {
        public required string Id { get; init; }
        public required string Operation { get; init; }
        public string Name => Id;
        public string Description => Id;
        public IReadOnlyList<ReportParameter> Parameters => Array.Empty<ReportParameter>();
        public string ExpectedCostClass => "fast";

        public Task<ReportResult> RunAsync(
            WpsReplicaDbContext db,
            UserContext user,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private static UserContext User(bool isSystemAdmin, params string[] roles) => new()
    {
        UserId = "u",
        OrganizationIds = Array.Empty<int>(),
        RoleNames = roles,
        IsSystemAdmin = isSystemAdmin,
    };

    [TestMethod]
    public void Find_ReturnsRegisteredReport()
    {
        var catalog = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "a", Operation = ReportOperations.UserReport },
            new FakeReport { Id = "b", Operation = ReportOperations.AdminReport },
        });

        Assert.AreEqual("a", catalog.Find("a")?.Id);
        Assert.AreEqual("b", catalog.Find("b")?.Id);
        Assert.IsNull(catalog.Find("c"));
    }

    [TestMethod]
    public void Find_IsCaseInsensitive()
    {
        var catalog = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "region-site-totals", Operation = ReportOperations.AdminReport },
        });

        Assert.IsNotNull(catalog.Find("REGION-site-totals"));
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void DuplicateIds_Throw()
    {
        _ = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "x", Operation = ReportOperations.AdminReport },
            new FakeReport { Id = "x", Operation = ReportOperations.UserReport },
        });
    }

    [TestMethod]
    public void VisibleTo_SystemAdmin_SeesEverything()
    {
        var catalog = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "admin1", Operation = ReportOperations.AdminReport },
            new FakeReport { Id = "user1", Operation = ReportOperations.UserReport },
        });

        var visible = catalog.VisibleTo(User(isSystemAdmin: true));
        Assert.AreEqual(2, visible.Count);
    }

    [TestMethod]
    public void VisibleTo_Viewer_SeesOnlyUserReports()
    {
        var catalog = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "admin1", Operation = ReportOperations.AdminReport },
            new FakeReport { Id = "user1", Operation = ReportOperations.UserReport },
        });

        var visible = catalog.VisibleTo(User(isSystemAdmin: false, Roles.Viewer));
        Assert.AreEqual(1, visible.Count);
        Assert.AreEqual("user1", visible[0].Id);
    }

    [TestMethod]
    public void VisibleTo_Volunteer_SeesNothing()
    {
        var catalog = new ReportCatalog(new IReportDefinition[]
        {
            new FakeReport { Id = "admin1", Operation = ReportOperations.AdminReport },
            new FakeReport { Id = "user1", Operation = ReportOperations.UserReport },
        });

        var visible = catalog.VisibleTo(User(isSystemAdmin: false, Roles.Volunteer));
        Assert.AreEqual(0, visible.Count);
    }
}
