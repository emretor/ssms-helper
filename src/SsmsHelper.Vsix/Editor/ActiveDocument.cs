using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SsmsHelper.Editor;

/// <summary>
/// The active document window, which is what SSMS's query commands (Execute, the results grid) act on, even when
/// focus is in its results grid or a tool window. UI thread only.
/// </summary>
internal static class ActiveDocument
{
    private static IVsMonitorSelection? s_selection;
    private static IVsEditorAdaptersFactoryService? s_adapters;

    /// <summary>The active document's view; for a query window, SSMS's SqlScriptEditorControl.</summary>
    public static object? DocView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        s_selection ??= ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        if (s_selection is null ||
            s_selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frameObject) != VSConstants.S_OK ||
            frameObject is not IVsWindowFrame frame ||
            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView) != VSConstants.S_OK)
        {
            return null;
        }

        return docView;
    }

    /// <summary>The editor of a document view: the pane last used when the window is split.</summary>
    public static IWpfTextView? TextView(object? docView)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (docView is not IVsCodeWindow codeWindow)
        {
            return null;
        }

        s_adapters ??= (ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) as IComponentModel)?
            .GetService<IVsEditorAdaptersFactoryService>();
        if (s_adapters is null)
        {
            return null;
        }

        if ((codeWindow.GetLastActiveView(out var textView) != VSConstants.S_OK || textView is null) &&
            codeWindow.GetPrimaryView(out textView) != VSConstants.S_OK)
        {
            return null;
        }

        return textView is null ? null : s_adapters.GetWpfTextView(textView);
    }

    /// <summary>The active document's editor, or null when the active document is not a text window.</summary>
    public static IWpfTextView? TextView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return TextView(DocView());
    }
}
