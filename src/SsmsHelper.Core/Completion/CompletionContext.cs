using System;
using System.Collections.Generic;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Completion;

public enum CompletionKind
{
    /// <summary>Tables, views and table-valued functions: after FROM, JOIN, INTO, UPDATE.</summary>
    Objects,

    /// <summary>Stored procedures: after EXEC / EXECUTE.</summary>
    Procedures,

    /// <summary>Columns of <see cref="CompletionContext.Tables"/>: after UPDATE ... SET, in a WHERE clause, after "alias.".</summary>
    Columns,

    /// <summary>Keywords that can follow "SELECT *": FROM, INTO, ...</summary>
    Keywords,

    /// <summary>Databases of the server: after USE.</summary>
    Databases,

    /// <summary>
    /// Join conditions for <see cref="CompletionContext.Join"/>, then the columns of <see cref="CompletionContext.Tables"/>
    /// (the joined table first): after "JOIN table [alias] " and after "JOIN table [alias] ON".
    /// </summary>
    JoinConditions,
}

/// <summary>Where a completion list applies and what it lists.</summary>
/// <param name="Kind">What to list.</param>
/// <param name="ReplaceStart">Buffer position of the partly typed name the list replaces (the caret when nothing is typed).</param>
/// <param name="Tables">For <see cref="CompletionKind.Columns"/>: the tables whose columns to list, as written.</param>
/// <param name="UpdatableOnly">For columns after UPDATE ... SET: leave out identity, computed and rowversion columns.</param>
/// <param name="AfterQualifier">For columns after "o.": the alias is already typed, so names go in unqualified.</param>
/// <param name="Join">For <see cref="CompletionKind.JoinConditions"/>: the JOIN being written. Its positions are
/// relative to the analyzed text; use <paramref name="ReplaceStart"/> for the buffer.</param>
public sealed record CompletionContext(CompletionKind Kind, int ReplaceStart, IReadOnlyList<TableReference> Tables,
    bool UpdatableOnly = false, bool AfterQualifier = false, JoinContext? Join = null)
{
    public static CompletionContext Of(CompletionKind kind, int replaceStart) => new(kind, replaceStart, Array.Empty<TableReference>());
}

/// <summary>
/// Decides whether the caret is where one of our completion lists belongs. Reads only a window of text around the
/// caret (<see cref="Window"/> characters each side), so it is cheap enough for every keystroke. Returns null
/// everywhere else, so SSMS's own list keeps working there.
/// </summary>
public static class CompletionContextAnalyzer
{
    /// <summary>Characters read before and after the caret.</summary>
    public const int Window = 2000;

