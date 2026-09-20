using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

public class OnlineFeatureStoreTests
{
    private static readonly IReadOnlyList<AirportFeature> OneHangar =
        new[] { new AirportFeature { Kind = FeatureKind.Hangar, Name = "Narrows Aviation", Lat = 47.27, Lon = -122.57, Source = FeatureSource.Osm } };
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5), Short = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Concurrent_callers_share_one_fetch_and_the_result_is_cached()
    {
        int fetches = 0; var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var store = new OnlineFeatureStore((_, _, _, _, _) => { Interlocked.Increment(ref fetches); return gate.Task; }) { Enabled = true };
        var a = store.GetAsync("KTIW", 0, 0, null, Long);
        var b = store.GetAsync("ktiw", 0, 0, null, Long);
        gate.SetResult(OneHangar);
        Assert.Single(await a); Assert.Single(await b);
        Assert.Single(await store.GetAsync("KTIW", 0, 0, null, Long));
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task A_caller_that_gives_up_gets_nothing_now_and_an_event_when_the_fetch_lands()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var updated = new TaskCompletionSource<string>();
        var store = new OnlineFeatureStore((_, _, _, _, _) => gate.Task) { Enabled = true };
        store.FeaturesUpdated += icao => updated.TrySetResult(icao);

        Assert.Empty(await store.GetAsync("KTIW", 0, 0, null, Short));
        gate.SetResult(OneHangar);
        Assert.Equal("KTIW", await updated.Task.WaitAsync(Long));
        Assert.Single(await store.GetAsync("KTIW", 0, 0, null, Short));
    }

    [Fact]
    public async Task No_event_when_every_waiter_received_the_result()
    {
        int events = 0;
        var store = new OnlineFeatureStore((_, _, _, _, _) => Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar)) { Enabled = true };
        store.FeaturesUpdated += _ => events++;
        Assert.Single(await store.GetAsync("KTIW", 0, 0, null, Long));
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Disabled_means_no_request_at_all()
    {
        int fetches = 0;
        var store = new OnlineFeatureStore((_, _, _, _, _) => { fetches++; return Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar); });
        Assert.Empty(await store.GetAsync("KTIW", 0, 0, null, Long));
        Assert.Equal(0, fetches);
    }

    [Fact]
    public async Task A_failed_fetch_is_remembered_so_the_mirrors_are_not_hammered()
    {
        int fetches = 0; var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var store = new OnlineFeatureStore((_, _, _, _, _) => { fetches++; return Task.FromResult<IReadOnlyList<AirportFeature>?>(null); }, () => now) { Enabled = true };
        Assert.Empty(await store.GetAsync("KTIW", 0, 0, null, Long));
        Assert.Empty(await store.GetAsync("KTIW", 0, 0, null, Long));
        Assert.Equal(1, fetches);
        now += OnlineFeatureStore.FailureMemory + TimeSpan.FromSeconds(1);
        await store.GetAsync("KTIW", 0, 0, null, Long);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task A_throwing_fetch_never_escapes()
    {
        var store = new OnlineFeatureStore((_, _, _, _, _) => throw new InvalidOperationException("boom")) { Enabled = true };
        Assert.Empty(await store.GetAsync("KTIW", 0, 0, null, Long));
    }
}
