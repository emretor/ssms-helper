using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Selects;

namespace SsmsHelper.Core.Tests.Editor;

public class StarKeyRulesTests
{
    [Theory]
    [InlineData(EditorKey.Tab, false, StarLayout.OnePerLine)]     // default: wide
    [InlineData(EditorKey.Return, true, StarLayout.Compact)]      // Shift+Enter: compact, as for INSERT
    [InlineData(EditorKey.Return, false, null)]                   // plain Enter keeps starting a new line
    [InlineData(EditorKey.TypeChar, false, null)]
    public void BeforeKey(EditorKey key, bool shiftOnly, StarLayout? expected)
    {
        Assert.Equal(expected, StarKeyRules.BeforeKey(key, shiftOnly));
    }
}
