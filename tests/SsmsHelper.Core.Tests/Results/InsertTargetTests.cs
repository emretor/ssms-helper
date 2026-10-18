using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Results;
using SsmsHelper.Core.Tests.Metadata;

namespace SsmsHelper.Core.Tests.Results;

public class InsertTargetTests
{
    private static TableReference Source(string text) => ResultSourceFinder.Find("SELECT * FROM " + text, 0, 1)!;

    private static Task<DatabaseSchema> Shop() => SampleSchema.Shop().LoadAsync();

    [Fact]
    public async Task KnownTable_WithAllColumns_UsesTheSchemaName()
    {
        var target = InsertTarget.Resolve(Source("orders"), await Shop(), new[] { "OrderId", "customerid" });

        Assert.Equal("dbo.Orders", target.Text);
        Assert.NotNull(target.Table);
    }

    [Fact]
    public async Task KnownTable_ColumnNotInIt_Placeholder()
    {
        var target = InsertTarget.Resolve(Source("dbo.Orders"), await Shop(), new[] { "OrderId", "Doubled" });

        Assert.True(target.IsPlaceholder);
    }

    [Fact]
    public async Task UnknownSource_Placeholder()
    {
        Assert.True(InsertTarget.Resolve(null, await Shop(), new[] { "x" }).IsPlaceholder);
        Assert.True(InsertTarget.Resolve(Source("dbo.Nope"), await Shop(), new[] { "x" }).IsPlaceholder);
    }

    [Fact]
    public async Task TempTables_OtherDatabases_AndNoSchema_AsWritten()
    {
        Assert.Equal("#work", InsertTarget.Resolve(Source("#work"), await Shop(), new[] { "x" }).Text);
        Assert.Equal("Archive.dbo.Orders", InsertTarget.Resolve(Source("Archive.dbo.Orders"), await Shop(), new[] { "x" }).Text);
        Assert.Equal("dbo.Orders", InsertTarget.Resolve(Source("dbo.Orders"), null, new[] { "x" }).Text);
    }

    [Fact]
    public async Task Column_FlagsFromTheTable()
    {
        var target = InsertTarget.Resolve(Source("dbo.Orders"), await Shop(), new[] { "OrderId", "Version" });

        var id = target.Column("OrderId", "int", resultSaysIdentity: false, resultSaysComputed: false);
        Assert.True(id.IsIdentity);
        Assert.False(id.CannotInsert);
        Assert.True(target.Column("Version", "timestamp", false, true).CannotInsert);
    }

    [Fact]
    public void Column_WithoutTable_FromTheResult()
    {
        var target = InsertTarget.Resolve(null, null, new[] { "Id" });

        Assert.True(target.Column("Id", "int", resultSaysIdentity: true, resultSaysComputed: false).IsIdentity);
        Assert.False(target.Column("Id", "int", resultSaysIdentity: true, resultSaysComputed: false).CannotInsert);
        Assert.True(target.Column("Doubled", "int", resultSaysIdentity: false, resultSaysComputed: true).CannotInsert);
        Assert.True(target.Column("Version", "timestamp", false, false).CannotInsert);
        Assert.False(target.Column("Name", "nvarchar", false, false).CannotInsert);
    }
}
