using System.Linq;
using System.Text;
using SsmsHelper.Core.Formatting;
using SsmsHelper.Core.Inserts;
using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Execs;

public enum ExecLayout
{
    /// <summary>Tab: one parameter per line with an empty value and a type comment, ";" on its own line.</summary>
    Expanded,

    /// <summary>Shift+Enter: all parameters on the EXEC line (wrapped at 120 characters), no comments.</summary>
    Compact,
}

/// <summary>Text inserted at <see cref="ExecContext.ReplaceStart"/> (replacing up to the caret) and the caret offset in it.</summary>
public sealed record ExecExpansion(string Text, int CaretOffset);

/// <summary>Writes a procedure's parameters as "@Name = ," with empty values for the user to fill in.</summary>
public static class ExecGenerator
{
    private const string Tab = "    ";

    public static ExecExpansion Generate(ExecContext context, ProcedureInfo procedure, string newLine, ExecLayout layout = ExecLayout.Expanded)
    {
        var parameters = procedure.Parameters;
        if (parameters.Count == 0)
        {
            return new ExecExpansion(";", 1);
        }

        var items = parameters.Select(p => p.Name + " = ").ToList();
        var text = new StringBuilder();

        if (layout == ExecLayout.Compact)
        {
            var nextLine = context.Indent + Tab;
            text.Append(' ');
            var caret = text.Length + items[0].Length;

            // The first line continues after "EXEC dbo.Name "; continuation lines are indented under EXEC.
            text.Append(string.Join(newLine + nextLine, LineWrap.Wrap(items, context.NameEndColumn + 1, nextLine.Length))).Append(';');
            return new ExecExpansion(text.ToString(), caret);
        }

        // "@CustomerId = ," padded so the comments line up; the last item has no comma.
        var lines = items.Select((item, i) => item + (i < items.Count - 1 ? "," : string.Empty)).ToList();
        var width = lines.Max(l => l.Length);
        var expandedCaret = -1;
        for (var i = 0; i < parameters.Count; i++)
        {
            text.Append(newLine).Append(context.Indent).Append(Tab);
            if (expandedCaret < 0)
            {
                expandedCaret = text.Length + items[0].Length;
            }

            text.Append(lines[i].PadRight(width)).Append(" -- ").Append(Describe(parameters[i]));
        }

        // A ";" after the last value would land inside its comment, so it gets a line of its own.
        text.Append(newLine).Append(context.Indent).Append(';');
        return new ExecExpansion(text.ToString(), expandedCaret);
    }

    private static string Describe(ParameterInfo parameter)
    {
        var type = InsertGenerator.FormatType(parameter.TypeName, parameter.BaseTypeName, parameter.MaxLength, parameter.Precision, parameter.Scale);
        return parameter.IsOutput ? type + " OUTPUT" : type;
    }
}
