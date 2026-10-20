using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using SsmsHelper.Options;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>Tools > SSMS Helper > Options... Opens Tools > Options > SSMS Helper > General (SSM-65).</summary>
internal static class OptionsCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var id = new CommandID(PackageIds.CommandSet, PackageIds.OptionsCommandId);
        commandService.AddCommand(new MenuCommand((_, _) => package.ShowOptionPage(typeof(GeneralOptionsPage)), id));
    }
}
