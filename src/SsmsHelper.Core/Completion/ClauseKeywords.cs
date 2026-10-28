using System;
using System.Collections.Generic;
using SsmsHelper.Core.Joins;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Completion;

/// <summary>
/// SSM-69: the keywords that can follow what is already written, for a word being typed ("FROM dbo.Orders o gr" gives
/// GROUP BY). Only where a keyword is what comes next; never on a bare space, so Enter at the end of a line stays a
/// new line. Called with the tokens <see cref="CompletionContextAnalyzer"/> already has: a few token steps back, no
/// extra pass over the script.
/// </summary>
internal static class ClauseKeywords
{
    // After a FROM / JOIN table source. A JOIN's own source gets ON first.
    private static readonly string[] AfterTable =
    {
        "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "CROSS APPLY", "OUTER APPLY",
        "GROUP BY", "ORDER BY", "UNION", "UNION ALL", "EXCEPT", "INTERSECT",
    };

    private static readonly string[] AfterJoinTable = Prepend("ON", AfterTable);

    // After a complete condition ("WHERE o.Status = 1 |").
    private static readonly string[] AfterWhere =
        { "AND", "OR", "GROUP BY", "ORDER BY", "UNION", "UNION ALL", "EXCEPT", "INTERSECT" };

    private static readonly string[] AfterWriteWhere = { "AND", "OR" };
    private static readonly string[] AfterHaving = { "AND", "OR", "ORDER BY", "UNION", "UNION ALL", "EXCEPT", "INTERSECT" };
    private static readonly string[] AfterOn = Prepend("AND", Prepend("OR", AfterTable));

    // After a column with no operator yet ("WHERE o.Name |").
    private static readonly string[] AfterColumn = { "LIKE", "NOT LIKE", "IN", "NOT IN", "IS NULL", "IS NOT NULL", "BETWEEN" };

    private static readonly string[] AfterGroupBy = { "HAVING", "ORDER BY", "UNION", "UNION ALL", "EXCEPT", "INTERSECT" };
    private static readonly string[] AfterUpdateTarget = { "SET" };
    private static readonly string[] AfterSet = { "WHERE", "FROM" };
    private static readonly string[] AfterInsertTarget = { "VALUES", "SELECT" };
    private static readonly string[] AfterSelectItem = { "FROM", "INTO" };

