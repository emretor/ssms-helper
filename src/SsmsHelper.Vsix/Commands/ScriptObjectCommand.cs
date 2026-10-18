using System.ComponentModel.Design;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using SsmsHelper.Editor;
using SsmsHelper.Options;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper: Script Object (F12 by default, keyboard name Tools.SSMSHelperScriptObject). Opens the object
/// under the caret of the focused query window as ALTER (modules) or CREATE (tables) in a new query window.
/// </summary>
internal static class ScriptObjectCommand
{
    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
        {
            return;
        }

        var textManager = await package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
        var componentModel = await package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();

        var id = new CommandID(PackageIds.CommandSet, PackageIds.ScriptObjectCommandId);
        commandService.AddCommand(new MenuCommand((_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SettingsStore.Current.ScriptObject)
            {
                StatusBar.Show("Script Object is turned off under Tools > Options > SSMS Helper.");
                return;
            }

            // Must have focus: F12 pressed in Object Explorer must not script the last editor's word.
            if (textManager is null || adapters is null || textManager.GetActiveView(1, null, out var activeView) != 0 || activeView is null)
            {
                StatusBar.Show("place the caret on a name in a query window.");
                return;
            }

            var view = adapters.GetWpfTextView(activeView);
            if (view is null || !view.Properties.ContainsProperty(typeof(SqlEditorCommandFilter)))
            {
                StatusBar.Show("Script Object works in SQL query windows only.");
                return;
            }

            ObjectScripter.Run(view);
        }, id));
    }
}
