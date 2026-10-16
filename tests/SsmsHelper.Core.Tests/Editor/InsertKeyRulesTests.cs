using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Inserts;

namespace SsmsHelper.Core.Tests.Editor;

public class InsertKeyRulesTests
{
    [Theory]
    [InlineData(EditorKey.Tab, false, InsertLayout.Expanded)]
    [InlineData(EditorKey.Return, true, InsertLayout.Compact)]   // Shift+Enter
    [InlineData(EditorKey.Return, false, null)]                  // plain Enter keeps starting a new line
    [InlineData(EditorKey.TypeChar, false, null)]
    [InlineData(EditorKey.Escape, false, null)]
    public void BeforeKey(EditorKey key, bool shiftOnly, InsertLayout? expected)
    {
        Assert.Equal(expected, InsertKeyRules.BeforeKey(key, shiftOnly));
    }

    [Theory]
    [InlineData(EditorKey.Return, true)]
    [InlineData(EditorKey.Tab, true)]
    [InlineData(EditorKey.TypeChar, false)]
    [InlineData(EditorKey.Escape, false)]
    [InlineData(EditorKey.Other, false)]
    public void MayCommitCompletion(EditorKey key, bool expected)
    {
        Assert.Equal(expected, InsertKeyRules.MayCommitCompletion(key));
    }

    [Theory]
    [InlineData(EditorKey.Return, true, InsertLayout.Compact)]
    [InlineData(EditorKey.Return, false, InsertLayout.Expanded)]
    [InlineData(EditorKey.Tab, false, InsertLayout.Expanded)]
    public void AfterKeyLayout(EditorKey key, bool shiftOnly, InsertLayout expected)
    {
        Assert.Equal(expected, InsertKeyRules.AfterKeyLayout(key, shiftOnly));
    }

    [Fact]
    public void CompletionCommit_PartialNameCompleted()
    {
        // "INSERT INTO Gam" + pick Games + Enter: same line, caret right after "Games".
        Assert.True(InsertKeyRules.LooksLikeCompletionCommit(lineBefore: 4, lineAfter: 4, charBeforeCaret: 's'));
    }

    [Fact]
    public void CompletionCommit_FullyTypedNameCommitted()
    {
        // "INSERT INTO Customers" + pick Customers + Enter: nothing changes but it is still a commit.
        Assert.True(InsertKeyRules.LooksLikeCompletionCommit(4, 4, 's'));
    }

    [Fact]
    public void CompletionCommit_BracketedName()
    {
        Assert.True(InsertKeyRules.LooksLikeCompletionCommit(0, 0, ']'));
    }

    [Fact]
    public void NotACommit_EnterStartedANewLine()
    {
        Assert.False(InsertKeyRules.LooksLikeCompletionCommit(4, 5, null));
        Assert.False(InsertKeyRules.LooksLikeCompletionCommit(4, 5, 's'));
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    public void NotACommit_TabInsertedWhitespace(char before)
    {
        Assert.False(InsertKeyRules.LooksLikeCompletionCommit(4, 4, before));
    }

    [Fact]
    public void NotACommit_StartOfBuffer()
    {
        Assert.False(InsertKeyRules.LooksLikeCompletionCommit(0, 0, null));
    }

    [Fact]
    public void PairUndo_WhenTheCompletionChangedTheName()
    {
        Assert.True(InsertKeyRules.ShouldPairUndo(expanded: true, versionBeforeKey: 10, versionAfterKey: 11));
    }

    [Fact]
    public void NoPairedUndo_WhenTheNameWasAlreadyComplete()
    {
        // A second undo would take back unrelated typing.
        Assert.False(InsertKeyRules.ShouldPairUndo(expanded: true, versionBeforeKey: 10, versionAfterKey: 10));
    }

    [Fact]
    public void NoPairedUndo_WhenNothingWasExpanded()
    {
        Assert.False(InsertKeyRules.ShouldPairUndo(expanded: false, versionBeforeKey: 10, versionAfterKey: 11));
    }

    [Fact]
    public void IsPairedUndo_OnlyAtTheRememberedVersion()
    {
        Assert.True(InsertKeyRules.IsPairedUndo(12, 12));
        Assert.False(InsertKeyRules.IsPairedUndo(12, 13)); // edited since the expansion
        Assert.False(InsertKeyRules.IsPairedUndo(null, 12));
    }
}
