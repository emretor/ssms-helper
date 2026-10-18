using System.Diagnostics;
using SsmsHelper.Core.Completion;

namespace SsmsHelper.Core.Tests.Completion;

public class CompletionMatcherTests
{
    private static readonly CompletionCandidate[] Objects =
    {
        new("dbo.CustomerOrders", 0),
        new("dbo.Games", 0),
        new("dbo.OrderLines", 0),
        new("dbo.Orders", 0),
        new("sales.Orders", 0),
        new("dbo.[Order Notes]", 0),
        new("dbo.VideoGames", 0),
        new("dbo.vOrders", 1),
        new("dbo.OrdersOf", 2),
    };

    private static string[] Filter(string typed) =>
        CompletionMatcher.Filter(Objects, typed).Select(m => Objects[m.Index].Text).ToArray();

    [Fact]
    public void Tier_KeepsJoinConditionsAboveBetterMatchingColumns()
    {
        var items = new CompletionCandidate[]
        {
            new("a.AddressId = o.ShippingAddressId", 0, 0),
            new("a.AddressId = o.BillingAddressId", 0, 0),
            new("o.ShippingAddressId", 1, 1),
            new("a.AddressId", 2, 1),
        };

        string[] Run(string typed) => CompletionMatcher.Filter(items, typed).Select(m => items[m.Index].Text).ToArray();

        // "Ship" is only inside the condition but at the start of the column; the condition still comes first.
        Assert.Equal(new[] { "a.AddressId = o.ShippingAddressId", "o.ShippingAddressId" }, Run("Ship"));
        Assert.Equal(new[] { "a.AddressId = o.ShippingAddressId", "a.AddressId = o.BillingAddressId", "a.AddressId" }, Run("a.Add"));
    }

    [Fact]
    public void Gam_FindsGamesFirst_ThenNamesContainingIt()
    {
        Assert.Equal(new[] { "dbo.Games", "dbo.VideoGames" }, Filter("gam"));
    }

    [Fact]
    public void StartsWith_ShorterNamesFirst_TablesBeforeViewsAndFunctions_ThenContains()
    {
        Assert.Equal(
            new[] { "dbo.Orders", "sales.Orders", "dbo.OrderLines", "dbo.[Order Notes]", "dbo.OrdersOf", "dbo.CustomerOrders", "dbo.vOrders" },
            Filter("order"));
    }

    [Fact]
    public void ExactName_ComesFirst()
    {
        Assert.Equal("dbo.Orders", Filter("orders")[0]);
        Assert.Equal("dbo.[Order Notes]", Filter("Order Notes")[0]);
    }

    [Fact]
    public void WithADot_MatchesTheWholeName()
    {
        Assert.Equal(new[] { "sales.Orders" }, Filter("sales.ord"));
        Assert.Equal(new[] { "dbo.CustomerOrders", "dbo.Games", "dbo.OrderLines", "dbo.Orders", "dbo.[Order Notes]", "dbo.VideoGames", "dbo.vOrders", "dbo.OrdersOf" },
            Filter("dbo."));
    }

    [Fact]
    public void SchemaOnly_StillMatches_AfterNameMatches()
    {
        Assert.Equal(new[] { "sales.Orders" }, Filter("sales"));
    }

    [Fact]
    public void NothingTyped_KeepsTheOriginalOrder_GroupedByKind()
    {
        Assert.Equal(Objects.Select(o => o.Text).Where(t => t != "dbo.vOrders" && t != "dbo.OrdersOf").Concat(new[] { "dbo.vOrders", "dbo.OrdersOf" }),
            Filter(""));
    }

    [Fact]
    public void NoMatch_IsEmpty()
    {
        Assert.Empty(Filter("zzz"));
        Assert.Empty(Filter("gam "));
    }

    [Fact]
    public void MatchStart_PointsAtTheMatchedText()
    {
        var match = CompletionMatcher.Filter(Objects, "game").First();
        Assert.Equal(4, match.MatchStart);
        Assert.Equal(4, match.MatchLength);
    }

    [Fact]
    public void Performance_TenThousandTables_FilterWellUnderAFrame()
    {
        var candidates = Enumerable.Range(0, 10_000).Select(i => new CompletionCandidate($"schema{i % 20}.Table{i:D5}Name", i % 3)).ToArray();
        CompletionMatcher.Filter(candidates, "warm");

        var stopwatch = Stopwatch.StartNew();
        foreach (var typed in new[] { "t", "ta", "tab", "table0", "table01", "name", "e1" })
        {
            CompletionMatcher.Filter(candidates, typed);
        }

        stopwatch.Stop();
        var perKeystroke = stopwatch.Elapsed.TotalMilliseconds / 7;
        TestContext.Current.SendDiagnosticMessage($"10,000 tables: {perKeystroke:0.00} ms per keystroke");

        // Budget is 5 ms on a developer machine; the assertion leaves room for slow CI runners.
        Assert.True(perKeystroke < 25, $"{perKeystroke:0.00} ms per keystroke");
    }
}
