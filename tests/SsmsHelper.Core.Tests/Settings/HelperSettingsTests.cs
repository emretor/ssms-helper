using SsmsHelper.Core.Settings;

namespace SsmsHelper.Core.Tests.Settings;

public class HelperSettingsTests
{
    [Fact]
    public void Defaults_HaveEverythingOn_AndThirtyMinuteCache()
    {
        var settings = HelperSettings.Default;
        Assert.True(settings.CompletionList);
        Assert.True(settings.CopyAsInsert);
        Assert.True(settings.CloseQuotes);
        Assert.True(settings.ClauseKeywords);
        Assert.True(settings.Snippets);
        Assert.True(settings.JoinAutoOpen);
        Assert.True(settings.InsertHelper);
        Assert.True(settings.StarExpansion);
        Assert.True(settings.ExecHelper);
        Assert.True(settings.ScriptObject);
        Assert.True(settings.WhereSafetyCheck);
        Assert.True(settings.RefreshWithIntelliSense);
        Assert.True(settings.ReloadAfterDdl);
        Assert.Equal(30, settings.SchemaCacheMinutes);
    }

    [Fact]
    public void EmptyStore_GivesDefaults()
    {
        Assert.Equal(HelperSettings.Default, HelperSettings.FromValues(_ => null));
    }

    [Fact]
    public void RoundTrips()
    {
        var settings = new HelperSettings { CompletionList = false, Snippets = false, ExecHelper = false, ScriptObject = false, CopyAsInsert = false, ClauseKeywords = false, CloseQuotes = false, WhereSafetyCheck = false, RefreshWithIntelliSense = false, ReloadAfterDdl = false, SchemaCacheMinutes = 90 };
        var values = settings.ToValues();
        Assert.Equal(settings, HelperSettings.FromValues(name => values.TryGetValue(name, out var v) ? v : null));
    }

    [Fact]
    public void UnreadableValues_FallBackToDefaults()
    {
        var settings = HelperSettings.FromValues(name => name == nameof(HelperSettings.Snippets) ? "maybe" : "x");
        Assert.Equal(HelperSettings.Default, settings);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("1", 1)]
    [InlineData("1440", 1440)]
    [InlineData("100000", 1440)]
    public void SchemaCacheMinutes_IsKeptInRange(string stored, int expected)
    {
        var settings = HelperSettings.FromValues(name => name == nameof(HelperSettings.SchemaCacheMinutes) ? stored : null);
        Assert.Equal(expected, settings.SchemaCacheMinutes);
        Assert.Equal(expected, (new HelperSettings { SchemaCacheMinutes = int.Parse(stored) }).Normalized().SchemaCacheMinutes);
    }
}
