using SsmsHelper.Core.Inserts;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Tests.Metadata;

namespace SsmsHelper.Core.Tests.Inserts;

public class InsertGeneratorTests
{
    /// <summary>Same shape as dbo.Orders in samples/ShopDemo.sql.</summary>
    private static SampleSchema Orders() => new SampleSchema()
        .Object(1, "dbo", "Orders")
        .Column(1, 1, "OrderId", identity: true)
        .Column(1, 2, "CustomerId")
        .Column(1, 3, "ShippingAddressId", nullable: true)
        .Column(1, 4, "OrderDate", "datetime2", defaultDefinition: "(sysutcdatetime())", maxLength: 8, precision: 27, scale: 7)
        .Column(1, 5, "Status", "varchar", defaultDefinition: "('New')", maxLength: 20)
        .Column(1, 6, "Total", "money", computed: true)
        .Column(1, 7, "RowVersion", "timestamp")
        .Column(1, 8, "ValidFrom", "datetime2", generatedAlways: 1, maxLength: 8, precision: 27, scale: 7);

    private static async Task<(string Result, int Caret)> Expand(
        string marked, SampleSchema? sample = null, InsertOptions? options = null, InsertLayout layout = InsertLayout.Expanded)
    {
        var caret = marked.IndexOf('|');
        var text = marked.Remove(caret, 1);
        var context = InsertContextAnalyzer.Analyze(text, caret);
        Assert.NotNull(context);
        var schema = await (sample ?? Orders()).LoadAsync();
        var table = schema.FindTable(context.Schema, context.Name);
        Assert.NotNull(table);

        var expansion = InsertGenerator.Generate(context, table, "\n", layout, options);
        var result = text.Substring(0, context.ReplaceStart) + expansion.Text + text.Substring(caret);
        return (result, context.ReplaceStart + expansion.CaretOffset);
    }

    [Fact]
    public async Task SkipsServerGeneratedColumns()
    {
        var table = (await Orders().LoadAsync()).FindTable("dbo", "Orders")!;

        Assert.Equal(new[] { "CustomerId", "ShippingAddressId", "OrderDate", "Status" }, InsertGenerator.InsertableColumns(table).Select(c => c.Name));
        Assert.Equal(
            new[] { "OrderId (identity)", "Total (computed)", "RowVersion (rowversion)", "ValidFrom (generated always)" },
            InsertGenerator.SkippedColumns(table).Select(s => $"{s.Name} ({s.Reason})"));
    }

    [Fact]
    public async Task Values()
    {
        var (result, caret) = await Expand("INSERT INTO dbo.Orders|");

        Assert.Equal(
            """
            INSERT INTO dbo.Orders
            (
                CustomerId,
                ShippingAddressId,
                OrderDate,
                Status
            )
            VALUES
            (
                0,         -- CustomerId int
                0,         -- ShippingAddressId int NULL
                GETDATE(), -- OrderDate datetime2 DEFAULT (sysutcdatetime())
                ''         -- Status varchar(20) DEFAULT ('New')
            );
            """.ReplaceLineEndings("\n"),
            result);
        Assert.Equal(result.IndexOf("0,", StringComparison.Ordinal), caret);
    }

    [Fact]
    public async Task Compact()
    {
        var (result, caret) = await Expand("  INSERT INTO dbo.Orders|", layout: InsertLayout.Compact);

        Assert.Equal(
            "  INSERT INTO dbo.Orders (CustomerId, ShippingAddressId, OrderDate, Status)\n" +
            "  VALUES (0, 0, GETDATE(), '');",
            result);
        Assert.Equal(result.IndexOf("(0", StringComparison.Ordinal) + 1, caret);
    }

    [Fact]
    public async Task Compact_TypedColumnList()
    {
        var (result, _) = await Expand("INSERT INTO dbo.Orders (Status, CustomerId)|", layout: InsertLayout.Compact);

        Assert.Equal("INSERT INTO dbo.Orders (Status, CustomerId)\nVALUES ('', 0);", result);
    }

