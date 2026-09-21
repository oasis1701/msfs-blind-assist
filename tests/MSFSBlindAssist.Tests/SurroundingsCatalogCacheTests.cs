using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The cache's contract: ONE build per ICAO at a time, a build written back only if nothing
/// invalidated that airport while it ran, a remembered failure so a 2 s poll cannot hammer a
/// broken build, a DEGRADED build that expires on time so the tier it went without is asked
/// again, and the same GateDataSource.ShouldRebuildGateList staleness rule on both read paths.
/// Every wait here is gated on an event, never a sleep.
/// </summary>
public class SurroundingsCatalogCacheTests
{
    private static SurroundingsBuild One(string name = "Narrows Aviation", bool degraded = false) => new(
        new[] { new AirportFeature { Kind = FeatureKind.Fbo, Name = name, Lat = 47.27, Lon = -122.57, Source = FeatureSource.Osm } }, "Tower 118.5.", degraded);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    /// <summary>Continuations run asynchronously so completing a gate never drags the rest of the
    /// test onto the build thread that set it.</summary>
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Concurrent_callers_share_one_build_and_the_facts_ride_along()
    {
        int builds = 0; using var release = new ManualResetEventSlim();
        var cache = new SurroundingsCatalogCache { BuildSupplier = _ => { Interlocked.Increment(ref builds); release.Wait(Wait); return One(); } };
        var a = cache.GetAsync("KTIW"); var b = cache.GetAsync("ktiw");
        release.Set();
        Assert.Same(await a, await b);
        Assert.Equal(1, builds);
        Assert.Equal("Tower 118.5.", (await a)!.Facts);
        Assert.True(cache.TryGetCached("KTIW", out var cached)); Assert.Same(await a, cached);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_invalidation_that_lands_mid_build_is_not_undone_by_that_build(bool clearAll)
    {
        int builds = 0; using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var cache = new SurroundingsCatalogCache { BuildSupplier = _ => { int n = Interlocked.Increment(ref builds); if (n == 1) { started.Set(); release.Wait(Wait); } return One($"build {n}"); } };
        var first = cache.GetAsync("KTIW");
        Assert.True(started.Wait(Wait));
        if (clearAll) cache.Clear(); else cache.Invalidate("KTIW");     // the OSM fetch landed / the database was switched
        release.Set();
        Assert.Equal("build 1", (await first)!.Features[0].Name);       // its own awaiter still gets an answer
        Assert.False(cache.TryGetCached("KTIW", out _));                // …but the stale result was NOT cached
        Assert.Equal("build 2", (await cache.GetAsync("KTIW"))!.Features[0].Name);
    }

    [Fact]
    public async Task A_failed_build_is_remembered_so_a_2_second_poll_cannot_hammer_it()
    {
        int builds = 0; var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var cache = new SurroundingsCatalogCache(() => now) { BuildSupplier = _ => { builds++; throw new InvalidOperationException("db locked"); } };
        Assert.Null(await cache.GetAsync("KTIW"));
        Assert.Null(await cache.GetAsync("KTIW"));
        Assert.Equal(1, builds);
        now += SurroundingsCatalogCache.FailureMemory + TimeSpan.FromSeconds(1);
        await cache.GetAsync("KTIW");
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_build_finishing_after_an_invalidation_does_not_evict_the_replacement_build()
    {
        // The only interleaving where TWO builds for one airport overlap: build 1 is still running
        // when the OSM fetch invalidates the airport, so build 2 starts beside it. Build 1 must
        // leave build 2's in-flight entry alone when it finishes — evict it and the next caller
        // starts a THIRD build, and the two race to be the catalog that lands.
        var started = new[] { Gate(), Gate(), Gate() };
        var release = new[] { Gate(), Gate(), Gate() };
        int builds = 0;
        var cache = new SurroundingsCatalogCache
        {
            BuildSupplier = _ =>
            {
                int n = Interlocked.Increment(ref builds);
                started[n - 1].SetResult();
                release[n - 1].Task.Wait(Wait);
                return One($"build {n}");
            },
        };

        var first = cache.GetAsync("KTIW");
        await started[0].Task;
        cache.Invalidate("KTIW");                       // the OSM fetch landed
        var second = cache.GetAsync("KTIW");
        await started[1].Task;                          // both builds are now in flight

        release[0].SetResult();
        Assert.Equal("build 1", (await first)!.Features[0].Name);
        Assert.Same(second, cache.GetAsync("KTIW"));    // a third caller JOINS build 2

        release[1].SetResult();
        Assert.Equal("build 2", (await second)!.Features[0].Name);
        Assert.True(cache.TryGetCached("KTIW", out var cached));
        Assert.Equal("build 2", cached!.Features[0].Name);
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_failure_that_lands_after_a_database_switch_is_not_remembered()
    {
        // RefreshDatabaseProvider Clear()s the cache and pulls the provider out from under whatever
        // build is running, which is exactly what makes that build throw. Remembering THAT failure
        // would blank the airport for a minute on the NEW database.
        int builds = 0; using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var cache = new SurroundingsCatalogCache
        {
            BuildSupplier = _ =>
            {
                if (Interlocked.Increment(ref builds) > 1) return One();
                started.Set(); release.Wait(Wait);
                throw new InvalidOperationException("provider swapped");
            },
        };
        var first = cache.GetAsync("KTIW");
        Assert.True(started.Wait(Wait));
        cache.Clear();
        release.Set();
        Assert.Null(await first);
        Assert.NotNull(await cache.GetAsync("KTIW"));   // not held off by FailureMemory
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_token_upgrade_rebuilds_and_a_downgrade_does_not()
    {
        int builds = 0; string token = "navdata";
        var cache = new SurroundingsCatalogCache { VersionSupplier = _ => token, BuildSupplier = _ => { builds++; return One(); } };
        await cache.GetAsync("KJFK");
        token = "api:5"; await cache.GetAsync("KJFK");
        Assert.Equal(2, builds);
        token = "navdata";                                               // a transient GSX drop
        Assert.True(cache.TryGetCached("KJFK", out _));
        await cache.GetAsync("KJFK");
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_build_that_went_without_an_optional_tier_is_rebuilt_once_its_lifetime_is_up()
    {
        // A null OSM fetch raises no FeaturesUpdated (the continuation needs a non-empty result),
        // so nothing invalidates the airport and nothing asks the store again once its own failure
        // memory expires. Without an expiry the OSM-less catalog was simply the catalog, for the
        // session — which is what made the careful null-versus-empty handling below it buy nothing.
        int builds = 0; var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var cache = new SurroundingsCatalogCache(() => now) { BuildSupplier = _ => { builds++; return One($"build {builds}", degraded: true); } };
        Assert.Equal("build 1", (await cache.GetAsync("KTIW"))!.Features[0].Name);

        now += SurroundingsCatalogCache.DegradedLifetime - TimeSpan.FromSeconds(1);
        Assert.True(cache.TryGetCached("KTIW", out var still));            // served, not rebuilt, in the meantime
        Assert.Equal("build 1", still!.Features[0].Name);
        await cache.GetAsync("KTIW");
        Assert.Equal(1, builds);

        now += TimeSpan.FromSeconds(2);
        Assert.False(cache.TryGetCached("KTIW", out _));
        Assert.Equal("build 2", (await cache.GetAsync("KTIW"))!.Features[0].Name);
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task A_complete_build_never_goes_stale_on_time_alone()
    {
        int builds = 0; var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var cache = new SurroundingsCatalogCache(() => now) { BuildSupplier = _ => { builds++; return One(); } };
        await cache.GetAsync("KTIW");
        now += SurroundingsCatalogCache.DegradedLifetime + TimeSpan.FromHours(1);
        Assert.True(cache.TryGetCached("KTIW", out _));
        await cache.GetAsync("KTIW");
        Assert.Equal(1, builds);
    }

    [Fact]
    public void The_degraded_lifetime_is_the_stores_own_failure_memory()
        => Assert.Equal(OnlineFeatureStore.FailureMemory, SurroundingsCatalogCache.DegradedLifetime);

    [Fact]
    public async Task A_degraded_build_that_straddles_an_invalidation_is_still_discarded()
    {
        // The degraded flag must not buy a stale build a way back in: the generation check still
        // decides whether anything is written at all, and only then does the flag decide how long.
        int builds = 0; using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var cache = new SurroundingsCatalogCache
        {
            BuildSupplier = _ => { int n = Interlocked.Increment(ref builds); if (n == 1) { started.Set(); release.Wait(Wait); } return One($"build {n}", degraded: true); },
        };
        var first = cache.GetAsync("KTIW");
        Assert.True(started.Wait(Wait));
        cache.Invalidate("KTIW");
        release.Set();
        Assert.Equal("build 1", (await first)!.Features[0].Name);
        Assert.False(cache.TryGetCached("KTIW", out _));
        Assert.Equal("build 2", (await cache.GetAsync("KTIW"))!.Features[0].Name);
    }

    [Fact]
    public async Task A_blank_icao_is_nothing()
    {
        var cache = new SurroundingsCatalogCache();
        Assert.Null(await cache.GetAsync(" "));
        Assert.False(cache.TryGetCached("", out _));
    }
}
