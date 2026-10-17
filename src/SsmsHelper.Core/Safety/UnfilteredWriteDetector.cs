using System;
using System.Collections.Generic;
using System.Linq;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Safety;

/// <summary>An UPDATE or DELETE that has no WHERE and so changes every row of its target.</summary>
/// <param name="Verb">UPDATE or DELETE, upper case.</param>
/// <param name="Target">Target table as written, resolved through a FROM alias when there is one.</param>
/// <param name="Start">Offset of the UPDATE or DELETE keyword.</param>
public sealed record UnfilteredWrite(string Verb, string Target, int Start);

/// <summary>
/// Finds UPDATE and DELETE statements without WHERE in the text about to be executed. One tokenizer pass, then a
/// forward scan per UPDATE/DELETE keyword. Leaves alone: temp tables and table variables, the other uses of the
/// keywords (ON DELETE CASCADE, AFTER UPDATE, GRANT UPDATE, THEN DELETE in MERGE, UPDATE(col), UPDATE STATISTICS),
/// and the bodies of CREATE/ALTER PROCEDURE, TRIGGER and FUNCTION batches, which do not run when executed.
/// </summary>
public static class UnfilteredWriteDetector
{
    private static readonly HashSet<string> NotAStatementAfter = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "FOR", "AFTER", "OF", "THEN", "GRANT", "DENY", "REVOKE",
    };

    private static readonly HashSet<string> ModuleKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROC", "PROCEDURE", "TRIGGER", "FUNCTION",
    };

    // A statement without a terminating semicolon ends where the next one starts.
    private static readonly HashSet<string> StatementStarts = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "SET", "DECLARE", "EXEC", "EXECUTE", "IF", "ELSE", "WHILE",
        "BEGIN", "END", "RETURN", "PRINT", "GO", "CREATE", "ALTER", "DROP", "TRUNCATE", "USE", "RAISERROR", "THROW",
        "COMMIT", "ROLLBACK", "SAVE", "OPEN", "FETCH", "CLOSE", "DEALLOCATE", "GRANT", "DENY", "REVOKE", "WAITFOR",
        "BREAK", "CONTINUE", "GOTO", "BACKUP", "RESTORE", "DBCC", "KILL", "CHECKPOINT", "BULK", "WITH",
    };

    public static IReadOnlyList<UnfilteredWrite> Find(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        var found = new List<UnfilteredWrite>();
        var batchStart = true;
        var inModule = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsWord("GO"))
            {
                batchStart = true;
                inModule = false;
                continue;
            }

            if (batchStart)
            {
                batchStart = false;
                inModule = StartsModule(tokens, i);
            }

            if (inModule || !(token.IsWord("UPDATE") || token.IsWord("DELETE")) || !IsStatement(tokens, i))
            {
                continue;
            }

            if (Check(tokens, i) is { } write)
            {
                found.Add(write);
            }
        }

        return found;
    }

    /// <summary>CREATE [OR ALTER] / ALTER followed by PROC, PROCEDURE, TRIGGER or FUNCTION.</summary>
    private static bool StartsModule(IReadOnlyList<SqlToken> tokens, int i)
    {
        if (!tokens[i].IsWord("CREATE") && !tokens[i].IsWord("ALTER"))
        {
            return false;
        }

        var j = i + 1;
        if (j + 1 < tokens.Count && tokens[j].IsWord("OR") && tokens[j + 1].IsWord("ALTER"))
        {
            j += 2;
        }

        return j < tokens.Count && tokens[j].Kind == SqlTokenKind.Word && ModuleKinds.Contains(tokens[j].Text);
    }

    private static bool IsStatement(IReadOnlyList<SqlToken> tokens, int i)
    {
        if (i > 0)
        {
            var previous = tokens[i - 1];
            if (previous.IsPunctuation(',') || previous.IsPunctuation('.') ||
                (previous.Kind == SqlTokenKind.Word && NotAStatementAfter.Contains(previous.Text)))
            {
                return false;
            }
        }

        if (i + 1 >= tokens.Count)
        {
            return false;
        }

        var next = tokens[i + 1];
        return !next.IsPunctuation('(') && !next.IsPunctuation('.') && !next.IsWord("STATISTICS");
    }

    private static UnfilteredWrite? Check(IReadOnlyList<SqlToken> tokens, int start)
    {
        var verb = tokens[start].Text.ToUpperInvariant();
        var j = start + 1;

        // TOP (n) [PERCENT]
        if (j < tokens.Count && tokens[j].IsWord("TOP"))
        {
            j = SkipParentheses(tokens, j + 1);
            if (j < tokens.Count && tokens[j].IsWord("PERCENT"))
            {
                j++;
            }
        }

        if (verb == "DELETE" && j < tokens.Count && tokens[j].IsWord("FROM"))
        {
            j++;
        }

        var target = ReadName(tokens, ref j);
        if (target is null)
        {
            return null; // @table variable, OPENQUERY(...), or incomplete text
        }

        var targetName = target.Value.Name;
        var resolved = target.Value.Display;
        var depth = 0;
        var caseDepth = 0;
        var sawSet = false;
        for (; j < tokens.Count; j++)
        {
            var token = tokens[j];
            if (token.IsPunctuation('('))
            {
                depth++;
                continue;
            }

            if (token.IsPunctuation(')'))
            {
                if (--depth < 0)
                {
                    break;
                }

                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (token.IsPunctuation(';'))
            {
                break;
            }

            if (token.Kind != SqlTokenKind.Word)
            {
                continue;
            }

            if (token.IsWord("CASE"))
            {
                caseDepth++;
                continue;
            }

            if (caseDepth > 0)
            {
                if (token.IsWord("END"))
                {
                    caseDepth--;
                }

                continue;
            }

            if (token.IsWord("WHERE"))
            {
                return null;
            }

            if (token.IsWord("FROM") || token.IsWord("JOIN"))
            {
                // UPDATE o SET ... FROM dbo.Orders o: report the table behind the alias.
                var k = j + 1;
                if (ReadName(tokens, ref k) is { } source && k < tokens.Count)
                {
                    if (tokens[k].IsWord("AS"))
                    {
                        k++;
                    }

                    if (k < tokens.Count && tokens[k].IsIdentifier && string.Equals(tokens[k].Value, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = source.Display;
                    }
                }

                continue;
            }

            if (verb == "UPDATE" && !sawSet && token.IsWord("SET"))
            {
                sawSet = true;
                continue;
            }

            // WITH (ROWLOCK) is a table hint; WITH name AS (...) starts the next statement.
            if (token.IsWord("WITH") && j + 1 < tokens.Count && tokens[j + 1].IsPunctuation('('))
            {
                continue;
            }

            if (StatementStarts.Contains(token.Text))
            {
                break;
            }
        }

        return IsScratch(resolved) ? null : new UnfilteredWrite(verb, resolved, tokens[start].Start);
    }

    private static int SkipParentheses(IReadOnlyList<SqlToken> tokens, int j)
    {
        if (j >= tokens.Count || !tokens[j].IsPunctuation('('))
        {
            return j + 1; // TOP 10 without parentheses
        }

        var depth = 0;
        for (; j < tokens.Count; j++)
        {
            if (tokens[j].IsPunctuation('('))
            {
                depth++;
            }
            else if (tokens[j].IsPunctuation(')') && --depth == 0)
            {
                return j + 1;
            }
        }

        return j;
    }

    /// <summary>Reads [server.][db.][schema.]name; Name is the last part's value, Display the text as written.</summary>
    private static (string Name, string Display)? ReadName(IReadOnlyList<SqlToken> tokens, ref int j)
    {
        if (j >= tokens.Count || !tokens[j].IsIdentifier || IsReserved(tokens[j]))
        {
            return null;
        }

        var parts = new List<SqlToken> { tokens[j++] };
        while (j + 1 < tokens.Count && tokens[j].IsPunctuation('.') && tokens[j + 1].IsIdentifier)
        {
            parts.Add(tokens[j + 1]);
            j += 2;
        }

        return (parts[parts.Count - 1].Value, string.Join(".", parts.Select(p => p.Text)));
    }

    private static bool IsReserved(SqlToken token) =>
        token.Kind == SqlTokenKind.Word && (token.IsWord("SET") || token.IsWord("WHERE") || token.IsWord("FROM") || token.IsWord("OPENQUERY"));

    private static bool IsScratch(string name) => name.StartsWith("#", StringComparison.Ordinal) || name.StartsWith("@", StringComparison.Ordinal);
}
