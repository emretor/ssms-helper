using System.Data.SqlTypes;
using SsmsHelper.Core.Results;

namespace SsmsHelper.Core.Tests.Results;

public class SqlLiteralTests
{
    [Fact]
    public void Nulls()
    {
        Assert.Equal("NULL", SqlLiteral.Format(null, "int"));
        Assert.Equal("NULL", SqlLiteral.Format(DBNull.Value, "nvarchar"));
        Assert.Equal("NULL", SqlLiteral.Format(SqlInt32.Null, "int"));
    }

    [Fact]
    public void TypedValues()
    {
        Assert.Equal("42", SqlLiteral.Format(42, "int"));
        Assert.Equal("-7", SqlLiteral.Format(-7L, "bigint"));
        Assert.Equal("1", SqlLiteral.Format(true, "bit"));
        Assert.Equal("12.50", SqlLiteral.Format(12.50m, "decimal"));
        Assert.Equal("0.1", SqlLiteral.Format(0.1d, "float"));
        Assert.Equal("N'O''Brien'", SqlLiteral.Format("O'Brien", "nvarchar"));
        Assert.Equal("'abc'", SqlLiteral.Format("abc", "varchar"));
        Assert.Equal("0x00FF10", SqlLiteral.Format(new byte[] { 0, 255, 16 }, "varbinary"));
        Assert.Equal("0x", SqlLiteral.Format(Array.Empty<byte>(), "varbinary"));
        Assert.Equal("'6F9619FF-8B86-D011-B42D-00C04FC964FF'", SqlLiteral.Format(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"), "uniqueidentifier"));
    }

    [Fact]
    public void SqlTypes_AreUnwrapped()
    {
        Assert.Equal("42", SqlLiteral.Format(new SqlInt32(42), "int"));
        Assert.Equal("12345678901234567890123456789.12345678", SqlLiteral.Format(SqlDecimal.Parse("12345678901234567890123456789.12345678"), "decimal"));
        Assert.Equal("10.5000", SqlLiteral.Format(new SqlMoney(10.5m), "money"));
        Assert.Equal("0", SqlLiteral.Format(SqlBoolean.False, "bit"));
        Assert.Equal("N'x'", SqlLiteral.Format(new SqlString("x"), "nvarchar"));
        Assert.Equal("0x0A", SqlLiteral.Format(new SqlBinary(new byte[] { 10 }), "varbinary"));
        Assert.Equal("NULL", SqlLiteral.Format(SqlString.Null, "nvarchar"));
    }

    [Fact]
    public void Dates_IsoPerType()
    {
        var value = new DateTime(2026, 10, 18, 14, 5, 9, 120);
        Assert.Equal("'2026-10-18'", SqlLiteral.Format(value.Date, "date"));
        Assert.Equal("'2026-10-18T14:05:09.120'", SqlLiteral.Format(value, "datetime"));
        Assert.Equal("'2026-10-18T14:05:09'", SqlLiteral.Format(value, "smalldatetime"));
        Assert.Equal("'2026-10-18T14:05:09.12'", SqlLiteral.Format(value, "datetime2"));
        Assert.Equal("'2026-10-18T14:05:09'", SqlLiteral.Format(new DateTime(2026, 10, 18, 14, 5, 9), "datetime2"));
        Assert.Equal("'2026-10-18T14:05:09+02:00'",
            SqlLiteral.Format(new DateTimeOffset(2026, 10, 18, 14, 5, 9, TimeSpan.FromHours(2)), "datetimeoffset"));
        Assert.Equal("'08:30:00'", SqlLiteral.Format(new TimeSpan(8, 30, 0), "time"));
    }

    [Fact]
    public void TextValues_FollowTheServerType()
    {
        Assert.Equal("42", SqlLiteral.Format("42", "int"));
        Assert.Equal("12.5", SqlLiteral.Format("12.5", "decimal"));
        Assert.Equal("1", SqlLiteral.Format("True", "bit"));
        Assert.Equal("0x0102", SqlLiteral.Format("0x0102", "varbinary"));
        Assert.Equal("'2026-10-18T14:05:09.120'", SqlLiteral.Format("2026-10-18 14:05:09.120", "datetime"));
        Assert.Equal("'2026-10-18T14:05:09.1234567 +02:00'", SqlLiteral.Format("2026-10-18 14:05:09.1234567 +02:00", "datetimeoffset"));
        Assert.Equal("'2026-10-18'", SqlLiteral.Format("2026-10-18", "date"));
        Assert.Equal("N'Zürich'", SqlLiteral.Format("Zürich", "nvarchar"));
        Assert.Equal("N'<a/>'", SqlLiteral.Format("<a/>", "xml"));
        Assert.Equal("N'/1/2/'", SqlLiteral.Format("/1/2/", "hierarchyid"));
        Assert.Equal("'not a number'", SqlLiteral.Format("not a number", "int"));
    }
}
