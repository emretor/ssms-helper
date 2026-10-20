using System.Text.RegularExpressions;

namespace SsmsHelper.Core;

/// <summary>Where the extension is installed: %LocalAppData%\Microsoft\SSMS\22.0_f68b7164\Extensions\&lt;random&gt;.</summary>
public static class InstallLocation
{
    /// <summary>The VSIX identity in source.extension.vsixmanifest (the same for the SSMS 21 and 22 packages).</summary>
    public const string ExtensionId = "SsmsHelper.a8e1d928-b079-4c18-acf5-603c10e1b44b";

    private static readonly Regex InstanceFolder = new(@"[\\/]\d+\.\d+_([0-9a-fA-F]+)[\\/]Extensions[\\/]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>The SSMS instance ID ("f68b7164") from a path inside the extension folder, or null when not installed per user.</summary>
    public static string? InstanceId(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var match = InstanceFolder.Match(path);
        return match.Success ? match.Groups[1].Value : null;
    }
}
