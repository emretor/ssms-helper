using SsmsHelper.Core.Editor;
using SsmsHelper.Core.Joins;

namespace SsmsHelper.Core.Tests.Editor;

public class JoinPopupRulesTests
{
    private static JoinContext? Analyze(string marked)
    {
        var caret = marked.IndexOf('|');
        return JoinContextAnalyzer.Analyze(marked.Remove(caret, 1), caret);
    }

    [Theory]
    [InlineData(EditorKey.Up, PopupAction.MoveUp)]
    [InlineData(EditorKey.Down, PopupAction.MoveDown)]
    [InlineData(EditorKey.PageUp, PopupAction.PageUp)]
    [InlineData(EditorKey.PageDown, PopupAction.PageDown)]
    [InlineData(EditorKey.Return, PopupAction.Commit)]
    [InlineData(EditorKey.Tab, PopupAction.Commit)]
    [InlineData(EditorKey.Escape, PopupAction.Close)]
    [InlineData(EditorKey.TypeChar, PopupAction.None)]
    [InlineData(EditorKey.Backspace, PopupAction.None)]
    [InlineData(EditorKey.Delete, PopupAction.None)]
    [InlineData(EditorKey.Other, PopupAction.None)]
    public void KeyWhileOpen(EditorKey key, PopupAction expected)
    {
        Assert.Equal(expected, JoinPopupRules.KeyWhileOpen(key));
    }

    [Theory]
    [InlineData(EditorKey.TypeChar, true)]
    [InlineData(EditorKey.Backspace, true)]
    [InlineData(EditorKey.Delete, true)]
    [InlineData(EditorKey.Return, false)]
    [InlineData(EditorKey.Up, false)]
    [InlineData(EditorKey.Other, false)]
    public void Refilters(EditorKey key, bool expected)
    {
        Assert.Equal(expected, JoinPopupRules.Refilters(key));
    }

    [Theory]
    [InlineData("JOIN dbo.Orders o ON ", true)]
    [InlineData("on ", true)]
    [InlineData("    ON ", true)]
    [InlineData("JOIN dbo.Orders o ", true)]
    [InlineData("left join Orders ", true)]
    [InlineData("SELECT * FROM a, b ", false)]
    [InlineData("WHERE x = 1 ", false)]
    [InlineData("SET ACTION ", false)]          // ON inside a word
    [InlineData("SELECT JOINED ", false)]       // JOIN inside a word
    [InlineData("SELECT @JOIN ", false)]
    [InlineData("ON", false)]                   // too short: needs the typed space
    [InlineData("  ", false)]
    public void MayEndJoinHeader(string lineBeforeCaret, bool expected)
    {
        Assert.Equal(expected, JoinPopupRules.MayEndJoinHeader(lineBeforeCaret));
    }

    [Theory]
    [InlineData("SELECT * FROM Customers c JOIN Orders o ON |", true)]
    [InlineData("SELECT * FROM Customers c JOIN Orders o |", true)]
    [InlineData("SELECT * FROM Customers c JOIN Orders AS o |", true)]
    [InlineData("SELECT * FROM Customers c JOIN Orders |", false)]     // alias probably follows
    [InlineData("SELECT * FROM Customers c JOIN Orders o ON o.Cu|", false)] // already typing a condition
    [InlineData("SELECT * FROM Customers c WHERE |", false)]
    public void ShouldAutoOpen(string marked, bool expected)
    {
        Assert.Equal(expected, JoinPopupRules.ShouldAutoOpen(Analyze(marked)));
    }

    [Fact]
    public void ShouldAutoOpen_NoContext()
    {
        Assert.False(JoinPopupRules.ShouldAutoOpen(null));
    }

    [Fact]
    public void KeepsOpen_WhileTypingTheSameCondition()
    {
        var opened = Analyze("FROM Customers c JOIN Orders o ON |");
        var typing = Analyze("FROM Customers c JOIN Orders o ON o.Cu|");

        // Opening "ON |" and typing "o.Cu" share the replace start, so the popup refilters.
        Assert.True(JoinPopupRules.KeepsOpen(typing, opened));
    }

    [Fact]
    public void KeepsOpen_FalseWhenTheContextKindChanges()
    {
        var opened = Analyze("FROM Customers c JOIN Orders o |");
        var typedOn = Analyze("FROM Customers c JOIN Orders o ON|");

        Assert.False(JoinPopupRules.KeepsOpen(typedOn, opened));
    }

    [Fact]
    public void KeepsOpen_FalseWhenTheCaretLeftTheJoin()
    {
        var opened = Analyze("FROM Customers c JOIN Orders o ON |");

        Assert.False(JoinPopupRules.KeepsOpen(Analyze("FROM Customers c JOIN Orders o ON o.CustomerId = c.CustomerId |"), opened));
        Assert.False(JoinPopupRules.KeepsOpen(null, opened));
        Assert.False(JoinPopupRules.KeepsOpen(opened, null));
    }

    [Fact]
    public void Filter_IsCaseInsensitiveSubstring()
    {
        var suggestions = new[]
        {
            new JoinSuggestion("a.AddressId = o.BillingAddressId", "FK_Orders_BillingAddress", JoinSuggestionKind.ForeignKey),
            new JoinSuggestion("a.AddressId = o.ShippingAddressId", "FK_Orders_ShippingAddress", JoinSuggestionKind.ForeignKey),
        };

        Assert.Same(suggestions, JoinPopupRules.Filter(suggestions, string.Empty));
        Assert.Equal(new[] { suggestions[1] }, JoinPopupRules.Filter(suggestions, "ship"));
        Assert.Equal(suggestions, JoinPopupRules.Filter(suggestions, "A.ADDRESS"));
        Assert.Empty(JoinPopupRules.Filter(suggestions, "x.Nope"));
    }
}
