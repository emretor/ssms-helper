using System.Collections.Generic;

namespace SsmsHelper.Core.Inserts;

/// <summary>An "INSERT [INTO] table [(columns)]" ending at the caret.</summary>
/// <param name="Schema">Schema part with delimiters removed, or null when unqualified.</param>
/// <param name="Name">Table name with delimiters removed.</param>
/// <param name="FullText">Table name as written, e.g. "dbo.[Order Lines]".</param>
/// <param name="Columns">Column names the user already listed (delimiters removed), or null when there is no list.</param>
/// <param name="ReplaceStart">End of the typed statement; generated text replaces from here to the caret.</param>
/// <param name="Indent">Leading whitespace of the line the INSERT keyword is on.</param>
public sealed record InsertContext(
    string? Schema,
    string Name,
    string FullText,
    IReadOnlyList<string>? Columns,
    int ReplaceStart,
    string Indent);
