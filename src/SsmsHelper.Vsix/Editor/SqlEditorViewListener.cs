using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using SsmsHelper.Core.Snippets;

namespace SsmsHelper.Editor;

/// <summary>
/// Attaches <see cref="SnippetCommandFilter"/> to every SSMS query editor.
/// Listens on all document text views and keeps only SQL ones, because the SSMS query
/// editor's content type name is not documented. The first attach logs the content type to
/// the SSMS activity log (run Ssms.exe /log) so the filter can be narrowed later.
/// </summary>
[Export(typeof(IVsTextViewCreationListener))]
[ContentType("text")]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class SqlEditorViewListener : IVsTextViewCreationListener
{
    private static bool s_loggedContentType;

    [Import]
    internal IVsEditorAdaptersFactoryService AdapterService { get; set; } = null!;

    [Import]
    internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

    [Import(AllowDefault = true)]
    internal ICompletionBroker? CompletionBroker { get; set; }

    [Import(AllowDefault = true)]
    internal IAsyncCompletionBroker? AsyncCompletionBroker { get; set; }

    // Catalog is shared by all views. User-defined snippets (SSM-16) will replace it at runtime.
    internal static SnippetExpander Expander { get; set; } = new(SnippetCatalog.Default);

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var view = AdapterService.GetWpfTextView(textViewAdapter);
            if (view is null || !IsSqlEditor(view, out var description))
            {
                return;
            }

            if (!s_loggedContentType)
            {
                s_loggedContentType = true;
                ActivityLog.TryLogInformation(Log.Source, $"Attached to SQL editor: {description}");
            }

            var filter = new SnippetCommandFilter(view, () => Expander, CompletionBroker, AsyncCompletionBroker);
            if (textViewAdapter.AddCommandFilter(filter, out var next) == Microsoft.VisualStudio.VSConstants.S_OK)
            {
                filter.Next = next;
                view.Properties[typeof(SnippetCommandFilter)] = filter;
            }
        }
        catch (Exception ex)
        {
            ActivityLog.TryLogError(Log.Source, $"Failed to attach to text view: {ex}");
        }
    }

    private bool IsSqlEditor(IWpfTextView view, out string description)
    {
        var contentType = view.TextBuffer.ContentType;
        var path = TextDocumentFactory.TryGetTextDocument(view.TextBuffer, out var document) ? document.FilePath : null;
        description = $"content type '{contentType.TypeName}', document '{path}'";

        if (path is not null && path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentType.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
