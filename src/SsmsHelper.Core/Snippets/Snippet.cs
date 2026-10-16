namespace SsmsHelper.Core.Snippets;

/// <summary>
/// An abbreviation that expands to SQL text. Bodies use "\n" for line breaks and may contain
/// <see cref="CursorMarker"/> once to place the caret after expansion.
/// </summary>
public sealed record Snippet(string Shortcut, string Body, string Description)
{
    public const string CursorMarker = "$CURSOR$";
}
