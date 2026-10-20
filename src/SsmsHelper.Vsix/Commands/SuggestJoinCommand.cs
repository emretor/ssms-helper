using System.ComponentModel.Design;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using SsmsHelper.Editor;
using Task = System.Threading.Tasks.Task;

namespace SsmsHelper.Commands;

/// <summary>
/// Tools > SSMS Helper > Suggest Join. Opens join suggestions at the caret of the active editor.
/// Bind a shortcut under Tools > Options > Environment > Keyboard (command Tools.SSMSHelperSuggestJoin).
/// </summary>
internal static class SuggestJoinCommand
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

        var id = new CommandID(PackageIds.CommandSet, PackageIds.SuggestJoinCommandId);
        commandService.AddCommand(new MenuCommand((_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (textManager is null || adapters is null || textManager.GetActiveView(1, null, out var activeView) != 0 || activeView is null)
            {
                StatusBar.Show("open a query window first.");
                return;
            }

            var view = adapters.GetWpfTextView(activeView);
            if (view is null || !view.Properties.TryGetProperty(typeof(JoinSuggestionController), out JoinSuggestionController controller))
            {
                StatusBar.Show("join suggestions work in SQL query windows only.");
                return;
            }

            controller.TriggerManually();
        }, id));
    }
}