    [Fact]
    public async Task TypedColumnList_GetsOnlyTheMatchingRow()
    {
        var (result, _) = await Expand("INSERT INTO dbo.Orders (Status, OrderId, Nope) |");

        Assert.Equal(
            """
            INSERT INTO dbo.Orders (Status, OrderId, Nope)
            VALUES
            (
                '',   -- Status varchar(20) DEFAULT ('New')
                0,    -- OrderId int (identity)
                NULL  -- Nope: not a column of this table
            );
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task FollowsTheInsertLineIndent_AndQuotesColumnNames()
    {
        var sample = new SampleSchema()
            .Object(1, "dbo", "Lines").Column(1, 1, "LineNo").Column(1, 2, "Unit Price", "decimal", precision: 10, scale: 2);

        var (result, _) = await Expand("BEGIN\n  INSERT dbo.Lines|\nEND", sample);

        Assert.Equal(
            """
            BEGIN
              INSERT dbo.Lines
              (
                  [LineNo],
                  [Unit Price]
              )
              VALUES
              (
                  0,    -- LineNo int
                  0.00  -- Unit Price decimal(10, 2)
              );
            END
            """.ReplaceLineEndings("\n"),
            result);
    }

    [Fact]
    public async Task OnlyGeneratedColumns_UsesDefaultValues()
    {
        var sample = new SampleSchema().Object(1, "dbo", "Ids").Column(1, 1, "Id", identity: true);

        var (result, _) = await Expand("INSERT INTO dbo.Ids|", sample);

        Assert.Equal("INSERT INTO dbo.Ids DEFAULT VALUES;", result);
    }

    [Fact]
    public async Task NullForNullable_Option()
    {
        var (result, _) = await Expand("INSERT INTO dbo.Orders|", options: new InsertOptions(NullForNullable: true));

        Assert.Contains("NULL,      -- ShippingAddressId int NULL", result);
    }

    [Theory]
    [InlineData("int", 4, 10, 0, "0", "int")]
    [InlineData("bigint", 8, 19, 0, "0", "bigint")]
    [InlineData("bit", 1, 1, 0, "0", "bit")]
    [InlineData("decimal", 9, 18, 4, "0.0000", "decimal(18, 4)")]
    [InlineData("numeric", 9, 18, 0, "0", "numeric(18, 0)")]
    [InlineData("money", 8, 19, 4, "0.00", "money")]
    [InlineData("float", 8, 53, 0, "0.0", "float")]
    [InlineData("nvarchar", 200, 0, 0, "N''", "nvarchar(100)")]
    [InlineData("nvarchar", -1, 0, 0, "N''", "nvarchar(max)")]
    [InlineData("varchar", 50, 0, 0, "''", "varchar(50)")]
    [InlineData("char", 3, 0, 0, "''", "char(3)")]
    [InlineData("date", 3, 10, 0, "GETDATE()", "date")]
    [InlineData("datetime", 8, 23, 3, "GETDATE()", "datetime")]
    [InlineData("datetime2", 6, 23, 3, "GETDATE()", "datetime2(3)")]
    [InlineData("datetimeoffset", 10, 34, 7, "SYSDATETIMEOFFSET()", "datetimeoffset")]
    [InlineData("time", 5, 16, 7, "CAST(GETDATE() AS time)", "time")]
    [InlineData("uniqueidentifier", 16, 0, 0, "NEWID()", "uniqueidentifier")]
    [InlineData("varbinary", -1, 0, 0, "0x", "varbinary(max)")]
    [InlineData("xml", -1, 0, 0, "N''", "xml")]
    [InlineData("geography", -1, 0, 0, "NULL", "geography")]
    public void PlaceholderAndType(string type, short maxLength, byte precision, byte scale, string placeholder, string formatted)
    {
        var column = new ColumnInfo("C", 1, type, type, maxLength, precision, scale, false, false, false, GeneratedAlwaysType.None, null);

        Assert.Equal(placeholder, InsertGenerator.Placeholder(column));
        Assert.Equal(formatted, InsertGenerator.FormatType(column));
    }

    [Fact]
    public void AliasType_ShowsTheAliasAndUsesTheBaseType()
    {
        var column = new ColumnInfo("C", 1, "sysname", "nvarchar", 256, 0, 0, false, false, false, GeneratedAlwaysType.None, null);

        Assert.Equal("N''", InsertGenerator.Placeholder(column));
        Assert.Equal("sysname", InsertGenerator.FormatType(column));
    }
}
