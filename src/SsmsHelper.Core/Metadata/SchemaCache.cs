using System;
using System.Threading;
using System.Threading.Tasks;

namespace SsmsHelper.Core.Metadata;

/// <summary>Identifies one database on one server. Credentials are deliberately not part of the key.</summary>
public sealed record DatabaseKey(string Server, string Database)
{
    public bool Equals(DatabaseKey? other) =>
        other is not null &&
        string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Database, other.Database, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        unchecked((StringComparer.OrdinalIgnoreCase.GetHashCode(Server) * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Database));

    public override string ToString() => $"{Server}/{Database}";
}

/// <summary>
/// Per-database schema cache (see <see cref="LoadingCache{TKey, TValue}"/>): a schema is stale
/// <see cref="LoadingCache{TKey, TValue}.TimeToLive"/> after its <see cref="DatabaseSchema.LoadedAt"/>.
/// </summary>
public sealed class SchemaCache : LoadingCache<DatabaseKey, DatabaseSchema>
{
    /// <param name="load">Loads a schema. Runs once per key at a time and is not cancelled by callers.</param>
    /// <param name="timeToLive">Age after which an entry is refreshed on next access.</param>
    /// <param name="clock">Time source; defaults to UTC now.</param>
    public SchemaCache(
        Func<DatabaseKey, CancellationToken, Task<DatabaseSchema>> load,
        TimeSpan timeToLive,
        Func<DateTimeOffset>? clock = null)
        : base(load, s => s.LoadedAt, timeToLive, clock)
    {
    }
}
