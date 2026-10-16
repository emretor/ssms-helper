using SsmsHelper.Core.Editor;

namespace SsmsHelper.Core.Tests.Editor;

public class CaretGuardTests
{
    [Theory]
    [InlineData(true, 1, false, true)]   // plain caret
    [InlineData(true, 0, false, true)]
    [InlineData(false, 1, false, false)] // selection: Tab indents
    [InlineData(true, 2, false, false)]  // multi-caret
    [InlineData(true, 1, true, false)]   // virtual space past the end of the line
    public void CanExpand(bool selectionIsEmpty, int spans, bool virtualSpace, bool expected)
    {
        Assert.Equal(expected, CaretGuard.CanExpand(selectionIsEmpty, spans, virtualSpace));
    }
}
