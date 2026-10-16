using System;
using System.Collections.Generic;
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
/// Per-database schema cache. Concurrent requests for the same database share one load,
/// stale entries keep serving until a refresh completes, and failed loads are retried on the
/// next request. Thread-safe; never blocks the caller while loading.
/// </summary>
public sealed class SchemaCache
{
    private readonly Func<DatabaseKey, CancellationToken, Task<DatabaseSchema>> _load;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<DatabaseKey, Entry> _entries = new();

    /// <param name="load">Loads a schema. Runs once per key at a time and is not cancelled by callers.</param>
    /// <param name="timeToLive">Age after which an entry is refreshed on next access.</param>
    /// <param name="clock">Time source; defaults to UTC now.</param>
    public SchemaCache(
        Func<DatabaseKey, CancellationToken, Task<DatabaseSchema>> load,
        TimeSpan timeToLive,
        Func<DateTimeOffset>? clock = null)
    {
        _load = load;
        TimeToLive = timeToLive;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan TimeToLive { get; set; }

    /// <summary>Raised on the loading thread when a load fails.</summary>
    public event Action<DatabaseKey, Exception>? LoadFailed;

    /// <summary>
    /// Returns the cached schema immediately, even if stale, or null if none is loaded yet.
    /// Starts a background load when the entry is missing or stale. Use this from UI code.
    /// </summary>
    public DatabaseSchema? TryGet(DatabaseKey key)
    {
        lock (_gate)
        {
            var entry = GetOrAddEntry(key);
            if (entry.Schema is null || IsStale(entry))
            {
                StartLoad(key, entry);
            }

            return entry.Schema;
        }
    }

    /// <summary>Returns a fresh schema, waiting for a load if the entry is missing or stale.</summary>
    public Task<DatabaseSchema> GetAsync(DatabaseKey key, CancellationToken cancellationToken = default)
    {
        Task<DatabaseSchema> pending;
        lock (_gate)
        {
            var entry = GetOrAddEntry(key);
            if (entry.Schema is not null && !IsStale(entry))
            {
                return Task.FromResult(entry.Schema);
            }

            pending = StartLoad(key, entry);
        }

        return WaitAsync(pending, cancellationToken);
    }

    /// <summary>Discards the cached schema and returns a newly loaded one.</summary>
    public Task<DatabaseSchema> RefreshAsync(DatabaseKey key, CancellationToken cancellationToken = default)
    {
        Invalidate(key);
        return GetAsync(key, cancellationToken);
    }

    /// <summary>Drops the entry. A load already running still completes but its result is discarded.</summary>
    public void Invalidate(DatabaseKey key)
    {
        lock (_gate)
        {
            _entries.Remove(key);
        }
    }

    public void InvalidateAll()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private Entry GetOrAddEntry(DatabaseKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry();
            _entries[key] = entry;
        }

        return entry;
    }

    private bool IsStale(Entry entry) => entry.Schema is not null && _clock() - entry.Schema.LoadedAt >= TimeToLive;

    // Caller holds _gate.
    private Task<DatabaseSchema> StartLoad(DatabaseKey key, Entry entry)
    {
        if (entry.Loading is not null)
        {
            return entry.Loading;
        }

        // Task.Run keeps the loader off the caller's thread (the UI thread in the extension).
        var loading = Task.Run(() => LoadAndStoreAsync(key, entry));
        entry.Loading = loading;
        return loading;
    }

    // Stores the result before the returned task completes, so anyone awaiting it sees the cache updated.
    private async Task<DatabaseSchema> LoadAndStoreAsync(DatabaseKey key, Entry entry)
    {
        try
        {
            var schema = await _load(key, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                entry.Loading = null;

                // Results for entries invalidated while loading are returned but not cached.
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    entry.Schema = schema;
                }
            }

            return schema;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                entry.Loading = null;
            }

            LoadFailed?.Invoke(key, ex);
            throw;
        }
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task.ConfigureAwait(false);
    }

    private sealed class Entry
    {
        public DatabaseSchema? Schema { get; set; }

        public Task<DatabaseSchema>? Loading { get; set; }
    }
}