    // Words that start another statement or clause: scanning back for the statement start stops at them.
    private static readonly ISet<string> StatementWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "IF", "WHILE", "BEGIN", "END", "ELSE",
        "EXEC", "EXECUTE", "PRINT", "RETURN", "CREATE", "ALTER", "DROP", "TRUNCATE", "WITH",
    };

    // Clause words: AND / OR belong to the nearest one before them.
    private static readonly ISet<string> ClauseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "ON", "HAVING", "SELECT", "FROM", "JOIN", "SET", "WHEN", "THEN", "BY", "VALUES", "CASE", "IF", "WHILE",
    };

    /// <param name="text">Text around the caret.</param>
    /// <param name="caret">Caret index in <paramref name="text"/>.</param>
    /// <param name="offset">Buffer position of text[0], added to the returned ReplaceStart.</param>
    public static CompletionContext? Analyze(string text, int caret, int offset = 0)
    {
        if (caret <= 0 || caret > text.Length)
        {
            return null;
        }

        var state = SqlLexicalContext.GetState(text, caret);
        if (state is not (SqlLexicalState.Code or SqlLexicalState.BracketIdentifier))
        {
            return null;
        }

        var tokens = SqlTokenizer.Tokenize(text);
        var before = -1; // last token that starts before the caret
        while (before + 1 < tokens.Count && tokens[before + 1].Start < caret)
        {
            before++;
        }

        if (before < 0)
        {
            return null;
        }

        // The partly typed name ending at the caret ("Ga", "dbo.Ga", "dbo.", "[Order Li"), or none after whitespace.
        int first;
        if (tokens[before].End < caret)
        {
            if (!char.IsWhiteSpace(text[caret - 1]))
            {
                return null;
            }

            first = before + 1;
        }
        else if (tokens[before].End == caret || state == SqlLexicalState.BracketIdentifier)
        {
            if (!tokens[before].IsIdentifier && !tokens[before].IsPunctuation('.'))
            {
                return null;
            }

            first = before;
            while (first > 0 && tokens[first - 1].End == tokens[first].Start &&
                   (tokens[first].IsPunctuation('.') ? tokens[first - 1].IsIdentifier : tokens[first - 1].IsPunctuation('.')))
            {
                first--;
            }

            if (tokens[first].IsPunctuation('.'))
            {
                return null;
            }
        }
        else
        {
            return null; // inside a token
        }

        var anchor = first - 1;
        if (anchor < 0)
        {
            return null;
        }

        var replaceStart = offset + (first <= before ? tokens[first].Start : caret);
        var qualified = first < before; // "dbo.x" or "o.x"
        var anchorToken = tokens[anchor];

        if (IsObjectKeyword(tokens, anchor))
        {
            return CompletionContext.Of(CompletionKind.Objects, replaceStart);
        }

        if ((anchorToken.IsWord("EXEC") || anchorToken.IsWord("EXECUTE")) && !(first <= before && tokens[first].Kind == SqlTokenKind.Variable))
        {
            return CompletionContext.Of(CompletionKind.Procedures, replaceStart);
        }

        if (anchorToken.IsWord("USE"))
        {
            return qualified ? null : CompletionContext.Of(CompletionKind.Databases, replaceStart);
        }

        if (JoinConditions(text, tokens, anchor, typedName: first <= before, caret, replaceStart) is { } join)
        {
            return join;
        }

        if (qualified)
        {
            return QualifiedColumns(text, tokens, first, before, caret, offset);
        }

        if (IsSelectStar(tokens, anchor))
        {
            return CompletionContext.Of(CompletionKind.Keywords, replaceStart);
        }

        if (SetTarget(text, tokens, anchor) is { } target)
        {
            return new CompletionContext(CompletionKind.Columns, replaceStart, new[] { target }, UpdatableOnly: true);
        }

        if (WhereTables(text, tokens, anchor) is { Count: > 0 } tables)
        {
            return new CompletionContext(CompletionKind.Columns, replaceStart, tables);
        }

        if (OnTables(tokens, anchor) is { Count: > 0 } onTables)
        {
            return new CompletionContext(CompletionKind.Columns, replaceStart, onTables);
        }

        return null;
    }

    /// <summary>
    /// "JOIN dbo.Orders o |", "JOIN dbo.Orders o ON |", "... ON o.Cu|": the JOIN helper's conditions plus the columns
    /// of every table so far, the joined table first, then the nearest earlier ones. Not while the alias is typed.
    /// </summary>
    private static CompletionContext? JoinConditions(string text, IReadOnlyList<SqlToken> tokens, int anchor, bool typedName, int caret, int replaceStart)
    {
        // Cheap pre-check: the anchor is ON, or a name right after "JOIN name[.name] [AS]".
        var a = tokens[anchor];
        if (!a.IsWord("ON") && (typedName || !a.IsIdentifier || !JoinShortlyBefore(tokens, anchor)))
        {
            return null;
        }

        var join = JoinContextAnalyzer.Analyze(text, caret);
        if (join is null || (join.Kind == JoinContextKind.AfterJoinTarget && typedName))
        {
            return null;
        }

        var tables = new List<TableReference> { join.Target };
        for (var i = join.Previous.Count - 1; i >= 0; i--)
        {
            tables.Add(join.Previous[i]);
        }

        tables.RemoveAll(t => t.IsDerived || t.Name.StartsWith("#", StringComparison.Ordinal) || t.Name.Length == 0);
        return new CompletionContext(CompletionKind.JoinConditions, replaceStart, tables, Join: join);
    }

    private static bool JoinShortlyBefore(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        // "JOIN [Sales].[dbo].[Order Lines] AS l": at most 7 tokens from JOIN to the alias.
        for (var i = anchor; i >= 0 && i >= anchor - 7; i--)
        {
            if (tokens[i].IsWord("JOIN"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Inside an ON condition, after "=", AND, OR, NOT or "(" (or right after ON when the JOIN helper has nothing):
    /// the columns of the tables up to that ON, the joined table first, then the nearest earlier ones.
    /// </summary>
    private static List<TableReference>? OnTables(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var a = tokens[anchor];
        int on;
        if (a.IsWord("ON"))
        {
            on = anchor;
        }
        else if ((a.Text == "=" && !(anchor > 0 && tokens[anchor - 1].End == a.Start && tokens[anchor - 1].Kind == SqlTokenKind.Operator)) ||
                 a.IsWord("AND") || a.IsWord("OR") || a.IsWord("NOT") || a.IsPunctuation('('))
        {
            on = ClauseBefore(tokens, anchor);
            if (on < 0 || !tokens[on].IsWord("ON"))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        var tables = new List<TableReference>();
        foreach (var source in TableSources.Collect(tokens, StatementStart(tokens, on), on))
        {
            tables.Add(source.Reference);
        }

        tables.Reverse();
        tables.RemoveAll(t => t.IsDerived || t.Name.StartsWith("#", StringComparison.Ordinal) || t.Name.Length == 0);
        return tables;
    }

    private static bool IsObjectKeyword(IReadOnlyList<SqlToken> tokens, int i)
    {
        var t = tokens[i];
        if (t.IsWord("FROM") || t.IsWord("JOIN") || t.IsWord("INTO"))
        {
            return true;
        }

        // UPDATE as a statement, not ON UPDATE / FOR UPDATE / GRANT UPDATE / AFTER UPDATE.
        return t.IsWord("UPDATE") &&
               (i == 0 || !(tokens[i - 1].Kind == SqlTokenKind.Word && NotStatementBefore(tokens[i - 1].Text) || tokens[i - 1].IsPunctuation(',')));
    }

    private static bool NotStatementBefore(string word) =>
        word.Equals("ON", StringComparison.OrdinalIgnoreCase) || word.Equals("FOR", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("AFTER", StringComparison.OrdinalIgnoreCase) || word.Equals("OF", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("GRANT", StringComparison.OrdinalIgnoreCase) || word.Equals("DENY", StringComparison.OrdinalIgnoreCase) ||
        word.Equals("REVOKE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// End of the text a pick replaces: the rest of the name right of the caret, so "WHERE |a" + AddressId gives
    /// "AddressId", not "AddressIda". Stops at a dot, whitespace or any other punctuation; inside [brackets], after
    /// the closing bracket. With <paramref name="wholeName"/> (lists of qualified names: tables, procedures) it goes on
    /// over the following parts, so "JOIN |dbo.Orders" + dbo.Orders replaces all of "dbo.Orders" (SSM-64).
    /// </summary>
    public static int ReplaceEnd(string text, int caret, bool wholeName = false)
    {
        var state = caret > 0 && caret <= text.Length ? SqlLexicalContext.GetState(text, caret) : SqlLexicalState.Code;
        int i;
        if (state == SqlLexicalState.BracketIdentifier)
        {
            i = BracketEnd(text, caret);
            if (i == caret)
            {
                return caret; // unclosed bracket
            }
        }
        else
        {
            i = wholeName ? PartEnd(text, caret) : WordEnd(text, caret);
        }

        if (!wholeName)
        {
            return i;
        }

        // ".part", "..part" (database..table) and a trailing dot.
        while (i < text.Length && text[i] == '.')
        {
            i++;
            i = PartEnd(text, i);
        }

        return i;
    }

    // A plain name part or a [bracketed] one starting at i; i itself when there is none.
    private static int PartEnd(string text, int i)
    {
        if (i >= text.Length || text[i] != '[')
        {
            return WordEnd(text, i);
        }

        var end = BracketEnd(text, i + 1);
        return end == i + 1 ? i : end; // unclosed: not a name part
    }

    private static int WordEnd(string text, int i)
    {
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$' or '#' or '@'))
        {
            i++;
        }

        return i;
    }

    // From inside a [bracketed] name: after its closing bracket ("]]" is an escaped bracket), or i when unclosed on the line.
    private static int BracketEnd(string text, int i)
    {
        var start = i;
        while (i < text.Length && text[i] != '\n' && text[i] != '\r')
        {
            if (text[i] == ']')
            {
                if (i + 1 < text.Length && text[i + 1] == ']')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return start;
    }

    /// <summary>"SELECT *", "SELECT DISTINCT *", "SELECT TOP (10) *".</summary>
    private static bool IsSelectStar(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        if (tokens[anchor].Text != "*" || anchor == 0)
        {
            return false;
        }

        var i = anchor - 1;
        if (tokens[i].IsPunctuation(')'))
        {
            while (i >= 0 && !tokens[i].IsPunctuation('('))
            {
                i--;
            }

            i--;
            if (i < 0 || !tokens[i].IsWord("TOP"))
            {
                return false;
            }

            i--;
        }
        else if (tokens[i].Kind == SqlTokenKind.Number && i > 0 && tokens[i - 1].IsWord("TOP"))
        {
            i -= 2;
        }

        if (i >= 0 && (tokens[i].IsWord("DISTINCT") || tokens[i].IsWord("ALL")))
        {
            i--;
        }

        return i >= 0 && tokens[i].IsWord("SELECT");
    }

    /// <summary>
    /// After "UPDATE target [WITH (...)] SET " or after a comma in that SET list: the target, resolved through
    /// "FROM table alias" later in the statement when the target is an alias.
    /// </summary>
    private static TableReference? SetTarget(string text, IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var set = anchor;
        if (tokens[anchor].IsPunctuation(','))
        {
            set = ClauseBefore(tokens, anchor);
            if (set < 0 || !tokens[set].IsWord("SET"))
            {
                return null;
            }
        }
        else if (!tokens[anchor].IsWord("SET"))
        {
            return null;
        }

        // Back over "WITH (hints)" and the target name to UPDATE [TOP (n)].
        var i = set - 1;
        if (i >= 0 && tokens[i].IsPunctuation(')'))
        {
            while (i >= 0 && !tokens[i].IsPunctuation('('))
            {
                i--;
            }

            i -= 2; // "(" and WITH
        }

        var nameEnd = i;
        while (i >= 0 && (TableSources.IsNamePart(tokens[i]) || tokens[i].IsPunctuation('.')))
        {
            i--;
        }

        var nameStart = i + 1;
        if (nameStart > nameEnd || i < 0)
        {
            return null;
        }

        if (tokens[i].IsPunctuation(')') && i >= 2)
        {
            while (i >= 0 && !tokens[i].IsPunctuation('('))
            {
                i--;
            }

            i -= 2; // "(" and TOP
            if (i + 1 < 0 || !tokens[i + 1].IsWord("TOP"))
            {
                return null;
            }
        }

        if (i < 0 || !tokens[i].IsWord("UPDATE"))
        {
            return null;
        }

        var parts = new List<SqlToken>();
        for (var k = nameStart; k <= nameEnd; k++)
        {
            if (!tokens[k].IsPunctuation('.'))
            {
                parts.Add(tokens[k]);
            }
        }

        if (parts.Count == 0 || parts[parts.Count - 1].Value.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        var name = parts[parts.Count - 1];
        var target = new TableReference(
            parts.Count > 1 ? parts[parts.Count - 2].Value : null, name.Value, name.Text,
            text.Substring(tokens[nameStart].Start, tokens[nameEnd].End - tokens[nameStart].Start), null, false, false);

        // "UPDATE o SET ... FROM dbo.Orders o": the alias names the real table.
        if (parts.Count == 1)
        {
            foreach (var source in TableSources.Collect(tokens, set, StatementEnd(text, tokens, set + 1)))
            {
                if (source.Reference.AliasText is { } alias && SqlIdentifierEquals(alias, name) && !source.Reference.IsDerived)
                {
                    return source.Reference;
                }
            }
        }

        return target;
    }

    /// <summary>
    /// After WHERE, or after AND / OR / NOT / "(" inside a WHERE clause: the tables of that statement (FROM / JOIN
    /// sources and the UPDATE or DELETE target).
    /// </summary>
    private static List<TableReference>? WhereTables(string text, IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var a = tokens[anchor];
        int where;
        if (a.IsWord("WHERE"))
        {
            where = anchor;
        }
        else if (a.IsWord("AND") || a.IsWord("OR") || a.IsWord("NOT") || a.IsPunctuation('('))
        {
            where = ClauseBefore(tokens, a.IsPunctuation('(') ? anchor : anchor);
            if (where < 0 || !tokens[where].IsWord("WHERE"))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        var start = StatementStart(tokens, where);
        var tables = new List<TableReference>();
        foreach (var source in TableSources.Collect(tokens, start, where))
        {
            tables.Add(source.Reference);
        }

        // UPDATE / DELETE targets are not in a FROM clause: "UPDATE dbo.Orders SET ... WHERE", "DELETE dbo.Orders WHERE".
        if (start < tokens.Count && (tokens[start].IsWord("UPDATE") || tokens[start].IsWord("DELETE")))
        {
            var i = start + 1;
            if (i < where && tokens[i].IsWord("TOP"))
            {
                i = TableSources.SkipGroup(tokens, i + 1, where);
            }

            if (i < where && tokens[i].IsWord("FROM"))
            {
                i = where; // DELETE FROM t: already collected as a FROM source
            }

            var parts = new List<SqlToken>();
            var nameStart = i;
            while (i < where && (TableSources.IsNamePart(tokens[i]) || tokens[i].IsPunctuation('.')))
            {
                if (!tokens[i].IsPunctuation('.'))
                {
                    parts.Add(tokens[i]);
                }

                i++;
            }

            if (parts.Count > 0 && !tables.Exists(t => t.AliasText is { } alias && SqlIdentifierEquals(alias, parts[parts.Count - 1])))
            {
                var name = parts[parts.Count - 1];
                tables.Insert(0, new TableReference(
                    parts.Count > 1 ? parts[parts.Count - 2].Value : null, name.Value, name.Text,
                    text.Substring(tokens[nameStart].Start, tokens[i - 1].End - tokens[nameStart].Start), null, false, false));
            }
        }

        tables.RemoveAll(t => t.IsDerived || t.Name.StartsWith("#", StringComparison.Ordinal) || t.Name.Length == 0);
        return tables;
    }

    /// <summary>
    /// "o." or "o.Sta": the columns of the table the alias (or the table name, when it has no alias) stands for in
    /// this statement. Anything else ("dbo.", "sys.", three-part names, unknown aliases) is left to SSMS's list.
    /// </summary>
    private static CompletionContext? QualifiedColumns(string text, IReadOnlyList<SqlToken> tokens, int first, int before, int caret, int offset)
    {
        if (before > first + 2 || !tokens[first + 1].IsPunctuation('.') || tokens[first].Kind == SqlTokenKind.Variable)
        {
            return null;
        }

        var qualifier = tokens[first];
        var start = StatementStart(tokens, first);
        foreach (var source in TableSources.Collect(tokens, start, StatementEnd(text, tokens, before + 1)))
        {
            var reference = source.Reference;
            var matches = reference.AliasText is { } alias
                ? SqlIdentifierEquals(alias, qualifier)
                : string.Equals(reference.Name, qualifier.Value, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                continue;
            }

            if (reference.IsDerived || reference.Name.Length == 0 || reference.Name.StartsWith("#", StringComparison.Ordinal))
            {
                return null;
            }

            var replaceStart = offset + (before == first + 2 ? tokens[before].Start : caret);
            return new CompletionContext(CompletionKind.Columns, replaceStart, new[] { reference }, AfterQualifier: true);
        }

        return null;
    }

    /// <summary>Index of the nearest clause word before <paramref name="i"/> at the same parenthesis level, or -1.</summary>
    private static int ClauseBefore(IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (var k = i - 1; k >= 0; k--)
        {
            var t = tokens[k];
            if (t.IsPunctuation(')'))
            {
                depth++;
            }
            else if (t.IsPunctuation('('))
            {
                if (depth == 0)
                {
                    continue; // "WHERE (a = 1 AND |": the group belongs to the clause outside it
                }

                depth--;
            }
            else if (depth == 0 && (t.IsPunctuation(';') || (t.Kind == SqlTokenKind.Word && ClauseWords.Contains(t.Text))))
            {
                return t.IsPunctuation(';') ? -1 : k;
            }
        }

        return -1;
    }

    /// <summary>First token of the statement (or subquery) containing token <paramref name="i"/>.</summary>
    private static int StatementStart(IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (var k = i - 1; k >= 0; k--)
        {
            var t = tokens[k];
            if (t.IsPunctuation(')'))
            {
                depth++;
            }
            else if (t.IsPunctuation('('))
            {
                if (depth == 0)
                {
                    return k + 1;
                }

                depth--;
            }
            else if (depth == 0)
            {
                if (t.IsPunctuation(';'))
                {
                    return k + 1;
                }

                if (t.IsWord("SELECT") || t.IsWord("UPDATE") || t.IsWord("DELETE"))
                {
                    // "INSERT INTO t SELECT ...": the SELECT starts the query that has the WHERE.
                    return k;
                }
            }
        }

        return 0;
    }

    private static int StatementEnd(string text, IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsPunctuation('('))
            {
                depth++;
            }
            else if (t.IsPunctuation(')'))
            {
                if (depth == 0)
                {
                    return i;
                }

                depth--;
            }
            else if (depth == 0 && (t.IsPunctuation(';') || TableSources.IsBatchSeparator(text, t) ||
                                    (t.Kind == SqlTokenKind.Word && StatementWords.Contains(t.Text) && !t.IsWord("WITH"))))
            {
                return i;
            }
        }

        return tokens.Count;
    }

    private static bool SqlIdentifierEquals(string aliasText, SqlToken token) =>
        string.Equals(aliasText.Trim('[', ']', '"'), token.Value, StringComparison.OrdinalIgnoreCase);
}
