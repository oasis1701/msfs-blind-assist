using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The cache's contract: ONE build per ICAO at a time, a build written back only if nothing
/// invalidated that airport while it ran, a remembered failure so a 2 s poll cannot hammer a
/// broken build, and the same GateDataSource.ShouldRebuildGateList staleness rule on both read
/// paths. Every wait here is gated on an event, never a sleep.
/// </summary>
public class SurroundingsCatalogCacheTests
{
    private static SurroundingsBuild One(string name = "Narrows Aviation") => new(
        new[] { new AirportFeature { Kind = FeatureKind.Fbo, Name = name, Lat = 47.27, Lon = -122.57, Source = FeatureSource.Osm } }, "Tower 118.5.");
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

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
    public async Task A_blank_icao_is_nothing()
    {
        var cache = new SurroundingsCatalogCache();
        Assert.Null(await cache.GetAsync(" "));
        Assert.False(cache.TryGetCached("", out _));
    }
}
