using System.IO;
using System.Xml.Linq;
using SsmsHelper.Core;
using Xunit;

namespace SsmsHelper.Core.Tests;

public class InstallLocationTests
{
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\SSMS\22.0_f68b7164\Extensions\30whqcwx.dqc\SsmsHelper.dll", "f68b7164")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\SSMS\21.0_7c0b6768\Extensions\2ej34wy0.wvt\SsmsHelper.dll", "7c0b6768")]
    [InlineData(@"c:\users\me\appdata\local\microsoft\ssms\22.0_f68b7164\extensions\abrok3ey.oji\SsmsHelper.dll", "f68b7164")] // as SSMS loads it
    [InlineData(@"D:\dev\SsmsHelper\src\SsmsHelper.Vsix\bin\Release\net48\SsmsHelper.dll", null)]
    [InlineData(@"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\SsmsHelper\SsmsHelper.dll", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void InstanceId_IsReadFromTheExtensionFolder(string? path, string? expected)
    {
        Assert.Equal(expected, InstallLocation.InstanceId(path));
    }

    [Fact]
    public void ExtensionId_MatchesTheManifestIdentity()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SsmsHelper.slnx")))
        {
            root = root.Parent;
        }

        Assert.SkipWhen(root is null, "Repository root not found.");
        var manifest = XDocument.Load(Path.Combine(root!.FullName, "src", "SsmsHelper.Vsix", "source.extension.vsixmanifest"));
        XNamespace ns = "http://schemas.microsoft.com/developer/vsx-schema/2011";
        Assert.Equal(InstallLocation.ExtensionId, (string?)manifest.Root!.Element(ns + "Metadata")!.Element(ns + "Identity")!.Attribute("Id"));
    }
}
