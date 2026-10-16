using System.Collections.Generic;
using System.Text;

namespace SsmsHelper.Core.Formatting;

/// <summary>Greedy wrapping of comma-separated lists, shared by the compact layouts.</summary>
internal static class LineWrap
{
    /// <summary>Compact lists wrap so that no line is longer than this.</summary>
    public const int MaxLineLength = 120;

    /// <summary>
    /// Wraps "a, b, c" into lines of at most <paramref name="maxLineLength"/> characters, counting the first line
    /// from column <paramref name="firstLineStart"/> and the others from <paramref name="nextLineStart"/>. Every line
    /// but the last ends with a comma. An item longer than a line gets a line of its own.
    /// </summary>
    public static IReadOnlyList<string> Wrap(IReadOnlyList<string> items, int firstLineStart, int nextLineStart, int maxLineLength = MaxLineLength)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        var start = firstLineStart;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i] + (i < items.Count - 1 ? "," : string.Empty);
            var needed = current.Length == 0 ? item.Length : current.Length + 1 + item.Length;
            if (current.Length > 0 && start + needed > maxLineLength)
            {
                lines.Add(current.ToString());
                current.Clear();
                start = nextLineStart;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(item);
        }

        lines.Add(current.ToString());
        return lines;
    }
}
