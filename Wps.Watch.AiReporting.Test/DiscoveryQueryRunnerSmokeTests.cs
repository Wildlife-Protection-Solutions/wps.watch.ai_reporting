using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Data;
using Wps.Watch.AiReporting.Discovery;

namespace Wps.Watch.AiReporting.Test;

/// <summary>
/// Live execution smoke tests for <see cref="DiscoveryQueryRunner.RunQueryAsync"/>.
///
/// These guard the regression fixed in 2026-06: a valid SELECT (even <c>SELECT 1</c>)
/// failed because the open <c>DataReader</c> was not closed before the belt-and-suspenders
/// transaction was rolled back, and SqlClient (no MARS) refuses a command on a connection
/// with an open reader. They run against the same DbContext configuration the host uses
/// (SqlServer + EnableRetryOnFailure + NoTracking), so they reproduce the SqlClient-specific
/// behaviour rather than masking it behind an in-memory provider.
///
/// The WpsReplica database needs network access + Active Directory Default auth (`az login`),
/// which isn't available in every CI environment, so the tests mark themselves
/// <see cref="Assert.Inconclusive(string)"/> when the database can't be reached instead of
/// failing the build. Where the database IS reachable (any dev machine / devqa runner) they
/// assert that rows actually come back.
/// </summary>
[TestClass]
public sealed class DiscoveryQueryRunnerSmokeTests
{
    // Mirrors appsettings.json. Override via the ConnectionStrings__WpsReplica env var.
    private const string DefaultConnectionString =
        "Server=tcp:wpswatch-devqa.database.windows.net,1433;Database=wpswatch-dev;" +
        "Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__WpsReplica") ?? DefaultConnectionString;

    [TestMethod]
    public async Task QueryData_SelectConstant_ReturnsOneRow()
    {
        await EnsureReachableAsync();
        await using var db = NewContext();
        var runner = NewRunner(db);

        var outcome = await runner.RunQueryAsync("SELECT 1 AS One", CancellationToken.None);

        var success = outcome as DiscoveryQueryOutcome.Success;
        Assert.IsNotNull(success, $"Expected Success but got {Describe(outcome)}");
        Assert.AreEqual(1, success!.RowCount, "SELECT 1 should return exactly one row.");
        Assert.AreEqual(1, Convert.ToInt32(success.Rows[0]["One"]));
    }

    [TestMethod]
    public async Task QueryData_RealAggregate_ReturnsRow()
    {
        await EnsureReachableAsync();
        await using var db = NewContext();
        var runner = NewRunner(db);

        var outcome = await runner.RunQueryAsync(
            "SELECT COUNT(*) AS Total FROM Organization", CancellationToken.None);

        var success = outcome as DiscoveryQueryOutcome.Success;
        Assert.IsNotNull(success, $"Expected Success but got {Describe(outcome)}");
        Assert.AreEqual(1, success!.RowCount);
        Assert.IsTrue(success.Rows[0].ContainsKey("Total"));
    }

    private static WpsReplicaDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<WpsReplicaDbContext>()
            .UseSqlServer(ConnectionString, sql =>
            {
                sql.CommandTimeout(15);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new WpsReplicaDbContext(options);
    }

    private static DiscoveryQueryRunner NewRunner(WpsReplicaDbContext db)
    {
        var accessor = new UserContextAccessor();
        accessor.Set(new UserContext
        {
            UserId = "test-admin",
            OrganizationIds = Array.Empty<int>(),
            RoleNames = Array.Empty<string>(),
            IsSystemAdmin = true,
        });

        var options = Options.Create(new DiscoveryOptions
        {
            Enabled = true,
            MaxRows = 5000,
            CommandTimeoutSeconds = 15,
        });

        return new DiscoveryQueryRunner(db, accessor, options, NullLogger<DiscoveryQueryRunner>.Instance);
    }

    private static async Task EnsureReachableAsync()
    {
        try
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
        }
        catch (Exception ex)
        {
            Assert.Inconclusive(
                "WpsReplica database is not reachable in this environment; skipping the live " +
                $"execution smoke test. ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static string Describe(DiscoveryQueryOutcome outcome) => outcome switch
    {
        DiscoveryQueryOutcome.Failed f => $"Failed: {f.Reason}",
        DiscoveryQueryOutcome.Rejected r => $"Rejected: {r.Reason}",
        _ => outcome.GetType().Name,
    };
}
