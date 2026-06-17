using System.Text.RegularExpressions;

namespace Wps.Watch.AiReporting.Discovery;

/// <summary>
/// Pragmatic, Rung-1 safety checks for free-form discovery SQL. This is NOT the
/// production-grade boundary — that's a read-only DB principal plus AST validation
/// (see security perspective S-MUST-17 / coordinate-handling doc). For internal
/// discovery against devqa it provides three things:
///   1. SELECT-only / single-statement validation (no writes, no batches),
///   2. a sensitive-column filter so credential/secret columns never leave the
///      service even if a query references a base table directly,
///   3. a sensitive-table list so <c>describe_schema</c> never advertises identity
///      or secret tables.
///
/// Known Rung-1 limitations (acceptable internal-only / test-data; harden later):
///   - keyword matching is textual, so a string literal containing e.g. "update"
///     would be rejected (rare in discovery queries);
///   - it does not parse the AST, so table-level allow-listing is not enforced —
///     the sensitive-column output filter is what protects secrets at this rung.
/// </summary>
public static class SqlGuard
{
    // Column-name fragments whose VALUES must never leave the service, regardless
    // of role or rung. GPS (Latitude/Longitude) is deliberately NOT here: on devqa
    // it's synthetic/stale (Rung 1). Add lat/long here when cutting over to the
    // production replica (Rung 2) per the coordinate-handling doc.
    private static readonly string[] SensitiveColumnFragments =
    {
        "token", "secret", "password", "apikey",
        "securitystamp", "concurrencystamp", "webhook",
    };

    // Tables never advertised by describe_schema (identity / auth / secrets / internal).
    public static readonly IReadOnlySet<string> SensitiveTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "User", "AspNetUsers", "AspNetRoles", "AspNetUserRoles",
            "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens",
            "AspNetRoleClaims", "Key", "KeyRoles",
            "ImageRecognitionService", "SiteImageRecognitionService",
            "__EFMigrationsHistory", "Version", "sysdiagrams",
        };

    // Word-boundary matching avoids false positives on legitimate identifiers like
    // CreatedDateUtc / UpdatedAtUtc / IsDeleted / DecommissionDate.
    private static readonly Regex ForbiddenKeyword = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|" +
        @"GRANT|REVOKE|DENY|BACKUP|RESTORE|SHUTDOWN|RECONFIGURE|OPENROWSET|" +
        @"OPENDATASOURCE|OPENQUERY|WAITFOR|BULK|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtendedProc = new(
        @"\b(sp_|xp_)\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SqlValidation Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlValidation.Reject("Query is empty.");
        }

        // Trim whitespace and any leading/trailing semicolons. The `;WITH` CTE
        // idiom is common, so leading semicolons are tolerated; internal ones
        // mean multiple statements and are rejected.
        var body = sql.Trim().Trim(';').Trim();

        if (body.Length == 0)
        {
            return SqlValidation.Reject("Query is empty.");
        }

        if (body.Contains(';'))
        {
            return SqlValidation.Reject("Only a single statement is allowed (no ';').");
        }

        if (!body.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !body.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return SqlValidation.Reject("Only SELECT (or WITH … SELECT) queries are allowed.");
        }

        var kw = ForbiddenKeyword.Match(body);
        if (kw.Success)
        {
            return SqlValidation.Reject($"Disallowed keyword: {kw.Value.ToUpperInvariant()}.");
        }

        if (ExtendedProc.IsMatch(body))
        {
            return SqlValidation.Reject("Stored/extended procedures (sp_/xp_) are not allowed.");
        }

        return SqlValidation.Accept(body);
    }

    public static bool IsSensitiveColumn(string columnName)
    {
        foreach (var fragment in SensitiveColumnFragments)
        {
            if (columnName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsSensitiveTable(string tableName) => SensitiveTables.Contains(tableName);
}

/// <summary>Result of <see cref="SqlGuard.Validate"/>.</summary>
public sealed record SqlValidation(bool IsValid, string? NormalizedSql, string? Reason)
{
    public static SqlValidation Accept(string sql) => new(true, sql, null);
    public static SqlValidation Reject(string reason) => new(false, null, reason);
}
