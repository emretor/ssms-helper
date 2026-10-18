using SsmsHelper.Core.Metadata;

namespace SsmsHelper.Core.Tests.Metadata;

public class SchemaCacheTests
{
    private static readonly DatabaseKey Key = new("srv", "db");
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private DatabaseSchema Schema() => new(Array.Empty<TableInfo>(), Array.Empty<ForeignKeyInfo>(), _now);

    [Fact]
    public async Task GetAsync_LoadsOnce_ThenServesFromCache()
    {
        var loads = 0;
        var cache = new SchemaCache((_, _) => { loads++; return Task.FromResult(Schema()); }, Ttl, () => _now);

        var first = await cache.GetAsync(Key, Ct);
        var second = await cache.GetAsync(Key, Ct);

        Assert.Same(first, second);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Key_IsCaseInsensitive()
    {
        var loads = 0;
        var cache = new SchemaCache((_, _) => { loads++; return Task.FromResult(Schema()); }, Ttl, () => _now);

        await cache.GetAsync(new DatabaseKey("SRV", "DB"), Ct);
        await cache.GetAsync(Key, Ct);

        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task ConcurrentRequests_ShareOneLoad()
    {
        var loads = 0;
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => { Interlocked.Increment(ref loads); return gate.Task; }, Ttl, () => _now);

        var a = cache.GetAsync(Key, Ct);
        var b = cache.GetAsync(Key, Ct);
        Assert.Null(cache.TryGet(Key));
        gate.SetResult(Schema());

        Assert.Same(await a, await b);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task TryGet_ReturnsNullFirst_ThenTheLoadedSchema()
    {
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => gate.Task, Ttl, () => _now);

        Assert.Null(cache.TryGet(Key));
        var schema = Schema();
        gate.SetResult(schema);

        Assert.Same(schema, await cache.GetAsync(Key, Ct));
        Assert.Same(schema, cache.TryGet(Key));
    }

    [Fact]
    public async Task StaleEntry_IsServedByTryGet_WhileRefreshing()
    {
        var loads = 0;
        var cache = new SchemaCache((_, _) => { loads++; return Task.FromResult(Schema()); }, Ttl, () => _now);
        var old = await cache.GetAsync(Key, Ct);

        _now += Ttl;
        Assert.Same(old, cache.TryGet(Key));

        var fresh = await cache.GetAsync(Key, Ct);
        Assert.NotSame(old, fresh);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task FailedLoad_IsReported_AndRetriedNextTime()
    {
        var attempt = 0;
        var cache = new SchemaCache((_, _) =>
        {
            attempt++;
            return attempt == 1 ? Task.FromException<DatabaseSchema>(new TimeoutException("boom")) : Task.FromResult(Schema());
        }, Ttl, () => _now);
        Exception? reported = null;
        cache.LoadFailed += (_, ex) => reported = ex;

        await Assert.ThrowsAsync<TimeoutException>(() => cache.GetAsync(Key, Ct));
        Assert.IsType<TimeoutException>(reported);

        Assert.NotNull(await cache.GetAsync(Key, Ct));
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task Invalidate_DuringLoad_DoesNotCacheTheResult()
    {
        var loads = 0;
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => ++loads == 1 ? gate.Task : Task.FromResult(Schema()), Ttl, () => _now);

        var pending = cache.GetAsync(Key, Ct);
        cache.Invalidate(Key);
        gate.SetResult(Schema());
        await pending;

        Assert.Null(cache.TryGet(Key));
        await cache.GetAsync(Key, Ct);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task RefreshAsync_ForcesANewLoad()
    {
        var loads = 0;
        var cache = new SchemaCache((_, _) => { loads++; return Task.FromResult(Schema()); }, Ttl, () => _now);

        await cache.GetAsync(Key, Ct);
        await cache.RefreshAsync(Key, Ct);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task ReloadAsync_LoadsAgain_WhileTheOldSchemaKeepsServing()
    {
        var loads = 0;
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => ++loads == 1 ? Task.FromResult(Schema()) : gate.Task, Ttl, () => _now);
        var old = await cache.GetAsync(Key, Ct);

        var reloading = cache.ReloadAsync(Key, Ct);
        Assert.False(reloading.IsCompleted);
        Assert.Same(old, cache.TryGet(Key));

        var fresh = Schema();
        gate.SetResult(fresh);
        Assert.Same(fresh, await reloading);
        Assert.Same(fresh, cache.TryGet(Key));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task ReloadAsync_JoinsALoadAlreadyRunning()
    {
        var loads = 0;
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => { Interlocked.Increment(ref loads); return gate.Task; }, Ttl, () => _now);

        var first = cache.GetAsync(Key, Ct);
        var reload = cache.ReloadAsync(Key, Ct);
        gate.SetResult(Schema());

        Assert.Same(await first, await reload);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task GetAsync_CanBeCancelled_WithoutCancellingTheSharedLoad()
    {
        var gate = new TaskCompletionSource<DatabaseSchema>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new SchemaCache((_, _) => gate.Task, Ttl, () => _now);
        using var cts = new CancellationTokenSource();

        var cancelled = cache.GetAsync(Key, cts.Token);
        var other = cache.GetAsync(Key, Ct);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.SetResult(Schema());
        Assert.NotNull(await other);
    }
}
