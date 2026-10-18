using System;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SsmsHelper.Core.Results;

/// <summary>
/// Writes a result grid value as a T-SQL literal for its server type: NULL, numbers as is, N'unicode' and 'ansi'
/// strings, ISO dates, 0x binary. Values may arrive typed (int, DateTime, byte[]) or already as display strings;
/// both give the same literal.
/// </summary>
public static class SqlLiteral
{
    private static readonly Regex NumberText = new(@"^-?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex SpacedDate = new(@"^\d{4}-\d{2}-\d{2} \d", RegexOptions.CultureInvariant);
    private static readonly Regex BinaryText = new(@"^0x[0-9A-Fa-f]*$", RegexOptions.CultureInvariant);

    /// <param name="value">The cell value; null or DBNull is NULL.</param>
    /// <param name="typeName">Server type name ("int", "nvarchar", "datetime2"); unknown types are written as N'...'.</param>
    public static string Format(object? value, string? typeName)
    {
        if (value is null || value is DBNull || value is INullable { IsNull: true })
        {
            return "NULL";
        }

        var type = (typeName ?? string.Empty).ToLowerInvariant();
        value = Unwrap(value);
        switch (value)
        {
            case bool b:
                return b ? "1" : "0";
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case decimal d:
                return d.ToString(CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case DateTime dt:
                return Quote(FormatDateTime(dt, type), unicode: false);
            case DateTimeOffset dto:
                return Quote(FormatDateTime(dto.DateTime, "datetime2") + dto.ToString("zzz", CultureInfo.InvariantCulture), unicode: false);
            case TimeSpan ts:
                return Quote(TrimFraction(ts.ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture)), unicode: false);
            case Guid g:
                return Quote(g.ToString("D").ToUpperInvariant(), unicode: false);
            case byte[] bytes:
                return Hex(bytes);
            case char[] chars:
                return FormatText(new string(chars), type);
        }

        var text = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
        return FormatText(text, type);
    }

    /// <summary>SqlInt32, SqlDecimal, SqlString, ...: the CLR value inside. SqlDecimal keeps its own text (38 digits).</summary>
    private static object Unwrap(object value)
    {
        switch (value)
        {
            case SqlDecimal d:
                return d.ToString();
            case SqlMoney m:
                return m.Value;
            case SqlBinary b:
                return b.Value;
            case INullable:
                var property = value.GetType().GetProperty("Value");
                return property?.GetValue(value) ?? value;
            default:
                return value;
        }
    }

    /// <summary>A value that arrived as text: numbers and 0x binary go in bare when their type says so.</summary>
    private static string FormatText(string text, string type)
    {
        switch (type)
        {
            case "bit":
                return text.Equals("True", StringComparison.OrdinalIgnoreCase) ? "1"
                    : text.Equals("False", StringComparison.OrdinalIgnoreCase) ? "0"
                    : NumberText.IsMatch(text) ? text : Quote(text, unicode: false);
            case "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney":
                return NumberText.IsMatch(text) ? text : Quote(text, unicode: false);
            case "binary" or "varbinary" or "image" or "timestamp" or "rowversion":
                return BinaryText.IsMatch(text) ? text : Quote(text, unicode: false);
            case "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset":
                // "2026-10-18 14:05:09" reads as year-day-month under DATEFORMAT dmy; the T form never does.
                return Quote(SpacedDate.IsMatch(text) ? text.Substring(0, 10) + "T" + text.Substring(11) : text, unicode: false);
            case "char" or "varchar" or "text" or "date" or "time" or "uniqueidentifier":
                return Quote(text, unicode: false);
            default:
                return Quote(text, unicode: true); // nvarchar, nchar, ntext, xml, sysname, sql_variant, CLR types
        }
    }

    private static string FormatDateTime(DateTime value, string type) => type switch
    {
        "date" => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "datetime" => value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
        "smalldatetime" => value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        _ => TrimFraction(value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
    };

    /// <summary>"10:00:00.1200000" to "10:00:00.12"; "10:00:00.0000000" to "10:00:00".</summary>
    private static string TrimFraction(string text)
    {
        var dot = text.LastIndexOf('.');
        if (dot < 0)
        {
            return text;
        }

        var trimmed = text.TrimEnd('0');
        return trimmed.EndsWith(".", StringComparison.Ordinal) ? trimmed.Substring(0, trimmed.Length - 1) : trimmed;
    }

    private static string Quote(string text, bool unicode) => (unicode ? "N'" : "'") + text.Replace("'", "''") + "'";

    private static string Hex(byte[] bytes)
    {
        var builder = new StringBuilder(2 + bytes.Length * 2).Append("0x");
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
