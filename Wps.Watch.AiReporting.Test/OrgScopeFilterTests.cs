using Wps.Watch.AiReporting.Reports;

namespace Wps.Watch.AiReporting.Test;

[TestClass]
public class OrgScopeFilterTests
{
    [TestMethod]
    public void SystemAdmin_GetsUnrestrictedFilter()
    {
        var filter = ReportResults.BuildOrgScopeFilter(
            organizationIds: new[] { 1, 2 },
            isSystemAdmin: true,
            orgIdColumn: "o.OrganizationId");
        Assert.AreEqual("1 = 1", filter);
    }

    [TestMethod]
    public void NonAdmin_WithNoOrgs_GetsDenyFilter()
    {
        var filter = ReportResults.BuildOrgScopeFilter(
            organizationIds: Array.Empty<int>(),
            isSystemAdmin: false,
            orgIdColumn: "o.OrganizationId");
        Assert.AreEqual("1 = 0", filter);
    }

    [TestMethod]
    public void NonAdmin_WithOrgs_GetsInList()
    {
        var filter = ReportResults.BuildOrgScopeFilter(
            organizationIds: new[] { 5, 7, 9 },
            isSystemAdmin: false,
            orgIdColumn: "o.OrganizationId");
        Assert.AreEqual("o.OrganizationId IN (5,7,9)", filter);
    }

    [TestMethod]
    public void Filter_OnlyContainsIntegersFromInput()
    {
        // Sanity check: the filter is composed of literal integers from the
        // strongly-typed list, so it can't smuggle SQL even if a caller passed
        // odd values. Pass extreme values and verify the output stays numeric.
        var filter = ReportResults.BuildOrgScopeFilter(
            organizationIds: new[] { int.MinValue, 0, int.MaxValue },
            isSystemAdmin: false,
            orgIdColumn: "o.OrganizationId");
        Assert.AreEqual($"o.OrganizationId IN ({int.MinValue},0,{int.MaxValue})", filter);
    }
}
