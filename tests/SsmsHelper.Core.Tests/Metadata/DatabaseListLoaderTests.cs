using System.Data;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

public class DatabaseListLoaderTests
{
    [Fact]
    public async Task UserDatabasesFirst_ThenSystem_EachByName()
    {
        var table = new DataTable();
        table.Columns.Add("DatabaseName", typeof(string));
        table.Columns.Add("IsSystem", typeof(bool));
        table.Rows.Add("tempdb", true);
        table.Rows.Add("Shop", false);
        table.Rows.Add("master", true);
        table.Rows.Add("archive", false);

        var list = await DatabaseListLoader.ReadAsync(table.CreateDataReader(), DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "archive", "Shop", "master", "tempdb" }, list.Databases.Select(d => d.Name));
        Assert.Equal(new[] { false, false, true, true }, list.Databases.Select(d => d.IsSystem));
        Assert.Equal(DateTimeOffset.UnixEpoch, list.LoadedAt);
    }
}
