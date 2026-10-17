namespace SsmsHelper.Core.Execs;

/// <summary>An "EXEC[UTE] [@ret =] [db.][schema.]procedure" ending at the caret.</summary>
/// <param name="Schema">Schema part with delimiters removed, or null when unqualified.</param>
/// <param name="Name">Procedure name with delimiters removed.</param>
/// <param name="FullText">Procedure name as written, e.g. "dbo.GetOrders".</param>
/// <param name="ReplaceStart">End of the procedure name; generated text replaces from here to the caret.</param>
/// <param name="Indent">Leading whitespace of the EXEC line.</param>
/// <param name="NameEndColumn">Column (characters from the line start) where the procedure name ends.</param>
public sealed record ExecContext(string? Schema, string Name, string FullText, int ReplaceStart, string Indent, int NameEndColumn);
