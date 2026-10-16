using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Tests.Parsing;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("CustomerId", "CustomerId")]
    [InlineData("_x1", "_x1")]
    [InlineData("Order", "[Order]")]
    [InlineData("order", "[order]")]
    [InlineData("Line No", "[Line No]")]
    [InlineData("1st", "[1st]")]
    [InlineData("a]b", "[a]]b]")]
    [InlineData("", "[]")]
    public void Quote(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Quote(name));
    }
}
