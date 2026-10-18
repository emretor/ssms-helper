using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SsmsHelper.Core.Metadata;

/// <summary>
/// Cache of values loaded in the background (a database's schema, a server's database list). Concurrent requests
/// for the same key share one load, stale entries keep serving until a refresh completes, and failed loads are
/// retried on the next request. Thread-safe; never blocks the caller while loading.
/// </summary>
public class LoadingCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly Func<TKey, CancellationToken, Task<TValue>> _load;
    private readonly Func<TValue, DateTimeOffset> _loadedAt;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries;

    /// <param name="load">Loads a value. Runs once per key at a time and is not cancelled by callers.</param>
    /// <param name="loadedAt">When a value was loaded, for the time to live.</param>
    /// <param name="timeToLive">Age after which an entry is refreshed on next access.</param>
    /// <param name="clock">Time source; defaults to UTC now.</param>
    /// <param name="comparer">Key comparer; defaults to the key's own equality.</param>
    public LoadingCache(
        Func<TKey, CancellationToken, Task<TValue>> load,
        Func<TValue, DateTimeOffset> loadedAt,
        TimeSpan timeToLive,
        Func<DateTimeOffset>? clock = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        _load = load;
        _loadedAt = loadedAt;
        TimeToLive = timeToLive;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _entries = new Dictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
    }

    public TimeSpan TimeToLive { get; set; }

    /// <summary>Raised on the loading thread when a load fails.</summary>
    public event Action<TKey, Exception>? LoadFailed;

    /// <summary>
    /// Returns the cached value immediately, even if stale, or null if none is loaded yet.
    /// Starts a background load when the entry is missing or stale. Use this from UI code.
    /// </summary>
    public TValue? TryGet(TKey key)
    {
        lock (_gate)
        {
            var entry = GetOrAddEntry(key);
            if (entry.Value is null || IsStale(entry))
            {
                StartLoad(key, entry);
            }

            return entry.Value;
        }
    }

    /// <summary>Returns a fresh value, waiting for a load if the entry is missing or stale.</summary>
    public Task<TValue> GetAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Task<TValue> pending;
        lock (_gate)
        {
            var entry = GetOrAddEntry(key);
            if (entry.Value is not null && !IsStale(entry))
            {
                return Task.FromResult(entry.Value);
            }

            pending = StartLoad(key, entry);
        }

        return WaitAsync(pending, cancellationToken);
    }

    /// <summary>Discards the cached value and returns a newly loaded one.</summary>
    public Task<TValue> RefreshAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Invalidate(key);
        return GetAsync(key, cancellationToken);
    }

    /// <summary>
    /// Loads the value again while the current one keeps serving <see cref="TryGet"/>, so helpers never wait.
    /// Joins a load already running instead of starting a second one.
    /// </summary>
    public Task<TValue> ReloadAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Task<TValue> pending;
        lock (_gate)
        {
            pending = StartLoad(key, GetOrAddEntry(key));
        }

        return WaitAsync(pending, cancellationToken);
    }

    /// <summary>Drops the entry. A load already running still completes but its result is discarded.</summary>
    public void Invalidate(TKey key)
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

    private Entry GetOrAddEntry(TKey key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry();
            _entries[key] = entry;
        }

        return entry;
    }

    private bool IsStale(Entry entry) => entry.Value is not null && _clock() - _loadedAt(entry.Value) >= TimeToLive;

    // Caller holds _gate.
    private Task<TValue> StartLoad(TKey key, Entry entry)
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
    private async Task<TValue> LoadAndStoreAsync(TKey key, Entry entry)
    {
        try
        {
            var value = await _load(key, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                entry.Loading = null;

                // Results for entries invalidated while loading are returned but not cached.
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    entry.Value = value;
                }
            }

            return value;
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
        public TValue? Value { get; set; }

        public Task<TValue>? Loading { get; set; }
    }
}
