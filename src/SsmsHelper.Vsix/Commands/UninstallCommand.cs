using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SsmsHelper.Core;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper > Uninstall (SSM-63). Starts SSMS's own VSIXInstaller with /uninstall for this SSMS instance
/// only; the installer waits for SSMS to close and then removes the extension.
/// </summary>
internal static class UninstallCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var id = new CommandID(PackageIds.CommandSet, PackageIds.UninstallCommandId);
        commandService.AddCommand(new MenuCommand((_, _) => Execute(package), id));
    }

    private static void Execute(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            Uninstall(package);
        }
        catch (Exception ex)
        {
            Log.Error("Uninstall", ex);
            Show(package, $"Uninstall failed: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK);
        }
    }

    private static void Uninstall(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var host = Process.GetCurrentProcess().MainModule;
        var hostName = host?.FileVersionInfo.ProductName ?? "SSMS";
        var instanceId = InstallLocation.InstanceId(typeof(UninstallCommand).Assembly.Location);
        var installer = host is null ? null : Path.Combine(Path.GetDirectoryName(host.FileName)!, "VSIXInstaller.exe");
        if (instanceId is null || installer is null || !File.Exists(installer))
        {
            Show(package, $"{ProductInfo.Name} cannot uninstall itself from here (installed from '{typeof(UninstallCommand).Assembly.Location}').{Environment.NewLine}{Environment.NewLine}" +
                "Close SSMS, delete the extension folder and touch 'extensions.configurationchanged' in the Extensions folder (see the README).",
                OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK);
            return;
        }

        var answer = Show(package,
            $"Uninstall {ProductInfo.Name} {ProductInfo.Version} from {hostName}?{Environment.NewLine}{Environment.NewLine}" +
            "The Visual Studio extension installer opens and asks you to close SSMS (save your queries first). " +
            "Other SSMS versions keep their copy.",
            OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL);
        if (answer != 1 /* IDOK */)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(installer, $"/uninstall:{InstallLocation.ExtensionId} /instanceIds:{instanceId}") { UseShellExecute = false });
    }

    // Cancel is the default for the confirmation, as in the WHERE check.
    private static int Show(AsyncPackage package, string message, OLEMSGICON icon, OLEMSGBUTTON buttons) =>
        VsShellUtilities.ShowMessageBox(package, message, ProductInfo.Name, icon, buttons,
            buttons == OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL ? OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND : OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
}
