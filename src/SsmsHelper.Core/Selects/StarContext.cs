using System.Collections.Generic;
using SsmsHelper.Core.Joins;

namespace SsmsHelper.Core.Selects;

/// <summary>A "*" or "alias.*" in a SELECT list, directly before the caret.</summary>
/// <param name="ReplaceStart">Start of "*" or of "alias.*".</param>
/// <param name="ReplaceEnd">End of the "*" (the caret).</param>
/// <param name="Qualifier">The "alias" of "alias.*" (delimiters removed), or null for a bare "*".</param>
/// <param name="Tables">Table sources of the SELECT's FROM clause, in source order.</param>
/// <param name="AlignIndent">Whitespace that lines a following line up under <paramref name="ReplaceStart"/>.</param>
/// <param name="StatementIndent">Leading whitespace of the SELECT keyword's line.</param>
/// <param name="FromOnSameLine">
/// Start of the FROM keyword when it directly follows the "*" on the same line, otherwise null.
/// </param>
/// <param name="IsFirstItem">True when the "*" is the first SELECT list item (not after a comma).</param>
/// <param name="PreviousTokenEnd">
/// End of the token before the "*" (SELECT, DISTINCT, TOP (n) or a comma) when only whitespace separates them;
/// otherwise <paramref name="ReplaceStart"/>, so a comment in between is never replaced.
/// </param>
public sealed record StarContext(
    int ReplaceStart,
    int ReplaceEnd,
    string? Qualifier,
    IReadOnlyList<TableReference> Tables,
    string AlignIndent,
    string StatementIndent,
    int? FromOnSameLine,
    bool IsFirstItem,
    int PreviousTokenEnd);
