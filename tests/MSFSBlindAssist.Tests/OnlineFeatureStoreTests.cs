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
        var store = new OnlineFeatureStore((_, _, _) => { Interlocked.Increment(ref fetches); return gate.Task; }) { Enabled = true };
        var a = store.GetAsync("KTIW", null, Long);
        var b = store.GetAsync("ktiw", null, Long);
        gate.SetResult(OneHangar);
        Assert.Single((await a).Features); Assert.Single((await b).Features);
        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task A_caller_that_gives_up_gets_nothing_now_and_an_event_when_the_fetch_lands()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var updated = new TaskCompletionSource<string>();
        var store = new OnlineFeatureStore((_, _, _) => gate.Task) { Enabled = true };
        store.FeaturesUpdated += icao => updated.TrySetResult(icao);

        Assert.Empty((await store.GetAsync("KTIW", null, Short)).Features);
        gate.SetResult(OneHangar);
        Assert.Equal("KTIW", await updated.Task.WaitAsync(Long));
        Assert.Single((await store.GetAsync("KTIW", null, Short)).Features);
    }

    [Fact]
    public async Task No_event_when_every_waiter_received_the_result()
    {
        int events = 0;
        var store = new OnlineFeatureStore((_, _, _) => Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar)) { Enabled = true };
        store.FeaturesUpdated += _ => events++;
        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Disabled_means_no_request_at_all()
    {
        int fetches = 0;
        var store = new OnlineFeatureStore((_, _, _) => { fetches++; return Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar); });
        Assert.Empty((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(0, fetches);
    }

    [Fact]
    public async Task A_failed_fetch_is_remembered_so_the_mirrors_are_not_hammered()
    {
        int fetches = 0; var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var store = new OnlineFeatureStore((_, _, _) => { fetches++; return Task.FromResult<IReadOnlyList<AirportFeature>?>(null); }, () => now) { Enabled = true };
        Assert.Empty((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Empty((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(1, fetches);
        now += OnlineFeatureStore.FailureMemory + TimeSpan.FromSeconds(1);
        await store.GetAsync("KTIW", null, Long);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task A_throwing_fetch_never_escapes()
    {
        var store = new OnlineFeatureStore((_, _, _) => throw new InvalidOperationException("boom")) { Enabled = true };
        Assert.Empty((await store.GetAsync("KTIW", null, Long)).Features);
    }

    [Fact]
    public async Task Clear_empties_a_served_airport_so_the_next_call_fetches_again()
    {
        int fetches = 0;
        var store = new OnlineFeatureStore((_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            return Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar);
        })
        { Enabled = true };

        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(1, fetches);                                          // served from the cache

        store.Clear();
        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task A_fetch_still_running_across_a_Clear_is_discarded_and_the_next_call_refetches()
    {
        // A database switch clears the store: the box that filtered the fallback came from the OLD
        // database, so a fetch that started under it must not land afterwards.
        int fetches = 0, events = 0;
        var first = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var store = new OnlineFeatureStore((_, _, _) =>
            Interlocked.Increment(ref fetches) == 1 ? first.Task : Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar))
        { Enabled = true };
        store.FeaturesUpdated += _ => Interlocked.Increment(ref events);

        Assert.Empty((await store.GetAsync("KTIW", null, Short)).Features);     // gives up: the event is armed
        var sharing = store.GetAsync("KTIW", null, Long);            // shares that same fetch

        store.Clear();
        first.SetResult(OneHangar);

        Assert.Empty((await sharing).Features);          // completing the awaited fetch is the barrier: it has landed
        Assert.Equal(0, events);              // …and landed on nothing, so nobody is told to rebuild
        Assert.Equal(1, fetches);

        Assert.Single((await store.GetAsync("KTIW", null, Long)).Features);     // a fresh fetch, not the discarded one
        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task An_airport_with_no_buildings_is_SERVED_not_failed()
    {
        // The distinction the caller needs and cannot make for itself: an empty list from a mirror
        // that answered is a FACT about the airport, while an empty list from a mirror that timed
        // out or refused is a gap to come back for. Inferring it from the list would treat every
        // building-less airport as permanently degraded, rebuilding its catalog every five minutes.
        var store = new OnlineFeatureStore((_, _, _) => Task.FromResult<IReadOnlyList<AirportFeature>?>(Array.Empty<AirportFeature>())) { Enabled = true };
        var served = await store.GetAsync("KTIW", null, Long);
        Assert.Empty(served.Features);
        Assert.Equal(OnlineFeatureStatus.Served, served.Status);
        Assert.Equal(OnlineFeatureStatus.Served, (await store.GetAsync("KTIW", null, Long)).Status);   // and from the cache
    }

    [Fact]
    public async Task A_caller_that_gives_up_waiting_reports_PENDING_and_a_refusal_reports_FAILED()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var store = new OnlineFeatureStore((_, _, _) => gate.Task) { Enabled = true };
        Assert.Equal(OnlineFeatureStatus.Pending, (await store.GetAsync("KTIW", null, Short)).Status);
        gate.SetResult(null);                                              // the mirror refused
        Assert.Equal(OnlineFeatureStatus.Failed, (await store.GetAsync("KTIW", null, Long)).Status);
        Assert.Equal(OnlineFeatureStatus.Failed, (await store.GetAsync("KTIW", null, Long)).Status);   // and while remembered
    }

    [Fact]
    public async Task A_tier_the_pilot_switched_off_is_DISABLED_and_is_not_a_gap()
    {
        var store = new OnlineFeatureStore((_, _, _) => Task.FromResult<IReadOnlyList<AirportFeature>?>(OneHangar));
        Assert.Equal(OnlineFeatureStatus.Disabled, (await store.GetAsync("KTIW", null, Long)).Status);
        store.Enabled = true;
        Assert.Equal(OnlineFeatureStatus.Disabled, (await store.GetAsync(" ", null, Long)).Status);
    }

    [Fact]
    public async Task A_prefetch_starts_the_fetch_by_itself_and_a_later_caller_joins_it()
    {
        // The catalog build starts the fetch BEFORE its other tiers (ML-6) and asks for the answer
        // after them. The fetch must really be running with nobody waiting on it yet, and the later
        // ask must JOIN it rather than send the mirrors a second request.
        int fetches = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var store = new OnlineFeatureStore((_, _, _) =>
        {
            Interlocked.Increment(ref fetches);
            started.TrySetResult();
            return gate.Task;
        }) { Enabled = true };

        store.Prefetch("KTIW", null);
        await started.Task.WaitAsync(Long);
        var joined = store.GetAsync("ktiw", null, Long);
        gate.SetResult(OneHangar);
        Assert.Single((await joined).Features);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task A_build_that_outran_its_whole_wait_still_takes_an_answer_that_landed_meanwhile()
    {
        // The scenery tier took longer than CatalogWait, so the build asks with NOTHING left of it —
        // and the mirror answered while the scan ran. A zero wait must still hand that answer over as
        // Served, or the catalog is built without buildings the store already holds. GetAsync's
        // served check already runs before any wait, so this pins behaviour the zero wait RELIES on.
        var gate = new TaskCompletionSource<IReadOnlyList<AirportFeature>?>();
        var store = new OnlineFeatureStore((_, _, _) => gate.Task) { Enabled = true };
        store.Prefetch("KTIW", null);
        gate.SetResult(OneHangar);
        await store.GetAsync("KTIW", null, Long);                // barrier: the answer is stored

        var wait = OnlineFeatureStore.RemainingWait(OnlineFeatureStore.CatalogWait + TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.Zero, wait);
        var taken = await store.GetAsync("KTIW", null, wait);
        Assert.Equal(OnlineFeatureStatus.Served, taken.Status);
        Assert.Single(taken.Features);
    }

    [Fact]
    public void The_remaining_wait_is_what_is_left_of_the_catalog_wait_and_never_negative()
    {
        Assert.Equal(OnlineFeatureStore.CatalogWait, OnlineFeatureStore.RemainingWait(TimeSpan.Zero));
        Assert.Equal(OnlineFeatureStore.CatalogWait - TimeSpan.FromSeconds(1), OnlineFeatureStore.RemainingWait(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.Zero, OnlineFeatureStore.RemainingWait(OnlineFeatureStore.CatalogWait));
        Assert.Equal(TimeSpan.Zero, OnlineFeatureStore.RemainingWait(TimeSpan.FromMinutes(1)));
    }
}
