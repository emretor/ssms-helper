using SsmsHelper.Core;

namespace SsmsHelper.Core.Tests;

public class ProductInfoTests
{
    [Fact]
    public void Version_IsTheBuildVersion_WithoutSourceLinkSuffix()
    {
        Assert.Equal("0.13.18", ProductInfo.Version);
    }

    [Fact]
    public void Name_IsSet()
    {
        Assert.Equal("SSMS Helper", ProductInfo.Name);
    }
}
