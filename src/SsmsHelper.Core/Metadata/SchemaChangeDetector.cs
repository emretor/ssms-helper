using System;
using System.Collections.Generic;
using SsmsHelper.Core.Parsing;

namespace SsmsHelper.Core.Metadata;

/// <param name="ChangesSchema">The script creates, alters, drops or renames a table, view, procedure, function, ...</param>
/// <param name="Databases">Databases named in USE statements of the script, in order, without duplicates.</param>
/// <param name="ChangesDatabaseList">CREATE / ALTER / DROP DATABASE: the server's database list (after USE) changes.</param>
public sealed record SchemaChange(bool ChangesSchema, IReadOnlyList<string> Databases, bool ChangesDatabaseList = false);

/// <summary>
/// Does an executed script change what the helpers know (SSM-52 part 1)? One tokenizer pass on Execute, nothing
/// while typing. Temp tables and table variables do not count. DDL inside dynamic SQL (EXEC('...'), sp_executesql)
/// is a string here and is not seen.
/// </summary>
public static class SchemaChangeDetector
{
    private static readonly ISet<string> ObjectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TABLE", "VIEW", "PROCEDURE", "PROC", "FUNCTION", "SCHEMA", "SYNONYM", "TYPE", "TRIGGER", "SEQUENCE",
    };

    public static SchemaChange Find(string text)
    {
        var tokens = SqlTokenizer.Tokenize(text);
        var changes = false;
        var databaseList = false;
        var databases = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != SqlTokenKind.Word)
            {
                continue;
            }

            if (t.IsWord("USE") && i + 1 < tokens.Count && IsName(tokens[i + 1]))
            {
                var name = tokens[i + 1].Value;
                if (!databases.Exists(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)))
                {
                    databases.Add(name);
                }
            }
            else
            {
                changes = changes || IsDdl(tokens, i) || IsSelectInto(tokens, i) || IsRename(tokens, i);
                databaseList = databaseList || IsDatabaseDdl(tokens, i);
            }
        }

        return new SchemaChange(changes, databases, databaseList);
    }

    // CREATE / ALTER / DROP / CREATE OR ALTER <type> [IF EXISTS] name, not #temp.
    private static bool IsDdl(IReadOnlyList<SqlToken> tokens, int i)
    {
        var t = tokens[i];
        if (!t.IsWord("CREATE") && !t.IsWord("ALTER") && !t.IsWord("DROP"))
        {
            return false;
        }

        var k = i + 1;
        if (t.IsWord("CREATE") && k + 1 < tokens.Count && tokens[k].IsWord("OR") && tokens[k + 1].IsWord("ALTER"))
        {
            k += 2;
        }

        if (k >= tokens.Count || tokens[k].Kind != SqlTokenKind.Word || !ObjectTypes.Contains(tokens[k].Text))
        {
            return false;
        }

        k++;
        if (k + 1 < tokens.Count && tokens[k].IsWord("IF") && tokens[k + 1].IsWord("EXISTS"))
        {
            k += 2;
        }

        return k < tokens.Count && !IsTemp(tokens, k);
    }

    private static bool IsDatabaseDdl(IReadOnlyList<SqlToken> tokens, int i) =>
        (tokens[i].IsWord("CREATE") || tokens[i].IsWord("ALTER") || tokens[i].IsWord("DROP")) &&
        i + 1 < tokens.Count && tokens[i + 1].IsWord("DATABASE");

    // SELECT ... INTO dbo.Copy (not INSERT INTO, MERGE INTO, OUTPUT ... INTO, INTO #temp or INTO @var).
    private static bool IsSelectInto(IReadOnlyList<SqlToken> tokens, int i) =>
        tokens[i].IsWord("INTO") && i > 0 && !tokens[i - 1].IsWord("INSERT") && !tokens[i - 1].IsWord("MERGE") &&
        i + 1 < tokens.Count && IsName(tokens[i + 1]) && !IsTemp(tokens, i + 1) && BelongsToSelect(tokens, i);

    // Back from INTO at the same parenthesis level: SELECT comes first for SELECT INTO, OUTPUT for "OUTPUT ... INTO".
    private static bool BelongsToSelect(IReadOnlyList<SqlToken> tokens, int i)
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
                    return false;
                }

                depth--;
            }
            else if (depth == 0)
            {
                if (t.IsWord("SELECT"))
                {
                    return true;
                }

                if (t.IsWord("OUTPUT") || t.IsPunctuation(';'))
                {
                    return false;
                }
            }
        }

        return false;
    }

    // EXEC sp_rename / sys.sp_rename.
    private static bool IsRename(IReadOnlyList<SqlToken> tokens, int i) => tokens[i].IsWord("sp_rename");

    // The last part of "db.schema.#name" style names is what counts; temp names start with #.
    private static bool IsTemp(IReadOnlyList<SqlToken> tokens, int k)
    {
        while (k + 2 < tokens.Count && tokens[k + 1].IsPunctuation('.') && IsName(tokens[k + 2]))
        {
            k += 2;
        }

        return tokens[k].Value.StartsWith("#", StringComparison.Ordinal) || tokens[k].Kind == SqlTokenKind.Variable;
    }

    private static bool IsName(SqlToken token) => token.Kind is SqlTokenKind.Word or SqlTokenKind.QuotedIdentifier;
}
