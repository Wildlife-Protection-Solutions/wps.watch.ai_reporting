using System.Globalization;
using System.Text;

namespace Wps.Watch.AiReporting.Discovery;

/// <summary>
/// Renders a tabular result to CSV for the "download for verification" affordance.
/// Includes the CSV formula-injection guard (security DR-V5-20 / S-MUST-23):
/// cells beginning with =, +, -, @, tab, or CR are prefixed with an apostrophe so
/// spreadsheet apps don't evaluate them as formulas.
/// </summary>
public static class CsvExporter
{
    public static string ToCsv(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(EscapeCell)));

        foreach (var row in rows)
        {
            var cells = columns.Select(c =>
                EscapeCell(row.TryGetValue(c, out var v) ? FormatValue(v) : string.Empty));
            sb.AppendLine(string.Join(",", cells));
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string EscapeCell(string raw)
    {
        var cell = raw;

        // Formula-injection guard.
        if (cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            cell = "'" + cell;
        }

        // RFC-4180 quoting.
        if (cell.Contains('"') || cell.Contains(',') || cell.Contains('\n') || cell.Contains('\r'))
        {
            cell = "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        return cell;
    }
}
