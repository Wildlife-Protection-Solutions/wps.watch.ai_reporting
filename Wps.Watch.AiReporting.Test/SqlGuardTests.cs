using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wps.Watch.AiReporting.Discovery;

namespace Wps.Watch.AiReporting.Test;

[TestClass]
public sealed class SqlGuardTests
{
    [TestMethod]
    [DataRow("SELECT 1")]
    [DataRow("select top 10 * from Device")]
    [DataRow(";WITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [DataRow("SELECT * FROM Site WHERE DecommissionDate IS NULL")]
    [DataRow("SELECT CreatedDateUtc, UpdatedAtUtc, IsDeleted FROM Deployment")]
    [DataRow("SELECT COUNT(*) FROM Photo WHERE CaptureDateTimeUtc > '2025-01-01';")]
    public void Validate_accepts_read_only_selects(string sql)
    {
        var result = SqlGuard.Validate(sql);
        Assert.IsTrue(result.IsValid, $"Expected valid: {sql} (reason: {result.Reason})");
    }

    [TestMethod]
    [DataRow("INSERT INTO Site (Name) VALUES ('x')")]
    [DataRow("UPDATE Site SET Name = 'x'")]
    [DataRow("DELETE FROM Site")]
    [DataRow("DROP TABLE Site")]
    [DataRow("ALTER TABLE Site ADD col INT")]
    [DataRow("TRUNCATE TABLE Site")]
    [DataRow("MERGE Site AS t USING src AS s ON 1=1 WHEN MATCHED THEN DELETE")]
    [DataRow("SELECT * INTO SiteCopy FROM Site")]
    [DataRow("SELECT 1; DELETE FROM Site")]
    [DataRow("EXEC sp_who")]
    [DataRow("SELECT * FROM OPENROWSET('a','b','c')")]
    [DataRow("WAITFOR DELAY '00:00:05'")]
    public void Validate_rejects_writes_and_dangerous_statements(string sql)
    {
        Assert.IsFalse(SqlGuard.Validate(sql).IsValid, $"Expected rejected: {sql}");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(";")]
    [DataRow("GRANT SELECT ON Site TO public")]
    public void Validate_rejects_empty_or_non_select(string sql)
    {
        Assert.IsFalse(SqlGuard.Validate(sql).IsValid, $"Expected rejected: '{sql}'");
    }

    [TestMethod]
    [DataRow("DasToken")]
    [DataRow("dasToken")]
    [DataRow("SmartIntegrateToken")]
    [DataRow("PasswordHash")]
    [DataRow("ApiKey")]
    [DataRow("SlackWebhookUrl")]
    [DataRow("SecurityStamp")]
    [DataRow("ConcurrencyStamp")]
    public void IsSensitiveColumn_flags_secrets(string column)
    {
        Assert.IsTrue(SqlGuard.IsSensitiveColumn(column), column);
    }

    [TestMethod]
    [DataRow("Latitude")]
    [DataRow("Longitude")]
    [DataRow("DeviceName")]
    [DataRow("OrganizationId")]
    [DataRow("BatteryLevel")]
    public void IsSensitiveColumn_allows_normal_columns(string column)
    {
        Assert.IsFalse(SqlGuard.IsSensitiveColumn(column), column);
    }

    [TestMethod]
    [DataRow("User")]
    [DataRow("AspNetUsers")]
    [DataRow("Key")]
    [DataRow("ImageRecognitionService")]
    public void IsSensitiveTable_flags_identity_and_secret_tables(string table)
    {
        Assert.IsTrue(SqlGuard.IsSensitiveTable(table), table);
    }

    [TestMethod]
    [DataRow("Site")]
    [DataRow("Device")]
    [DataRow("Photo")]
    [DataRow("Deployment")]
    public void IsSensitiveTable_allows_operational_tables(string table)
    {
        Assert.IsFalse(SqlGuard.IsSensitiveTable(table), table);
    }
}
