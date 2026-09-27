using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AirportWarmUpTests
{
    private sealed class Recorder
    {
        public readonly List<string> Names = new(), Surroundings = new();
        public readonly HashSet<string> Claimed = new(StringComparer.OrdinalIgnoreCase);
        public bool OnlineNamesOn = true;
        public AirportWarmUp Build() => new(
            claimNames: icao => Claimed.Add(icao),
            onlineNamesEnabled: () => OnlineNamesOn,
            prefetchNames: Names.Add,
            buildSurroundings: Surroundings.Add);
    }

    [Fact]
    public void On_the_ground_at_an_airport_both_the_names_and_the_surroundings_are_fetched()
    {
        var r = new Recorder(); var w = r.Build();
        w.AtCurrentAirport("KTIW", onGround: true);
        Assert.Equal(new[] { "KTIW" }, r.Names);
        Assert.Equal(new[] { "KTIW" }, r.Surroundings);
    }

    [Fact]
    public void In_the_air_the_airport_below_is_not_warmed()
    {
        var r = new Recorder(); var w = r.Build();
        w.AtCurrentAirport("KTIW", onGround: false);
        Assert.Empty(r.Names);
        Assert.Empty(r.Surroundings);
    }

    [Fact]
    public void A_destination_is_warmed_whatever_the_aircraft_is_doing()
    {
        // No ground flag at all: Shift+D is as often pressed in the cruise as at the gate.
        var r = new Recorder(); var w = r.Build();
        w.AtDestination("EGLL");
        Assert.Equal(new[] { "EGLL" }, r.Names);
        Assert.Equal(new[] { "EGLL" }, r.Surroundings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_airport_warms_nothing(string? icao)
    {
        var r = new Recorder(); var w = r.Build();
        w.AtCurrentAirport(icao, onGround: true);
        w.AtDestination(icao);
        Assert.Empty(r.Names);
        Assert.Empty(r.Surroundings);
    }

    [Fact]
    public void Names_are_fetched_once_per_airport_and_share_the_claim_the_other_prefetches_use()
    {
        // The taxi form, ILS guidance and the landing-exit planner claim an airport in the same
        // set; a name fetch they already made is not made again. The surroundings build is asked
        // every time: the catalog cache answers a fresh one at once and rebuilds a stale one.
        var r = new Recorder(); var w = r.Build();
        r.Claimed.Add("KDEN");
        w.AtCurrentAirport("KDEN", onGround: true);
        w.AtDestination("kden");
        Assert.Empty(r.Names);
        Assert.Equal(new[] { "KDEN", "kden" }, r.Surroundings);
    }

    [Fact]
    public void With_online_taxi_data_switched_off_no_names_are_fetched_and_none_are_claimed()
    {
        var r = new Recorder { OnlineNamesOn = false }; var w = r.Build();
        w.AtCurrentAirport("KTIW", onGround: true);
        Assert.Empty(r.Names);
        Assert.Empty(r.Claimed);          // switched on later, the airport is still owed its fetch
        Assert.Equal(new[] { "KTIW" }, r.Surroundings);   // navdata and scenery tiers need no network
    }

    [Fact]
    public void A_failing_fetch_never_escapes_the_warm_up()
    {
        var w = new AirportWarmUp(_ => true, () => true,
            _ => throw new InvalidOperationException("names"), _ => throw new InvalidOperationException("surroundings"));
        w.AtCurrentAirport("KTIW", onGround: true);
        w.AtDestination("KTIW");
    }
}
