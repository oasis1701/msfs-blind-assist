using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class PassingCalloutGateTests
{
    private static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private static AirportFeature Feat(FeatureKind k, string name, double lat = 33.64, double lon = -84.43)
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Osm };
    private static NearbyFeature[] At(AirportFeature f, double metres, double rel = 90) => new[] { new NearbyFeature(f, metres, rel) };

    /// <summary>Feeds one distance per 2 s tick at 10 kt; returns every callout.</summary>
    private static List<string> Drive(PassingCalloutGate gate, AirportFeature f, DateTime start, params double[] metres)
    {
        var said = new List<string>();
        for (int i = 0; i < metres.Length; i++)
        {
            var hit = gate.Evaluate(At(f, metres[i]), 10, start.AddSeconds(2 * i));
            if (hit != null) said.Add(hit.Feature.SpokenName);
        }
        return said;
    }

    [Fact]
    public void A_building_is_called_once_when_its_range_stops_closing_and_starts_opening()
    {
        var gate = new PassingCalloutGate();
        var said = Drive(gate, Feat(FeatureKind.Concourse, "Concourse B"), T0, 140, 110, 80, 62, 60, 66, 90, 130);
        Assert.Equal(new[] { "Concourse B" }, said);
    }

    [Fact]
    public void Parked_beside_a_terminal_for_any_length_of_time_then_leaving_says_nothing()
    {
        // The old baseline was a 5-minute timestamp: six minutes of preflight and the first movement
        // recited the building the aircraft was parked at.
        var gate = new PassingCalloutGate(); var conc = Feat(FeatureKind.Concourse, "Concourse B");
        for (int i = 0; i < 300; i++) Assert.Null(gate.Evaluate(At(conc, 60), 0, T0.AddSeconds(2 * i)));      // ten minutes stopped
        Assert.Empty(Drive(gate, conc, T0.AddMinutes(10), 60, 61, 70, 95, 140));
    }

    [Fact]
    public void A_pushback_turn_that_swings_the_terminal_abeam_says_nothing()
    {
        var gate = new PassingCalloutGate(); var t1 = Feat(FeatureKind.Terminal, "Terminal 1");
        Assert.Null(gate.Evaluate(At(t1, 40, rel: 5), 0, T0));                  // nose-in: the terminal is dead ahead
        Assert.Null(gate.Evaluate(At(t1, 48, rel: 40), 3, T0.AddSeconds(2)));   // pushed back, turning
        Assert.Null(gate.Evaluate(At(t1, 60, rel: 90), 3, T0.AddSeconds(4)));   // now abeam — the range only ever OPENED
        Assert.Null(gate.Evaluate(At(t1, 90, rel: 120), 8, T0.AddSeconds(6)));
    }

    [Fact]
    public void Passing_too_fast_is_never_called_late()
    {
        var gate = new PassingCalloutGate(); var tower = Feat(FeatureKind.Tower, "Control Tower");
        double[] metres = { 190, 120, 60, 58, 100, 170 };
        for (int i = 0; i < metres.Length; i++) Assert.Null(gate.Evaluate(At(tower, metres[i]), 45, T0.AddSeconds(2 * i)));
        Assert.Null(gate.Evaluate(Array.Empty<NearbyFeature>(), 10, T0.AddSeconds(20)));      // it has left range: dropped, not deferred
    }

    [Fact]
    public void Two_buildings_passed_together_are_spaced_by_the_global_gap_and_the_second_still_speaks()
    {
        var gate = new PassingCalloutGate();
        var fuel = Feat(FeatureKind.Fuel, "Avfuel"); var fbo = Feat(FeatureKind.Fbo, "Narrows Aviation", lat: 33.6405);
        NearbyFeature[] Both(double a, double b) => new[] { new NearbyFeature(fuel, a, -90), new NearbyFeature(fbo, b, 90) };
        Assert.Null(gate.Evaluate(Both(90, 95), 10, T0));
        Assert.Null(gate.Evaluate(Both(50, 55), 10, T0.AddSeconds(2)));
        Assert.Equal("Avfuel", gate.Evaluate(Both(58, 62), 10, T0.AddSeconds(4))?.Feature.Name);         // nearest first
        Assert.Null(gate.Evaluate(Both(70, 75), 10, T0.AddSeconds(6)));                                  // inside the 10 s gap
        Assert.Equal("Narrows Aviation", gate.Evaluate(Both(88, 92), 10, T0.AddSeconds(16))?.Feature.Name);
    }

    [Fact]
    public void A_building_is_not_called_again_inside_five_minutes_but_is_after()
    {
        var gate = new PassingCalloutGate(); var conc = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Single(Drive(gate, conc, T0, 140, 80, 60, 70, 140));
        Assert.Null(gate.Evaluate(Array.Empty<NearbyFeature>(), 10, T0.AddSeconds(60)));                  // out of range: the track expires
        Assert.Empty(Drive(gate, conc, T0.AddMinutes(2), 140, 80, 60, 70, 140));                          // a second lap, two minutes later
        Assert.Single(Drive(gate, conc, T0.AddMinutes(8), 140, 80, 60, 70, 140));
    }

    [Fact]
    public void A_rebuilt_catalog_that_nudges_the_point_does_not_make_a_new_building()
    {
        var gate = new PassingCalloutGate();
        Assert.Single(Drive(gate, Feat(FeatureKind.Concourse, "Concourse B", 33.6400), T0, 140, 80, 60, 70));
        // OSM arrives and the merged centroid moves 40 m: same kind, same name → same track.
        Assert.Null(gate.Evaluate(At(Feat(FeatureKind.Concourse, "Concourse B", 33.64036), 75), 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(At(Feat(FeatureKind.Concourse, "Concourse B", 33.64036), 95), 10, T0.AddSeconds(10)));
    }

    [Fact]
    public void Kinds_nobody_wants_called_out_and_anything_outside_its_radius_are_ignored_and_state_is_pruned()
    {
        var gate = new PassingCalloutGate();
        Assert.Empty(Drive(gate, Feat(FeatureKind.Apron, "GA ramp"), T0, 90, 50, 40, 60, 90));
        Assert.Empty(Drive(gate, Feat(FeatureKind.Hangar, ""), T0, 90, 50, 40, 60, 90));                  // unnamed hangars are not announceable
        Assert.Empty(Drive(gate, Feat(FeatureKind.Fuel, "Avfuel"), T0, 240, 200, 160, 200, 240));         // never inside Fuel's 100 m
        Assert.Equal(0, gate.TrackCount);
        Drive(gate, Feat(FeatureKind.Fuel, "Avfuel"), T0.AddMinutes(1), 90, 80);
        Assert.Equal(1, gate.TrackCount);
        gate.Evaluate(Array.Empty<NearbyFeature>(), 10, T0.AddMinutes(1).AddSeconds(45));
        Assert.Equal(0, gate.TrackCount);
    }
}