    private static readonly ISet<string> SetOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UNION", "UNION ALL", "EXCEPT", "INTERSECT",
    };

    // Words that end a SELECT list when scanning back from an item to its SELECT.
    private static readonly ISet<string> SelectListStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "WHERE", "HAVING", "BY", "SET", "VALUES", "ON", "JOIN", "INTO", "UNION", "EXCEPT", "INTERSECT",
        "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "IF", "WHILE", "BEGIN", "ELSE", "EXEC", "EXECUTE", "PRINT",
        "RETURN", "CREATE", "ALTER", "DROP", "TRUNCATE", "OUTPUT",
    };

    // Words that start another condition or clause when scanning back over the current condition.
    private static readonly ISet<string> ConditionStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "WHERE", "ON", "HAVING", "SELECT", "FROM", "JOIN", "SET", "WHEN", "THEN", "ELSE", "BY", "VALUES",
        "CASE", "IF", "WHILE",
    };

    /// <param name="anchor">The token before the typed word.</param>
    /// <param name="typed">The word typed so far.</param>
    public static CompletionContext? Analyze(string text, IReadOnlyList<SqlToken> tokens, int anchor, string typed, int replaceStart)
    {
        var words = NextWords(text, tokens, anchor, out var soft);
        if (words is null)
        {
            return null;
        }

        // Where the word may be an alias, one letter ("o", "c") is far more often the alias: no list. One letter does
        // not open the list for the rare set operators either: "e", "i", "u" after a condition usually start the next
        // statement (EXEC, IF, UPDATE), not EXCEPT, INTERSECT, UNION.
        if ((soft && typed.Length < 2) ||
            !Array.Exists(words, w => StartsAWord(w, typed) && (typed.Length >= 2 || !SetOperators.Contains(w))))
        {
            return null;
        }

        return new CompletionContext(CompletionKind.Clauses, replaceStart, Array.Empty<TableReference>(), Words: words, SoftSelect: soft);
    }

    private static string[]? NextWords(string text, IReadOnlyList<SqlToken> tokens, int anchor, out bool soft)
    {
        soft = false;
        var a = tokens[anchor];
        if (a.IsWord("AS") || a.IsPunctuation('.') || a.IsPunctuation(','))
        {
            return null;
        }

        var start = CompletionContextAnalyzer.StatementStart(tokens, anchor + 1);

        // "FROM dbo.Orders o |", "JOIN dbo.Customers c |", "FROM dbo.Orders |" (the word may be its alias).
        foreach (var (reference, end) in TableSources.Collect(tokens, start, anchor + 1))
        {
            if (end == anchor)
            {
                soft = reference.AliasText is null && !a.IsPunctuation(')');
                return reference.IsJoined ? AfterJoinTable : AfterTable;
            }
        }

        if (IsUpdateTarget(tokens, anchor))
        {
            return AfterUpdateTarget;
        }

        if (IsInsertTarget(tokens, anchor))
        {
            return AfterInsertTarget;
        }

        if (CompletionContextAnalyzer.EndsByItem(tokens, anchor, "GROUP"))
        {
            return AfterGroupBy;
        }

        if (ConditionWords(tokens, anchor, start) is { } condition)
        {
            return condition;
        }

        if (EndsSelectItem(tokens, anchor))
        {
            soft = true; // "SELECT o.Name na": the word may be a column alias
            return AfterSelectItem;
        }

        return null;
    }

    /// <summary>
    /// Inside WHERE, HAVING, ON or UPDATE's SET: after a complete condition ("o.Status = 1", "a IN (1, 2)",
    /// "a IS NULL", "a BETWEEN 1 AND 5") the next condition or clause; after a lone column, the operators that are words.
    /// </summary>
    private static string[]? ConditionWords(IReadOnlyList<SqlToken> tokens, int anchor, int statementStart)
    {
        var conditionStart = ConditionStart(tokens, anchor);
        if (conditionStart > anchor)
        {
            return null;
        }

        var clause = CompletionContextAnalyzer.ClauseBefore(tokens, anchor + 1);
        if (clause < 0)
        {
            return null;
        }

        var c = tokens[clause];
        var isWrite = statementStart < tokens.Count && (tokens[statementStart].IsWord("UPDATE") || tokens[statementStart].IsWord("DELETE"));
        if (c.IsWord("SET"))
        {
            // UPDATE ... SET a = 1 |, not SET @x = 1 or SET NOCOUNT ON.
            return isWrite && tokens[statementStart].IsWord("UPDATE") && IsComplete(tokens, conditionStart, anchor) ? AfterSet : null;
        }

        if (!c.IsWord("WHERE") && !c.IsWord("HAVING") && !c.IsWord("ON"))
        {
            return null;
        }

        if (IsComplete(tokens, conditionStart, anchor))
        {
            return c.IsWord("ON") ? AfterOn
                : c.IsWord("HAVING") ? AfterHaving
                : isWrite ? AfterWriteWhere
                : AfterWhere;
        }

        return IsNameOnly(tokens, conditionStart, anchor) ? AfterColumn : null;
    }

    /// <summary>First token of the condition that ends at the anchor: after AND / OR (not BETWEEN's AND), a clause word or "(".</summary>
    private static int ConditionStart(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var depth = 0;
        for (var k = anchor; k >= 0; k--)
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
            else if (depth == 0 && (t.IsPunctuation(';') ||
                                    (t.Kind == SqlTokenKind.Word && ConditionStopWords.Contains(t.Text) && !IsBetweenAnd(tokens, k))))
            {
                return k + 1;
            }
        }

        return 0;
    }

    // "a BETWEEN 1 AND 5": that AND belongs to BETWEEN.
    private static bool IsBetweenAnd(IReadOnlyList<SqlToken> tokens, int k)
    {
        if (!tokens[k].IsWord("AND"))
        {
            return false;
        }

        var depth = 0;
        for (var i = k - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.IsPunctuation(')'))
            {
                depth++;
            }
            else if (t.IsPunctuation('('))
            {
                if (depth-- == 0)
                {
                    return false;
                }
            }
            else if (depth == 0)
            {
                if (t.IsWord("BETWEEN"))
                {
                    return true;
                }

                if (t.Kind == SqlTokenKind.Word && ConditionStopWords.Contains(t.Text))
                {
                    return false;
                }
            }
        }

        return false;
    }

    // tokens[from..anchor] is a whole condition: it compares something and ends with a value.
    private static bool IsComplete(IReadOnlyList<SqlToken> tokens, int from, int anchor)
    {
        if (!EndsValue(tokens[anchor]))
        {
            return false;
        }

        var depth = 0;
        for (var k = from; k <= anchor; k++)
        {
            var t = tokens[k];
            if (t.IsPunctuation('('))
            {
                depth++;
            }
            else if (t.IsPunctuation(')'))
            {
                depth--;
            }
            else if (depth == 0 && (t.Kind == SqlTokenKind.Operator && t.Text is "=" or "<" or ">" or "!" ||
                                    t.IsWord("LIKE") || t.IsWord("IN") || t.IsWord("IS") || t.IsWord("BETWEEN") || t.IsWord("EXISTS")))
            {
                return true;
            }
        }

        return false;
    }

    // A value can end here: a name, number, string, variable, NULL or a closing parenthesis.
    private static bool EndsValue(SqlToken t) =>
        t.Kind is SqlTokenKind.QuotedIdentifier or SqlTokenKind.Number or SqlTokenKind.String or SqlTokenKind.Variable ||
        t.IsPunctuation(')') || t.IsWord("NULL") ||
        (t.Kind == SqlTokenKind.Word && !SqlIdentifier.ReservedWords.Contains(t.Text));

    // "o.Name", "[Order Date]": a column name and nothing else.
    private static bool IsNameOnly(IReadOnlyList<SqlToken> tokens, int from, int anchor)
    {
        for (var k = from; k <= anchor; k++)
        {
            var isPart = (k - from) % 2 == 0;
            if (isPart ? !TableSources.IsNamePart(tokens[k]) : !tokens[k].IsPunctuation('.'))
            {
                return false;
            }
        }

        return (anchor - from) % 2 == 0;
    }

    // "UPDATE dbo.Orders |", "UPDATE TOP (10) o |".
    private static bool IsUpdateTarget(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var i = BeforeName(tokens, anchor);
        if (i == anchor)
        {
            return false;
        }

        if (i >= 0 && tokens[i].IsPunctuation(')'))
        {
            i = BeforeGroup(tokens, i);
            if (i < 0 || !tokens[i].IsWord("TOP"))
            {
                return false;
            }

            i--;
        }

        return i >= 0 && tokens[i].IsWord("UPDATE") && (i == 0 || !tokens[i - 1].IsWord("ON") && !tokens[i - 1].IsWord("FOR"));
    }

    // "INSERT [INTO] dbo.Orders |", "INSERT INTO dbo.Orders (a, b) |".
    private static bool IsInsertTarget(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var i = anchor;
        if (tokens[i].IsPunctuation(')'))
        {
            i = BeforeGroup(tokens, i);
            if (i < 0)
            {
                return false;
            }
        }

        var beforeName = BeforeName(tokens, i);
        if (beforeName == i)
        {
            return false;
        }

        if (beforeName >= 0 && tokens[beforeName].IsWord("INTO"))
        {
            beforeName--;
        }

        return beforeName >= 0 && tokens[beforeName].IsWord("INSERT");
    }

    // Index before the dotted name ending at i, or i when tokens[i] does not end a name.
    private static int BeforeName(IReadOnlyList<SqlToken> tokens, int i)
    {
        if (!TableSources.IsNamePart(tokens[i]) || tokens[i].Value.StartsWith("#", StringComparison.Ordinal))
        {
            return i;
        }

        var k = i - 1;
        while (k >= 1 && tokens[k].IsPunctuation('.') && TableSources.IsNamePart(tokens[k - 1]))
        {
            k -= 2;
        }

        return k;
    }

    // Index before the "(" matching the ")" at i, or -1.
    private static int BeforeGroup(IReadOnlyList<SqlToken> tokens, int i)
    {
        var depth = 0;
        for (; i >= 0; i--)
        {
            if (tokens[i].IsPunctuation(')'))
            {
                depth++;
            }
            else if (tokens[i].IsPunctuation('(') && --depth == 0)
            {
                return i - 1;
            }
        }

        return -1;
    }

    /// <summary>"SELECT o.Name |", "SELECT COUNT(*) |", "SELECT a, b AS x |": the anchor ends an item of a SELECT list.</summary>
    private static bool EndsSelectItem(IReadOnlyList<SqlToken> tokens, int anchor)
    {
        var a = tokens[anchor];
        if (!EndsValue(a) && a.Text != "*")
        {
            return false;
        }

        // Not the TOP value: "SELECT TOP 10 |", "SELECT TOP (10) |".
        var before = a.IsPunctuation(')') ? BeforeGroup(tokens, anchor) : anchor - 1;
        if (before >= 0 && tokens[before].IsWord("TOP"))
        {
            return false;
        }

        var depth = 0;
        for (var k = anchor; k >= 0; k--)
        {
            var t = tokens[k];
            if (t.IsPunctuation(')'))
            {
                depth++;
            }
            else if (t.IsPunctuation('('))
            {
                if (depth-- == 0)
                {
                    return false;
                }
            }
            else if (depth == 0)
            {
                if (t.IsWord("SELECT"))
                {
                    return true;
                }

                if (t.IsPunctuation(';') || (t.Kind == SqlTokenKind.Word && SelectListStopWords.Contains(t.Text)))
                {
                    return false;
                }
            }
        }

        return false;
    }

    // "jo" starts a word of "LEFT JOIN", "or" starts "ORDER BY".
    private static bool StartsAWord(string keyword, string typed)
    {
        foreach (var word in keyword.Split(' '))
        {
            if (word.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Prepend(string word, string[] words)
    {
        var result = new string[words.Length + 1];
        result[0] = word;
        Array.Copy(words, 0, result, 1, words.Length);
        return result;
    }
}
