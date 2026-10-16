using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsHelper.Core;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper: About. Proves the package loads inside SSMS and that the
/// netstandard2.0 Core assembly resolves in the host process.
/// </summary>
internal static class AboutCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var id = new CommandID(PackageIds.CommandSet, PackageIds.AboutCommandId);
        commandService.AddCommand(new MenuCommand((_, _) => Execute(package), id));
    }

    private static void Execute(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var host = Process.GetCurrentProcess().MainModule?.FileVersionInfo;
        var message =
            $"{ProductInfo.Name} {ProductInfo.Version}{Environment.NewLine}{Environment.NewLine}" +
            $"Host: {host?.ProductName} {host?.ProductVersion}{Environment.NewLine}" +
            $"Runtime: {Environment.Version} ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")})";

        VsShellUtilities.ShowMessageBox(
            package,
            message,
            ProductInfo.Name,
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
