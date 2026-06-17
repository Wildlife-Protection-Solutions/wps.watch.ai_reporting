using Wps.Watch.AiReporting.Authorization;

namespace Wps.Watch.AiReporting.Test;

[TestClass]
public class UserContextTests
{
    private static UserContext MakeUser(bool isSystemAdmin, params string[] roles) => new()
    {
        UserId = "u",
        OrganizationIds = Array.Empty<int>(),
        RoleNames = roles,
        IsSystemAdmin = isSystemAdmin,
    };

    [TestMethod]
    public void SystemAdmin_Can_AdminReport()
    {
        var user = MakeUser(isSystemAdmin: true);
        Assert.IsTrue(user.Can(ReportOperations.AdminReport));
    }

    [TestMethod]
    public void SystemAdmin_Can_UserReport()
    {
        var user = MakeUser(isSystemAdmin: true);
        Assert.IsTrue(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void Viewer_Cannot_AdminReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.Viewer);
        Assert.IsFalse(user.Can(ReportOperations.AdminReport));
    }

    [TestMethod]
    public void Viewer_Can_UserReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.Viewer);
        Assert.IsTrue(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void OrgAdmin_IncidentManager_GpsViewer_Can_UserReport()
    {
        foreach (var role in new[] { Roles.OrgAdmin, Roles.IncidentManager, Roles.GpsViewer })
        {
            var user = MakeUser(isSystemAdmin: false, role);
            Assert.IsTrue(user.Can(ReportOperations.UserReport),
                $"Role '{role}' should be allowed UserReport.");
        }
    }

    [TestMethod]
    public void Volunteer_Cannot_AdminReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.Volunteer);
        Assert.IsFalse(user.Can(ReportOperations.AdminReport));
    }

    [TestMethod]
    public void Volunteer_Cannot_UserReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.Volunteer);
        Assert.IsFalse(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void PhotoUploader_Cannot_UserReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.PhotoUploader);
        Assert.IsFalse(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void PhotoTagger_Cannot_UserReport()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.PhotoTagger);
        Assert.IsFalse(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void NoRoles_Cannot_AnyReport()
    {
        var user = MakeUser(isSystemAdmin: false);
        Assert.IsFalse(user.Can(ReportOperations.AdminReport));
        Assert.IsFalse(user.Can(ReportOperations.UserReport));
    }

    [TestMethod]
    public void UnknownOperation_Returns_False()
    {
        var user = MakeUser(isSystemAdmin: false, Roles.OrgAdmin);
        Assert.IsFalse(user.Can("DoesNotExist"));
    }
}
