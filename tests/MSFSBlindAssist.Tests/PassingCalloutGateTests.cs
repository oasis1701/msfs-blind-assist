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

    // ---- Fix round 1: the returned record describes the pass, not the release tick ----

    [Fact]
    public void A_pass_held_back_by_the_global_gap_names_the_side_it_was_on_at_the_closest_point_not_the_side_at_release()
    {
        // A pass can fire up to ~10-25 s after its true closest point once something else's fire
        // has started the global gap clock; by release the bearing can have swung through a turn.
        // The announced side and range must be the ones AT THE CLOSEST POINT, never release's.
        var gate = new PassingCalloutGate();
        var first = Feat(FeatureKind.Concourse, "First Building");
        var second = Feat(FeatureKind.Concourse, "Second Building");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 140, 90), new NearbyFeature(second, 140, 95) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 85, 90), new NearbyFeature(second, 110, 95) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 60, 90), new NearbyFeature(second, 90, 95) }, 10, T0.AddSeconds(4)));
        Assert.NotNull(gate.Evaluate(new[] { new NearbyFeature(first, 70, 90), new NearbyFeature(second, 70, 95) }, 10, T0.AddSeconds(6)));   // first fires, starting the global gap clock
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 55, 95) }, 10, T0.AddSeconds(8)));     // second's own closest point: 55 m at +95 degrees
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 58, 95) }, 10, T0.AddSeconds(10)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 65, 95) }, 10, T0.AddSeconds(12)));    // opens -- arms, but still inside the 10 s gap
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 80, -150) }, 10, T0.AddSeconds(14)));  // turning; still inside the gap
        var hit = gate.Evaluate(new[] { new NearbyFeature(second, 90, -150) }, 10, T0.AddSeconds(16));     // gap clears; current bearing now reads -150
        Assert.NotNull(hit);
        Assert.Equal(55, hit!.DistanceMetres);
        Assert.Equal(95, hit.RelativeBearingDeg);
    }

    [Fact]
    public void The_mirror_case_on_the_left_also_names_the_closest_point_side()
    {
        var gate = new PassingCalloutGate();
        var first = Feat(FeatureKind.Concourse, "First Building");
        var second = Feat(FeatureKind.Concourse, "Second Building");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 140, 90), new NearbyFeature(second, 140, -95) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 85, 90), new NearbyFeature(second, 110, -95) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 60, 90), new NearbyFeature(second, 90, -95) }, 10, T0.AddSeconds(4)));
        Assert.NotNull(gate.Evaluate(new[] { new NearbyFeature(first, 70, 90), new NearbyFeature(second, 70, -95) }, 10, T0.AddSeconds(6)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 55, -95) }, 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 58, -95) }, 10, T0.AddSeconds(10)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 65, -95) }, 10, T0.AddSeconds(12)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 80, 150) }, 10, T0.AddSeconds(14)));
        var hit = gate.Evaluate(new[] { new NearbyFeature(second, 90, 150) }, 10, T0.AddSeconds(16));
        Assert.NotNull(hit);
        Assert.Equal(55, hit!.DistanceMetres);
        Assert.Equal(-95, hit.RelativeBearingDeg);
    }

    [Fact]
    public void An_undelayed_pass_also_returns_the_minimum_sample_not_the_detection_tick()
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, 90) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 100) }, 10, T0.AddSeconds(4)));    // closest point: 60 m at 100 degrees
        var hit = gate.Evaluate(new[] { new NearbyFeature(h, 75, 130) }, 10, T0.AddSeconds(6));        // detection tick: 75 m at 130 degrees
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);
        Assert.Equal(100, hit.RelativeBearingDeg);
    }

    // ---- Fix round 1: MinSpeedKts pinned (a pass can also be held back by being too slow) ----

    [Fact]
    public void A_pass_that_arms_below_minimum_speed_stays_pending_and_fires_once_speed_comes_back_into_band()
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, 90) }, 1.5, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 1.5, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 90) }, 1.5, T0.AddSeconds(4)));    // closest point, at 1.5 kt -- below MinSpeedKts
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 75, 90) }, 1.5, T0.AddSeconds(6)));    // opens (arms), but still under 2 kt: held, not fired
        var hit = gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 5, T0.AddSeconds(8));          // speeds up to 5 kt, still in range: fires
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);   // still the closest-point values, however late it fires
        Assert.Equal(90, hit.RelativeBearingDeg);
    }

    // ---- Fix round 1: SameFeatureMetres boundary, offsets computed from the constant itself ----

    /// <summary>The point exactly `metres` due north of `baseLat`, computed the same way
    /// TaxiGeo.HaversineMeters treats a pure north-south offset (a meridian is a great circle, so
    /// the relationship is exact, not an approximation) -- so a boundary test's offset can never
    /// accidentally straddle the real constant the way a hand-picked degree delta once did.</summary>
    private static double NorthOf(double baseLat, double metres)
    {
        const double R = 6371000.0;
        return baseLat + (metres / R) * (180.0 / Math.PI);
    }

    [Fact]
    public void A_catalog_rebuild_that_moves_the_feature_just_beyond_SameFeatureMetres_orphans_the_pass_and_stays_silent()
    {
        var gate = new PassingCalloutGate();
        const double baseLat = 33.6400;
        var original = Feat(FeatureKind.Concourse, "Concourse B", baseLat);
        Assert.Null(gate.Evaluate(At(original, 140), 10, T0));
        Assert.Null(gate.Evaluate(At(original, 80), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(At(original, 60), 10, T0.AddSeconds(4)));   // closest point recorded on the ORIGINAL track
        // a rebuild moves the feature just beyond SameFeatureMetres: the original track is
        // orphaned (it never sees the rest of the approach) and the new one starts fresh, with no
        // prior closing recorded -- silence, never a second or false callout.
        double outsideLat = NorthOf(baseLat, PassingCalloutGate.SameFeatureMetres + 0.1);
        var moved = Feat(FeatureKind.Concourse, "Concourse B", outsideLat);
        Assert.Null(gate.Evaluate(At(moved, 70), 10, T0.AddSeconds(6)));      // would "open" the ORIGINAL track's minimum, but the original never sees this reading
        Assert.Null(gate.Evaluate(At(moved, 90), 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(At(moved, 130), 10, T0.AddSeconds(10)));
        Assert.Equal(2, gate.TrackCount);   // the orphaned original AND the fresh one, both silent -- not merged, not double-counted
    }

    [Fact]
    public void A_catalog_rebuild_that_moves_the_feature_just_within_SameFeatureMetres_keeps_the_same_track()
    {
        var gate = new PassingCalloutGate();
        const double baseLat = 33.6400;
        var original = Feat(FeatureKind.Concourse, "Concourse B", baseLat);
        Assert.Null(gate.Evaluate(At(original, 140), 10, T0));
        Assert.Null(gate.Evaluate(At(original, 80), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(At(original, 60), 10, T0.AddSeconds(4)));
        double insideLat = NorthOf(baseLat, PassingCalloutGate.SameFeatureMetres - 0.1);
        var moved = Feat(FeatureKind.Concourse, "Concourse B", insideLat);
        var hit = gate.Evaluate(At(moved, 70), 10, T0.AddSeconds(6));   // same track continues -- opens, fires
        Assert.NotNull(hit);
        Assert.Equal(1, gate.TrackCount);
    }

    // ---- Fix round 1: AbeamMinDeg/AbeamMaxDeg boundary, offsets computed from the constants ----

    private static bool FiresAtMinBearing(double minRel)
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Boundary Test");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, minRel) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, minRel) }, 10, T0.AddSeconds(2)));   // closest point, at the bearing under test
        var hit = gate.Evaluate(new[] { new NearbyFeature(h, 70, minRel) }, 10, T0.AddSeconds(4));      // opens
        return hit != null;
    }

    // Degrees need no unit conversion (unlike SameFeatureMetres's metres-to-lat/lon case above), so
    // these are literal numbers, not derived from AbeamMinDeg/AbeamMaxDeg -- deriving them would
    // make the test self-referential (it would still pass after a change to either constant's
    // VALUE, since the offsets would silently follow it) and defeat the point of pinning 45/135.
    [Theory]
    [InlineData(44.0, false)]      // just outside the lower bound
    [InlineData(46.0, true)]       // just inside
    [InlineData(134.0, true)]      // just inside the upper bound
    [InlineData(136.0, false)]     // just outside
    [InlineData(-44.0, false)]     // mirrored on the left
    [InlineData(-46.0, true)]      // mirrored on the left
    public void The_abeam_window_boundary_is_exact_on_both_sides(double minRel, bool shouldFire)
    {
        Assert.Equal(shouldFire, FiresAtMinBearing(minRel));
    }
}
