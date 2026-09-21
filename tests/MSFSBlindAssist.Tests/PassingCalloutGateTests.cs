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
        // OSM arrives and the merged centroid moves about 5 m (a real rebuild nudge, per SameFeatureMetres's
        // own doc comment — "metres, not tens of metres"): same kind, same name, same place -> same track.
        // (A 40 m nudge, as this test used before the position-aware identity fix, would sit right on
        // SameFeatureMetres's own 40 m boundary — a flaky assertion, not a meaningful one.)
        Assert.Null(gate.Evaluate(At(Feat(FeatureKind.Concourse, "Concourse B", 33.640045), 75), 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(At(Feat(FeatureKind.Concourse, "Concourse B", 33.640045), 95), 10, T0.AddSeconds(10)));
        Assert.Equal(1, gate.TrackCount);   // one callout, not zero, not two -- it really is the same track
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

    // ---- Plan-owner amendment: track identity is KEY + POSITION, and only an ABEAM closest point fires ----

    [Fact]
    public void Two_same_named_buildings_150_metres_apart_are_tracked_and_announced_separately()
    {
        // Regression for the false positive a name-only key produced: two same-named "Fuel" features
        // both inside range at once shared ONE track, so the nearer one's minimum let the farther
        // one's still-closing range instantly read as "opening" -- a spurious, early false callout.
        var gate = new PassingCalloutGate();
        var fuelA = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6400);
        var fuelB = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6413);   // ~150 m north of fuelA, same name
        double[] a = { 100, 85, 70, 60, 58, 70, 90, 95, 98, 100, 100 };
        double[] b = { 95, 90, 85, 75, 65, 55, 60, 65, 70, 75, 80 };
        var hits = new List<NearbyFeature>();
        for (int i = 0; i < a.Length; i++)
        {
            var hit = gate.Evaluate(new[] { new NearbyFeature(fuelA, a[i], -90), new NearbyFeature(fuelB, b[i], 90) }, 10, T0.AddSeconds(2 * i));
            if (hit != null) hits.Add(hit);
        }
        Assert.Equal(2, hits.Count);
        Assert.Same(fuelA, hits[0].Feature);          // fuelA's own closest point (reached first, at i=4/5)
        Assert.Equal(-90, hits[0].RelativeBearingDeg);
        Assert.Same(fuelB, hits[1].Feature);          // fuelB's own closest point, deferred past the global gap
        Assert.Equal(90, hits[1].RelativeBearingDeg);
    }

    [Fact]
    public void Repeat_suppression_is_per_building_not_per_a_name_shared_by_a_different_one()
    {
        var gate = new PassingCalloutGate();
        var fuelA = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6400);
        var fuelB = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6413);   // same name, ~150 m away: a different building
        NearbyFeature[] Solo(AirportFeature f, double d) => new[] { new NearbyFeature(f, d, 90) };

        Assert.Null(gate.Evaluate(Solo(fuelA, 100), 10, T0));
        Assert.Null(gate.Evaluate(Solo(fuelA, 60), 10, T0.AddSeconds(2)));
        Assert.NotNull(gate.Evaluate(Solo(fuelA, 70), 10, T0.AddSeconds(4)));                              // fuelA's first pass

        // 2 minutes later: fuelA again is silent (its own 5-minute repeat window)...
        Assert.Null(gate.Evaluate(Solo(fuelA, 100), 10, T0.AddMinutes(2)));
        Assert.Null(gate.Evaluate(Solo(fuelA, 60), 10, T0.AddMinutes(2).AddSeconds(2)));
        Assert.Null(gate.Evaluate(Solo(fuelA, 70), 10, T0.AddMinutes(2).AddSeconds(4)));

        // ...but fuelB, a DIFFERENT building that merely shares the name, speaks right away.
        Assert.Null(gate.Evaluate(Solo(fuelB, 100), 10, T0.AddMinutes(2).AddSeconds(20)));
        Assert.Null(gate.Evaluate(Solo(fuelB, 60), 10, T0.AddMinutes(2).AddSeconds(22)));
        Assert.NotNull(gate.Evaluate(Solo(fuelB, 70), 10, T0.AddMinutes(2).AddSeconds(24)));

        // 8 minutes after fuelA's own first pass, fuelA speaks again.
        Assert.Null(gate.Evaluate(Solo(fuelA, 100), 10, T0.AddMinutes(8)));
        Assert.Null(gate.Evaluate(Solo(fuelA, 60), 10, T0.AddMinutes(8).AddSeconds(2)));
        Assert.NotNull(gate.Evaluate(Solo(fuelA, 70), 10, T0.AddMinutes(8).AddSeconds(4)));
    }

    [Fact]
    public void A_building_approached_tail_first_and_left_by_taxiing_forward_is_never_called()
    {
        // A pushback straight toward a building behind the stand closes the range tail-first;
        // taxiing forward afterward "opens" it -- that is not a pass and must never be announced.
        var gate = new PassingCalloutGate(); var h = Feat(FeatureKind.Hangar, "Old Hangar");
        NearbyFeature[] Behind(double d) => new[] { new NearbyFeature(h, d, 180) };
        Assert.Null(gate.Evaluate(Behind(150), 10, T0));
        Assert.Null(gate.Evaluate(Behind(100), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(Behind(60), 10, T0.AddSeconds(4)));     // closest point, still dead astern
        Assert.Null(gate.Evaluate(Behind(80), 10, T0.AddSeconds(6)));     // opens -- consumed silently
        Assert.Null(gate.Evaluate(Behind(120), 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(Behind(150), 10, T0.AddSeconds(10)));
        Assert.Equal(1, gate.TrackCount);                                 // tracked, just never announced
    }

    [Fact]
    public void A_building_dead_ahead_that_the_aircraft_turns_away_from_before_reaching_it_is_never_called()
    {
        var gate = new PassingCalloutGate(); var h = Feat(FeatureKind.Concourse, "Concourse B");
        NearbyFeature[] Ahead(double d) => new[] { new NearbyFeature(h, d, -20) };   // nowhere near abeam at any point
        Assert.Null(gate.Evaluate(Ahead(140), 10, T0));
        Assert.Null(gate.Evaluate(Ahead(100), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(Ahead(60), 10, T0.AddSeconds(4)));      // closest point: still only 20 degrees off the nose
        Assert.Null(gate.Evaluate(Ahead(80), 10, T0.AddSeconds(6)));      // opens -- consumed silently
        Assert.Null(gate.Evaluate(Ahead(140), 10, T0.AddSeconds(8)));
    }

    [Fact]
    public void An_ordinary_pass_uses_the_bearing_at_the_closest_point_not_at_the_tick_that_detects_it()
    {
        // At 40 kt and a 2 s poll the detection tick can land 40+ m past the true closest point,
        // where a genuinely abeam building already reads well outside the abeam window.
        var gate = new PassingCalloutGate();
        var right = Feat(FeatureKind.Concourse, "Concourse C");
        var left = Feat(FeatureKind.Concourse, "Concourse D", lat: 33.6410);

        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(right, 140, 60) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(right, 100, 80) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(right, 60, 100) }, 10, T0.AddSeconds(4)));    // closest point: abeam at 100 degrees
        var hitRight = gate.Evaluate(new[] { new NearbyFeature(right, 75, 150) }, 10, T0.AddSeconds(6));  // detection tick reads 150 -- outside the window
        Assert.Equal("Concourse C", hitRight?.Feature.SpokenName);

        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(left, 140, -60) }, 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(left, 100, -80) }, 10, T0.AddSeconds(10)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(left, 60, -100) }, 10, T0.AddSeconds(12)));   // closest point: abeam at -100 degrees
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(left, 75, -150) }, 10, T0.AddSeconds(14)));   // opens, but still inside the global gap
        var hitLeft = gate.Evaluate(new[] { new NearbyFeature(left, 90, -150) }, 10, T0.AddSeconds(16));  // gap has cleared
        Assert.Equal("Concourse D", hitLeft?.Feature.SpokenName);
    }
}
