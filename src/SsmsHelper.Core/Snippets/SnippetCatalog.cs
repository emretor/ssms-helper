using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsHelper.Core.Snippets;

/// <summary>Case-insensitive set of snippets keyed by shortcut. Later entries override earlier ones.</summary>
public sealed class SnippetCatalog
{
    private readonly Dictionary<string, Snippet> _byShortcut;

    public SnippetCatalog(IEnumerable<Snippet> snippets)
    {
        _byShortcut = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in snippets)
        {
            _byShortcut[snippet.Shortcut] = snippet;
        }
    }

    public IReadOnlyCollection<Snippet> All => _byShortcut.Values;

    public bool TryGet(string shortcut, out Snippet snippet)
    {
        if (_byShortcut.TryGetValue(shortcut, out var found))
        {
            snippet = found;
            return true;
        }

        snippet = null!;
        return false;
    }

    /// <summary>Returns a new catalog with <paramref name="overrides"/> replacing or adding entries.</summary>
    public SnippetCatalog With(IEnumerable<Snippet> overrides) => new(All.Concat(overrides));

    public static SnippetCatalog Default { get; } = new(new[]
    {
        new Snippet("ssf", "SELECT * FROM $CURSOR$", "SELECT * FROM"),
        new Snippet("st100", "SELECT TOP (100) * FROM $CURSOR$", "SELECT TOP (100) * FROM"),
        new Snippet("scf", "SELECT COUNT(*) FROM $CURSOR$", "SELECT COUNT(*) FROM"),
        new Snippet("ii", "INSERT INTO $CURSOR$", "INSERT INTO"),
        // Caret on an indented line of its own for the table name or a query; WHERE always present as a reminder.
        new Snippet("upd", "UPDATE\n    $CURSOR$\nSET\nWHERE", "UPDATE ... SET ... WHERE"),
        new Snippet("df", "DELETE FROM\n    $CURSOR$\nWHERE", "DELETE FROM ... WHERE"),
        new Snippet("cte", "WITH cte AS\n(\n    $CURSOR$\n)\nSELECT * FROM cte;", "Common table expression"),
        new Snippet("bt", "BEGIN TRANSACTION;\n\n$CURSOR$\n\n-- COMMIT TRANSACTION;\n-- ROLLBACK TRANSACTION;", "Transaction with commented COMMIT/ROLLBACK"),
    });
}
