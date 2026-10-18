using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Services/Volanta/VolantaPlaceCache: each aircraft's place is looked up once, then
// again only after it has moved 2 nm, nearest first, a batch at a time.
public class VolantaPlaceCacheTests
{
    private static VolantaTrafficEntry At(string id, double lat, double lon, double distanceNm = 10)
        => new(new VolantaFlight { Id = id }, lat, lon, distanceNm, 0, 0, 12, 0, 0);

    [Fact]
    public void Aircraft_never_looked_up_are_due_in_the_order_given_up_to_the_batch_size()
    {
        var cache = new VolantaPlaceCache<string>();
        var due = cache.Due(new[] { At("near", 0, 0), At("mid", 0, 1), At("far", 0, 2), At("near", 0, 0) }, max: 2);
        Assert.Equal(new[] { "near", "mid" }, due.Select(e => e.Flight.Id));
    }

    [Fact]
    public void A_place_is_looked_up_again_only_after_the_aircraft_moved_two_nm()
    {
        var cache = new VolantaPlaceCache<string>();
        cache.Store("a", 0, 0, "Pike Lake, Wisconsin, United States");
        Assert.Equal("Pike Lake, Wisconsin, United States", cache.PlaceOf("a"));
        Assert.Empty(cache.Due(new[] { At("a", 0, 1.9 / 60) }, 10));   // 1.9 nm east
        Assert.Single(cache.Due(new[] { At("a", 0, 2.1 / 60) }, 10));  // 2.1 nm east
    }

    [Fact]
    public void A_lookup_that_found_nothing_is_kept_so_it_is_not_retried_in_place()
    {
        var cache = new VolantaPlaceCache<string>();
        cache.Store("a", 10, 10, null);
        Assert.Null(cache.PlaceOf("a"));
        Assert.Empty(cache.Due(new[] { At("a", 10, 10) }, 10));
    }

    [Fact]
    public void Aircraft_that_left_the_map_are_forgotten()
    {
        var cache = new VolantaPlaceCache<string>();
        cache.Store("stays", 0, 0, "Here");
        cache.Store("gone", 0, 0, "There");
        cache.KeepOnly(new[] { "stays" });
        Assert.Equal(1, cache.Count);
        Assert.Null(cache.PlaceOf("gone"));
        Assert.Equal("Here", cache.PlaceOf("stays"));
    }

    [Fact]
    public void Only_one_burst_of_lookups_fills_a_cache_at_a_time()
    {
        var cache = new VolantaPlaceCache<string>();
        Assert.True(cache.TryBeginLookup());
        Assert.False(cache.TryBeginLookup());
        Assert.True(new VolantaPlaceCache<string>().TryBeginLookup());   // the other kind of place has its own
        cache.EndLookup();
        Assert.True(cache.TryBeginLookup());
    }
}
