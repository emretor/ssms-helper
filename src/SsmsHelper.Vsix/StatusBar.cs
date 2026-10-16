using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsHelper.Core;

namespace SsmsHelper;

/// <summary>Short messages in the SSMS status bar, prefixed with the product name.</summary>
internal static class StatusBar
{
    public static void Show(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
        {
            statusBar.SetText($"{ProductInfo.Name}: {message}");
        }
    }
}
