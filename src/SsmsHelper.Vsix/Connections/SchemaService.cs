using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SsmsHelper.Core.Metadata;
using SsmsHelper.Core.Settings;

namespace SsmsHelper.Connections;

/// <summary>Process-wide schema cache shared by all query windows and helpers.</summary>
internal static class SchemaService
{
    /// <summary>Until the settings are read; then Options > Schema cache minutes (SettingsStore sets the cache's TTL).</summary>
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(HelperSettings.DefaultSchemaCacheMinutes);

    private static readonly ConcurrentDictionary<DatabaseKey, Func<System.Data.Common.DbConnection>> s_connectionFactories = new();

    public static SchemaCache Cache { get; } = CreateCache();

    /// <summary>
    /// Remembers how to connect to the database so the cache can load it, and returns its key.
    /// Call with a fresh <see cref="ActiveConnection"/> each time so changed credentials are picked up.
    /// </summary>
    public static DatabaseKey Register(ActiveConnection connection)
    {
        s_connectionFactories[connection.Key] = connection.CreateConnection;
        return connection.Key;
    }

    private static SchemaCache CreateCache()
    {
        var cache = new SchemaCache(LoadAsync, DefaultTimeToLive);
        cache.LoadFailed += (key, ex) => Log.Error($"Loading schema for {key} failed.", ex);
        return cache;
    }

    private static async Task<DatabaseSchema> LoadAsync(DatabaseKey key, CancellationToken cancellationToken)
    {
        if (!s_connectionFactories.TryGetValue(key, out var factory))
        {
            throw new InvalidOperationException($"No connection registered for {key}.");
        }

        using var connection = factory();
        return await SchemaLoader.LoadAsync(connection, DateTimeOffset.UtcNow, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
