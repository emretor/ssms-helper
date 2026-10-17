using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Execs;

namespace SsmsHelper.Core.Tests.Editor;

public class ExecKeyRulesTests
{
    [Theory]
    [InlineData(EditorKey.Tab, false, ExecLayout.Expanded)]
    [InlineData(EditorKey.Return, true, ExecLayout.Compact)]   // Shift+Enter
    [InlineData(EditorKey.Return, false, null)]                // plain Enter keeps starting a new line
    [InlineData(EditorKey.TypeChar, false, null)]
    public void BeforeKey(EditorKey key, bool shiftOnly, ExecLayout? expected)
    {
        Assert.Equal(expected, ExecKeyRules.BeforeKey(key, shiftOnly));
    }

    [Theory]
    [InlineData(EditorKey.Return, false, ExecLayout.Expanded)]  // Enter picking from the IntelliSense list acts like Tab
    [InlineData(EditorKey.Tab, false, ExecLayout.Expanded)]
    [InlineData(EditorKey.Return, true, ExecLayout.Compact)]    // Shift+Enter
    public void AfterKeyLayout(EditorKey key, bool shiftOnly, ExecLayout expected)
    {
        Assert.Equal(expected, ExecKeyRules.AfterKeyLayout(key, shiftOnly));
    }
}
