using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Scripting;

/// <summary>A one- to three-part object name as written in the editor.</summary>
/// <param name="Database">Database part, or null.</param>
/// <param name="Schema">Schema part, or null when not written (the server resolves the user's default schema).</param>
/// <param name="Name">Object name, delimiters removed.</param>
/// <param name="Text">The name exactly as written, for messages and for OBJECT_ID.</param>
public sealed record ObjectReference(string? Database, string? Schema, string Name, string Text)
{
    /// <summary>Two-part (or one-part) name for OBJECT_ID in the current database.</summary>
    public string LocalText => Schema is null ? SqlIdentifier.Quote(Name) : $"{SqlIdentifier.Quote(Schema)}.{SqlIdentifier.Quote(Name)}";

    /// <summary>
    /// The [db.][schema.]name around <paramref name="caret"/> in <paramref name="line"/>, or null when the caret is
    /// not on an identifier. The caret may be anywhere in the name or right after it.
    /// </summary>
    public static ObjectReference? At(string line, int caret)
    {
        var tokens = SqlTokenizer.Tokenize(line);
        var index = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Start <= caret && caret <= tokens[i].End && tokens[i].IsIdentifier)
            {
                index = i;

                // "a|.b": prefer the token the caret is inside over the one it just follows.
                if (caret < tokens[i].End)
                {
                    break;
                }
            }
        }

        if (index < 0)
        {
            return null;
        }

        var first = index;
        while (first >= 2 && tokens[first - 1].IsPunctuation('.') && tokens[first - 2].IsIdentifier &&
               tokens[first - 1].Start == tokens[first - 2].End && tokens[first].Start == tokens[first - 1].End)
        {
            first -= 2;
        }

        var last = index;
        while (last + 2 < tokens.Count && tokens[last + 1].IsPunctuation('.') && tokens[last + 2].IsIdentifier &&
               tokens[last + 1].Start == tokens[last].End && tokens[last + 2].Start == tokens[last + 1].End)
        {
            last += 2;
        }

        var parts = new List<SqlToken>();
        for (var i = first; i <= last; i += 2)
        {
            parts.Add(tokens[i]);
        }

        if (parts.Count > 3 || parts[parts.Count - 1].Value.StartsWith("#"))
        {
            return null; // linked-server names and temp objects are not scripted
        }

        var text = line.Substring(parts[0].Start, parts[parts.Count - 1].End - parts[0].Start);
        var values = parts.Select(p => p.Value).ToList();
        return values.Count switch
        {
            1 => new ObjectReference(null, null, values[0], text),
            2 => new ObjectReference(null, values[0], values[1], text),
            _ => new ObjectReference(values[0], values[1], values[2], text),
        };
    }
}
