using System;

namespace SsmsHelper.Core.Metadata;

/// <summary>
/// Schema-qualified object name. Equality ignores case, matching the default SQL Server
/// collations; case-sensitive databases are rare enough not to justify the complexity.
/// </summary>
public sealed record TableName(string Schema, string Name)
{
    public bool Equals(TableName? other) =>
        other is not null &&
        string.Equals(Schema, other.Schema, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        unchecked((StringComparer.OrdinalIgnoreCase.GetHashCode(Schema) * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    public override string ToString() => $"{Schema}.{Name}";
}
