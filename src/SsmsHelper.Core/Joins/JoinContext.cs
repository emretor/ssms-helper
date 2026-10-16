using System.Collections.Generic;

namespace SsmsHelper.Core.Joins;

/// <summary>A table source in a FROM clause, as written by the user.</summary>
/// <param name="Schema">Schema part, or null when unqualified.</param>
/// <param name="Name">Object name with delimiters removed. Empty for derived tables.</param>
/// <param name="NameText">Object name as written, e.g. "[Order Lines]".</param>
/// <param name="FullText">Full multi-part name as written, e.g. "dbo.Orders".</param>
/// <param name="AliasText">Alias as written, or null.</param>
/// <param name="IsJoined">True when introduced by JOIN, false for FROM or a comma.</param>
/// <param name="IsDerived">True for subqueries and table-valued functions, which cannot be resolved.</param>
public sealed record TableReference(
    string? Schema,
    string Name,
    string NameText,
    string FullText,
    string? AliasText,
    bool IsJoined,
    bool IsDerived);

public enum JoinContextKind
{
    /// <summary>Caret follows "JOIN table [alias]"; a suggestion is inserted with "ON ".</summary>
    AfterJoinTarget,

    /// <summary>Caret follows "JOIN table [alias] ON", possibly with a partly typed condition.</summary>
    AfterOn,
}

/// <param name="Target">The table being joined.</param>
/// <param name="Previous">Earlier table sources in the same FROM clause, in source order.</param>
/// <param name="ReplaceStart">Start of the text a suggestion replaces; the caret is the end.</param>
/// <param name="FilterText">Text the user already typed between ReplaceStart and the caret.</param>
/// <param name="Prefix">Text inserted before the condition, such as " ON ".</param>
public sealed record JoinContext(
    JoinContextKind Kind,
    TableReference Target,
    IReadOnlyList<TableReference> Previous,
    int ReplaceStart,
    string FilterText,
    string Prefix);
