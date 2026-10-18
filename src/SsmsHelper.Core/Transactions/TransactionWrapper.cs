using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SsmsHelper.Core.Transactions;

/// <summary>Result of <see cref="TransactionWrapper.Wrap"/>.</summary>
/// <param name="Text">The replacement for the whole selected lines.</param>
/// <param name="CaretOffset">Caret position in <paramref name="Text"/>: the start of the ROLLBACK line.</param>
public sealed record WrappedTransaction(string Text, int CaretOffset);

/// <summary>
/// Surround with Transaction: wraps whole lines in BEGIN TRANSACTION and an active ROLLBACK, with
/// COMMIT commented out. Running it shows the effect and undoes it; swap the two lines to keep the change.
/// </summary>
public static class TransactionWrapper
{
    /// <param name="lines">The selected lines without line breaks; blank lines at either end are dropped.</param>
    /// <returns>Null when every line is blank.</returns>
    public static WrappedTransaction? Wrap(IReadOnlyList<string> lines, string newLine)
    {
        var first = 0;
        var last = lines.Count - 1;
        while (first <= last && lines[first].Trim().Length == 0)
        {
            first++;
        }

        while (last >= first && lines[last].Trim().Length == 0)
        {
            last--;
        }

        if (first > last)
        {
            return null;
        }

        var body = lines.Skip(first).Take(last - first + 1).Select(l => l.TrimEnd()).ToList();

        // The wrapper lines line up with the least indented line, so a block inside BEGIN ... END stays in place.
        var indent = body.Where(l => l.Length > 0).Select(Indent).OrderBy(i => i.Length).First();

        var text = new StringBuilder();
        text.Append(indent).Append("BEGIN TRANSACTION;").Append(newLine).Append(newLine);
        foreach (var line in body)
        {
            text.Append(line).Append(newLine);
        }

        text.Append(newLine);
        var caret = text.Length + indent.Length;
        text.Append(indent).Append("ROLLBACK TRANSACTION;").Append(newLine);
        text.Append(indent).Append("-- COMMIT TRANSACTION;");
        return new WrappedTransaction(text.ToString(), caret);
    }

    private static string Indent(string line)
    {
        var length = 0;
        while (length < line.Length && (line[length] == ' ' || line[length] == '\t'))
        {
            length++;
        }

        return line.Substring(0, length);
    }
}
