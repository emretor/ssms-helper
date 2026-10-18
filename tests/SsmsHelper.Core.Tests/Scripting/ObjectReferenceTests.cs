using SsmsHelper.Core.Scripting;

namespace SsmsHelper.Core.Tests.Scripting;

public class ObjectReferenceTests
{
    [Theory]
    [InlineData("EXEC dbo.GetOrders @CustomerId = 1", 7, null, "dbo", "GetOrders", "dbo.GetOrders")]       // in the schema
    [InlineData("EXEC dbo.GetOrders @CustomerId = 1", 12, null, "dbo", "GetOrders", "dbo.GetOrders")]      // in the name
    [InlineData("EXEC dbo.GetOrders", 18, null, "dbo", "GetOrders", "dbo.GetOrders")]                      // right after it
    [InlineData("SELECT * FROM Orders o", 16, null, null, "Orders", "Orders")]
    [InlineData("SELECT * FROM [dbo].[Order Lines]", 25, null, "dbo", "Order Lines", "[dbo].[Order Lines]")]
    [InlineData("SELECT * FROM Shop.dbo.Orders", 15, "Shop", "dbo", "Orders", "Shop.dbo.Orders")]
    public void FindsTheNameAroundTheCaret(string line, int caret, string? database, string? schema, string name, string text)
    {
        var reference = ObjectReference.At(line, caret);
        Assert.Equal(new ObjectReference(database, schema, name, text), reference);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders", 3)]        // on a keyword: still an identifier token, the server decides
    public void KeywordsAreReturned_TheServerFindsNoObject(string line, int caret)
    {
        Assert.NotNull(ObjectReference.At(line, caret));
    }

    [Theory]
    [InlineData("SELECT * FROM #work", 16)]
    [InlineData("SELECT * FROM srv.db.dbo.Orders", 20)]
    [InlineData("SELECT 'dbo.Orders'", 10)]
    [InlineData("SELECT @x", 8)]
    [InlineData("SELECT 1 +   2", 11)]
    [InlineData("", 0)]
    public void NothingToScript(string line, int caret)
    {
        Assert.Null(ObjectReference.At(line, caret));
    }

    [Fact]
    public void SpacesAroundTheDot_EndTheName()
    {
        Assert.Equal("Orders", ObjectReference.At("SELECT dbo . Orders", 15)!.Text);
    }

    [Fact]
    public void LocalText_QuotesOnlyWhenNeeded()
    {
        Assert.Equal("dbo.Orders", ObjectReference.At("dbo.Orders", 5)!.LocalText);
        Assert.Equal("dbo.[Order Lines]", ObjectReference.At("[dbo].[Order Lines]", 8)!.LocalText);
        Assert.Equal("Orders", ObjectReference.At("Shop.dbo.Orders", 12)!.LocalText.Split('.').Last());
        Assert.Equal("[Order]", ObjectReference.At("[Order]", 3)!.LocalText);
    }
}
