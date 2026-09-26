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
    public void Kinds_nobody_wants_called_out_and_a_closest_point_outside_the_radius_are_never_called_and_state_is_pruned()
    {
        var gate = new PassingCalloutGate();
        Assert.Empty(Drive(gate, Feat(FeatureKind.Apron, "GA ramp"), T0, 90, 50, 40, 60, 90));
        Assert.Empty(Drive(gate, Feat(FeatureKind.Hangar, ""), T0, 90, 50, 40, 60, 90));                  // unnamed hangars are not announceable
        Assert.Equal(0, gate.TrackCount);                                                                  // ...and are never even tracked
        // Tracked from the edge of the rank window, but its closest point (160 m) never comes inside
        // Fuel's 150 m radius: tracked, never called.
        Assert.Empty(Drive(gate, Feat(FeatureKind.Fuel, "Avfuel"), T0, 240, 200, 160, 200, 240));
        Assert.Equal(1, gate.TrackCount);
        // Beyond RankRadiusMetres nothing is tracked at all (the monitor never ranks it there anyway).
        Assert.Empty(Drive(gate, Feat(FeatureKind.Concourse, "Far Pier", lat: 33.70), T0, 420, 400, 380, 400, 420));
        Assert.Equal(1, gate.TrackCount);
        gate.Evaluate(Array.Empty<NearbyFeature>(), 10, T0.AddMinutes(1));                                 // unseen for 52 s: pruned
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
    public void A_name_already_said_on_that_side_is_held_even_for_a_different_building()
    {
        // ⚠ THIS TEST WAS REVERSED (2026-09-22). It asserted that fuelB — a different building
        // sharing fuelA's name — speaks right away, because suppression was per BUILDING. It is
        // now held, and the reason is measurement rather than taste.
        //
        // Surveying this machine's 109 airports with scenery features: 64 of them (59%) carry at
        // least one repeated announceable name, 301 of 1,328 announceable scenery features (23%)
        // duplicate a name already in the catalog, and RJFF has THIRTY features called "Fuk City
        // Hangar", EHAM fifteen "Amsterdam Hangars East", BIKF thirteen "DS Hangar Military".
        // Taxiing such a row announced the identical sentence over and over about buildings the
        // pilot has no way to tell apart.
        //
        // What the gate still does per BUILDING is TRACK — kind, name AND position — which is what
        // stops the false pass a name-only track key produced, and is pinned by the test above.
        // Only the SENTENCE is deduplicated, and the side is part of the sentence: the test above
        // also proves fuelA-on-the-left and fuelB-on-the-right BOTH speak.
        var gate = new PassingCalloutGate();
        var fuelA = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6400);
        var fuelB = Feat(FeatureKind.Fuel, "Fuel", lat: 33.6413);   // same name, ~150 m away, SAME side
        NearbyFeature[] Solo(AirportFeature f, double d) => new[] { new NearbyFeature(f, d, 90) };

        Assert.Null(gate.Evaluate(Solo(fuelA, 100), 10, T0));
        Assert.Null(gate.Evaluate(Solo(fuelA, 60), 10, T0.AddSeconds(2)));
        Assert.NotNull(gate.Evaluate(Solo(fuelA, 70), 10, T0.AddSeconds(4)));                              // fuelA's first pass

        // 2 minutes later: fuelA again is silent (its own 5-minute per-building window)...
        Assert.Null(gate.Evaluate(Solo(fuelA, 100), 10, T0.AddMinutes(2)));
        Assert.Null(gate.Evaluate(Solo(fuelA, 60), 10, T0.AddMinutes(2).AddSeconds(2)));
        Assert.Null(gate.Evaluate(Solo(fuelA, 70), 10, T0.AddMinutes(2).AddSeconds(4)));

        // ...and so is fuelB, which would say the identical sentence on the identical side.
        Assert.Null(gate.Evaluate(Solo(fuelB, 100), 10, T0.AddMinutes(2).AddSeconds(20)));
        Assert.Null(gate.Evaluate(Solo(fuelB, 60), 10, T0.AddMinutes(2).AddSeconds(22)));
        Assert.Null(gate.Evaluate(Solo(fuelB, 70), 10, T0.AddMinutes(2).AddSeconds(24)));

        // 8 minutes after fuelA's own first pass, past BOTH windows, "Fuel" is available again.
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

    // ---- PR #230 review SW-1, controller decision M17: the loop-entry test governs BOTH when
    // tracking of a new feature starts AND whether an already-armed, Pending track keeps being
    // visited (and so can fire) while its feature recedes -- M17 deliberately keeps the second
    // half keyed on the wide rank window, never narrowed back to the kind's own radius. ----

    [Fact]
    public void A_pass_held_by_speed_still_fires_once_the_current_sample_has_receded_past_the_kind_s_own_radius_but_stays_inside_the_rank_window()
    {
        // A pass held by speed (or the global gap) may still be spoken while its building is
        // anywhere inside RankRadiusMetres, even once the CURRENT sample has grown past the
        // kind's own (narrower) PassRadiusMetres -- it still names the side and range frozen at
        // the closest point, never the release sample's. Without this, a track that is simply not
        // re-visited once it recedes past its own radius could never be released at all.
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Hangar, "Receding Hangar");
        double kindRadius = PassingCalloutGate.PassRadiusMetres(FeatureKind.Hangar);
        double rankRadius = PassingCalloutGate.RankRadiusMetres;
        double releaseDist = (kindRadius + rankRadius) / 2.0;   // strictly between the two, whatever their values

        // Driven at taxi speed up to and including the closest point (PC-1 would otherwise consume
        // this as a creep, not release it as "held by speed" -- see the PC-1 tests below); only the
        // opening sample onward is slow, which is what actually holds this pass on MinSpeedKts.
        Assert.Null(gate.Evaluate(At(h, 200), 10, T0));
        Assert.Null(gate.Evaluate(At(h, 100), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(At(h, 60), 10, T0.AddSeconds(4)));            // closest point, at taxi speed: 60 m at 90 degrees -- well inside the 150 m radius
        Assert.Null(gate.Evaluate(At(h, 90), 1.5, T0.AddSeconds(6)));           // opens -- arms, now below MinSpeedKts: held by speed
        Assert.Null(gate.Evaluate(At(h, releaseDist), 1.5, T0.AddSeconds(8)));  // recedes past the radius while still held: must stay tracked, not dropped

        // The premise this test exists to pin: the release sample really is outside the kind's own
        // radius and really is inside the rank window -- a later widening of either constant must
        // not let this test silently stop exercising M17.
        Assert.True(releaseDist > kindRadius);
        Assert.True(releaseDist < rankRadius);

        var hit = gate.Evaluate(At(h, releaseDist), 5, T0.AddSeconds(10));      // speed returns into band: releases
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);        // still the closest-point values, never the release sample's
        Assert.Equal(90, hit.RelativeBearingDeg);
    }

    [Fact]
    public void A_held_pass_whose_building_leaves_the_rank_window_before_release_is_never_spoken()
    {
        // The complementary boundary: the rank window is not unlimited. Once the current sample is
        // beyond RankRadiusMetres the track is not even visited that tick, so a pass held by speed
        // cannot be released while its building has gone that far, however recently it was armed.
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Hangar, "Departed Hangar");
        double rankRadius = PassingCalloutGate.RankRadiusMetres;
        double beyondRank = rankRadius + 50.0;

        // As above: taxi speed through the closest point, so the eventual silence below is really
        // the rank-window boundary this test exists to pin -- not PC-1 consuming a creep.
        Assert.Null(gate.Evaluate(At(h, 200), 10, T0));
        Assert.Null(gate.Evaluate(At(h, 100), 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(At(h, 60), 10, T0.AddSeconds(4)));             // closest point, at taxi speed
        Assert.Null(gate.Evaluate(At(h, 90), 1.5, T0.AddSeconds(6)));            // opens -- arms, held by speed

        Assert.True(beyondRank > rankRadius);
        Assert.Null(gate.Evaluate(At(h, beyondRank), 5, T0.AddSeconds(8)));      // speed is back in band, but the building has left the rank window
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
        // The closest point is driven THROUGH at taxi speed; only the opening sample is slow (the
        // aircraft braked just past the building). Held below MinSpeedKts, said once it moves on.
        // A closest point reached while stopped or creeping is a different case, and silent — see
        // the PC-1 tests below.
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, 90) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 90) }, 10, T0.AddSeconds(4)));    // closest point, at taxi speed
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 75, 90) }, 1.5, T0.AddSeconds(6)));   // opens (arms) at 1.5 kt -- below MinSpeedKts: held, not fired
        var hit = gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 5, T0.AddSeconds(8));         // speeds up to 5 kt, still in range: fires
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

    // ---- Fix round 2: a pass is frozen the moment it arms -- Min/MinRel cannot drift afterward ----

    [Fact]
    public void A_pass_is_frozen_the_moment_it_arms_so_a_deeper_sample_while_held_by_the_gap_does_not_drift_the_announced_side()
    {
        // Worked example from the review: arms at 60 m / +95 (abeam right), held by the global gap
        // (started by a first building's fire); a later, deeper, off-side sample must not overwrite
        // the pair IsAbeam already judged -- the eventual release must still say "right", not
        // whatever the drifted reading would have said.
        var gate = new PassingCalloutGate();
        var first = Feat(FeatureKind.Concourse, "First Building");
        var second = Feat(FeatureKind.Concourse, "Second Building");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 140, 90), new NearbyFeature(second, 140, 95) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 85, 90), new NearbyFeature(second, 110, 95) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 60, 90), new NearbyFeature(second, 90, 95) }, 10, T0.AddSeconds(4)));
        Assert.NotNull(gate.Evaluate(new[] { new NearbyFeature(first, 70, 90), new NearbyFeature(second, 60, 95) }, 10, T0.AddSeconds(6)));   // first fires (starts the gap); second's own minimum so far: 60 m at +95
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 70, 95) }, 10, T0.AddSeconds(8)));     // second opens/arms at +95 (abeam right) -- held by the gap
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 50, -30) }, 10, T0.AddSeconds(10)));   // a DEEPER, off-side sample while pending: must NOT overwrite the armed pair
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 55, -30) }, 10, T0.AddSeconds(12)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 65, -30) }, 10, T0.AddSeconds(14)));
        var hit = gate.Evaluate(new[] { new NearbyFeature(second, 75, -30) }, 10, T0.AddSeconds(16));      // gap clears
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);
        Assert.Equal(95, hit.RelativeBearingDeg);
    }

    [Fact]
    public void The_mirror_case_on_the_left_also_stays_frozen_at_the_armed_pair()
    {
        var gate = new PassingCalloutGate();
        var first = Feat(FeatureKind.Concourse, "First Building");
        var second = Feat(FeatureKind.Concourse, "Second Building");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 140, 90), new NearbyFeature(second, 140, -95) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 85, 90), new NearbyFeature(second, 110, -95) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(first, 60, 90), new NearbyFeature(second, 90, -95) }, 10, T0.AddSeconds(4)));
        Assert.NotNull(gate.Evaluate(new[] { new NearbyFeature(first, 70, 90), new NearbyFeature(second, 60, -95) }, 10, T0.AddSeconds(6)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 70, -95) }, 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 50, 30) }, 10, T0.AddSeconds(10)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 55, 30) }, 10, T0.AddSeconds(12)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(second, 65, 30) }, 10, T0.AddSeconds(14)));
        var hit = gate.Evaluate(new[] { new NearbyFeature(second, 75, 30) }, 10, T0.AddSeconds(16));
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);
        Assert.Equal(-95, hit.RelativeBearingDeg);
    }

    [Fact]
    public void A_pass_held_back_by_excess_speed_also_stays_frozen_at_the_armed_pair()
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Fast Pass Building");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, 100) }, 45, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 100) }, 45, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 100) }, 45, T0.AddSeconds(4)));    // minimum so far: 60 m at 100 degrees
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 70, 100) }, 45, T0.AddSeconds(6)));     // opens/arms at 100 (abeam) -- held by excess speed (45 kt > MaxSpeedKts)
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 50, -60) }, 45, T0.AddSeconds(8)));    // a DEEPER, off-side sample while still fast: must NOT overwrite
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 55, -60) }, 45, T0.AddSeconds(10)));
        var hit = gate.Evaluate(new[] { new NearbyFeature(h, 65, -60) }, 10, T0.AddSeconds(12));       // slows into the band, building still in range
        Assert.NotNull(hit);
        Assert.Equal(60, hit!.DistanceMetres);
        Assert.Equal(100, hit.RelativeBearingDeg);
    }

    [Fact]
    public void A_non_abeam_minimum_consumed_silently_is_not_resurrected_by_a_later_deeper_abeam_sample()
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 140, 20) }, 10, T0));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 20) }, 10, T0.AddSeconds(2)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 20) }, 10, T0.AddSeconds(4)));     // minimum so far: 60 m at 20 degrees -- not abeam
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 70, 20) }, 10, T0.AddSeconds(6)));      // opens: consumed silently (20 degrees is not abeam)
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 50, 90) }, 10, T0.AddSeconds(8)));     // a DEEPER, ABEAM sample on the SAME (already-consumed) track
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 60, 90) }, 10, T0.AddSeconds(10)));
        Assert.Null(gate.Evaluate(new[] { new NearbyFeature(h, 90, 90) }, 10, T0.AddSeconds(12)));     // still nothing -- the track stays consumed
    }

    // ---- Final review: a catalog swap re-baselines the tracks, and a pending pass expires ----

    [Fact]
    public void A_catalog_swap_forgets_the_approaches_in_progress()
    {
        // A late OSM answer or a GSX publish replaces the catalog INSTANCE, and a feature's geometry
        // BASIS can change with it (a stand cluster becomes a building outline), so the very next
        // range for a same-identity track is a STEP, not a step along an approach: a premature
        // "Passing X" one way, a lost one the other. The approaches so far are what must go.
        var gate = new PassingCalloutGate();
        var conc = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(At(conc, 140), 10, T0));
        Assert.Null(gate.Evaluate(At(conc, 60), 10, T0.AddSeconds(2)));    // closing recorded
        gate.RebaselineTracks();
        Assert.Equal(0, gate.TrackCount);
        Assert.Null(gate.Evaluate(At(conc, 70), 10, T0.AddSeconds(4)));    // would have "opened" the old minimum
        Assert.Null(gate.Evaluate(At(conc, 140), 10, T0.AddSeconds(6)));
    }

    [Theory]
    [InlineData(false, 0)]     // re-baselined: the building is still inside its own five-minute window
    [InlineData(true, 1)]      // a full Reset forgets that too, and says it again
    public void Only_a_full_Reset_lets_a_just_announced_building_be_announced_again(bool fullReset, int expected)
    {
        var gate = new PassingCalloutGate();
        var conc = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Single(Drive(gate, conc, T0, 140, 80, 60, 70));
        if (fullReset) gate.Reset(); else gate.RebaselineTracks();
        Assert.Equal(expected, Drive(gate, conc, T0.AddSeconds(30), 140, 80, 60, 70, 140).Count);
    }

    [Fact]
    public void A_re_baseline_keeps_the_global_gap_so_the_next_building_is_not_stacked_on_the_last_callout()
    {
        var gate = new PassingCalloutGate();
        var a = Feat(FeatureKind.Concourse, "Concourse A", 33.6400);
        var b = Feat(FeatureKind.Fuel, "Avfuel", 33.6413);
        Assert.Null(gate.Evaluate(At(a, 140), 10, T0));
        Assert.Null(gate.Evaluate(At(a, 60), 10, T0.AddSeconds(2)));
        Assert.NotNull(gate.Evaluate(At(a, 70), 10, T0.AddSeconds(4)));    // fires: the global gap starts here
        gate.RebaselineTracks();
        Assert.Null(gate.Evaluate(At(b, 90), 10, T0.AddSeconds(6)));
        Assert.Null(gate.Evaluate(At(b, 60), 10, T0.AddSeconds(8)));
        Assert.Null(gate.Evaluate(At(b, 70), 10, T0.AddSeconds(10)));      // arms, but still inside the 10 s gap
        Assert.NotNull(gate.Evaluate(At(b, 80), 10, T0.AddSeconds(16)));
    }

    /// <summary>Arms a pass at T0+4 while stopped, holds the aircraft beside the building (so the
    /// track is fed and never simply expires), then moves off `releaseAt` seconds after the arm.</summary>
    private static NearbyFeature? PassHeldFor(TimeSpan afterArming)
    {
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        Assert.Null(gate.Evaluate(At(h, 140), 10, T0));
        Assert.Null(gate.Evaluate(At(h, 60), 10, T0.AddSeconds(2)));
        var armed = T0.AddSeconds(4);
        Assert.Null(gate.Evaluate(At(h, 70), 0.5, armed));                 // opens and arms — but below MinSpeedKts nothing may fire
        var release = armed + afterArming;
        for (var t = armed.AddSeconds(2); t < release; t = t.AddSeconds(2))
            Assert.Null(gate.Evaluate(At(h, 70), 0.5, t));                 // stopped beside it, still in range
        return gate.Evaluate(At(h, 80), 10, release);                      // taxis on
    }

    [Fact]
    public void A_pass_held_until_the_building_is_no_longer_beside_the_aircraft_is_dropped_never_said_late()
    {
        // Pass a building, stop within its radius for three minutes — below MinSpeedKts nothing may
        // fire — then move off. "Passing Concourse B, on the left." three minutes after the fact
        // describes somewhere the aircraft no longer is.
        Assert.Null(PassHeldFor(TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void The_pending_expiry_boundary_is_exact()
    {
        // Comfortably longer than the 10 s global gap plus a late tick, so an ordinary deferred pass
        // still speaks; short enough that what it describes is still beside the aircraft.
        Assert.NotNull(PassHeldFor(PassingCalloutGate.PendingExpiry - TimeSpan.FromSeconds(1)));
        Assert.Null(PassHeldFor(PassingCalloutGate.PendingExpiry + TimeSpan.FromSeconds(1)));
    }

    // ---- PC-1: a closest point reached while STOPPED, or at zero range, is not a pass ----

    /// <summary>Feeds one (range, relative bearing, ground speed) sample per 2 s tick — the
    /// monitor's own poll — and returns every callout.</summary>
    private static List<NearbyFeature> DriveSamples(PassingCalloutGate gate, AirportFeature f, DateTime start,
        params (double Metres, double Rel, double Kts)[] samples)
    {
        var said = new List<NearbyFeature>();
        for (int i = 0; i < samples.Length; i++)
        {
            var hit = gate.Evaluate(At(f, samples[i].Metres, samples[i].Rel), samples[i].Kts, start.AddSeconds(2 * i));
            if (hit != null) said.Add(hit);
        }
        return said;
    }

    [Fact]
    public void Taxiing_onto_a_fuel_stand_refuelling_and_leaving_is_never_called_a_pass()
    {
        // A navdata "Fuel" IS its stands and SurroundingsGeometry.Nearest measures to the nearest
        // one, so taxiing onto a fuel stand closes the range to a couple of metres and stops there.
        // The bearing to a point 2 m away is noise — here it happens to read abeam left — and leaving
        // then "opened" the range: "Passing Fuel, on the left." about the stand just refuelled on.
        var gate = new PassingCalloutGate(); var fuel = Feat(FeatureKind.Fuel, "Fuel");
        var samples = new List<(double, double, double)> { (140, 15, 10), (110, 15, 10), (80, 15, 10), (50, 20, 10), (25, 25, 8), (8, 40, 4) };
        for (int i = 0; i < 150; i++) samples.Add((2, -80, 0));                                  // five minutes on the stand
        samples.AddRange(new[] { (6.0, -120.0, 3.0), (15.0, -150.0, 8.0), (30.0, -160.0, 10.0), (50.0, -165.0, 10.0), (80.0, -170.0, 10.0) });
        Assert.Empty(DriveSamples(gate, fuel, T0, samples.ToArray()));
    }

    [Fact]
    public void A_closest_point_reached_while_stopped_is_consumed_silently_even_well_clear_of_the_feature()
    {
        // Held 20 m abeam of a fuel stand — well outside ZeroRangeMetres, a genuinely abeam bearing —
        // then taxiing on. The aircraft STOPPED at its closest point instead of driving through it,
        // and a stop is not a pass.
        var gate = new PassingCalloutGate(); var fuel = Feat(FeatureKind.Fuel, "Fuel");
        var samples = new List<(double, double, double)> { (140, 40, 10), (100, 50, 10), (60, 60, 10), (30, 75, 6), (20, 90, 0) };
        for (int i = 0; i < 10; i++) samples.Add((20, 90, 0));
        samples.AddRange(new[] { (22.0, 100.0, 4.0), (30.0, 120.0, 8.0), (45.0, 140.0, 10.0), (60.0, 150.0, 10.0) });
        Assert.Empty(DriveSamples(gate, fuel, T0, samples.ToArray()));
    }

    [Fact]
    public void A_stop_well_before_the_closest_point_does_not_count_once_the_aircraft_drives_through_it()
    {
        // Held 40 m from a fuel stand, then cleared on and driven PAST it at 25 m: the stop was 15 m
        // further out than the closest point (more than OpeningMetres), so the pass stands.
        var gate = new PassingCalloutGate(); var fuel = Feat(FeatureKind.Fuel, "Avfuel");
        var samples = new List<(double, double, double)> { (140, 20, 10), (90, 30, 10), (40, 45, 0) };
        for (int i = 0; i < 10; i++) samples.Add((40, 45, 0));
        samples.AddRange(new[] { (30.0, 70.0, 6.0), (25.0, 90.0, 10.0), (32.0, 115.0, 10.0), (45.0, 130.0, 10.0) });
        var said = DriveSamples(gate, fuel, T0, samples.ToArray());
        Assert.Single(said);
        Assert.Equal(25, said[0].DistanceMetres);
        Assert.Equal(90, said[0].RelativeBearingDeg);
    }

    // Literal metres, not derived from SurroundingsReport.ZeroRangeMetres (3.81 m) — the same reason
    // the abeam boundary test above uses literal degrees: derived offsets would follow a change to
    // the constant's VALUE and pin nothing.
    [Theory]
    [InlineData(3.8, false)]    // at or inside ZeroRangeMetres: a degenerate bearing, never a side
    [InlineData(3.9, true)]     // just outside it: an ordinary pass
    public void A_closest_point_at_zero_range_is_never_a_pass_even_at_taxi_speed(double minMetres, bool called)
    {
        // Rolling straight over a stand point at 10 kt, never stopping: the range closes to a few
        // metres and opens again. Inside ZeroRangeMetres the bearing to that point is degenerate —
        // here it reads abeam left by accident — and the Surroundings readout already refuses to put
        // a side on it ("Fuel, here."); the callout must not put one on it either.
        var gate = new PassingCalloutGate(); var fuel = Feat(FeatureKind.Fuel, "Fuel");
        var said = DriveSamples(gate, fuel, T0,
            (60, 10, 10), (45, 10, 10), (30, 12, 10), (15, 20, 10), (minMetres, -85, 10), (12, -160, 10), (27, -170, 10), (42, -175, 10));
        Assert.Equal(called ? 1 : 0, said.Count);
    }

    [Fact]
    public void A_closest_point_crept_through_below_minimum_speed_is_consumed_silently()
    {
        // The data the MinSpeedKts test above used to carry: every sample, the closest point included,
        // at 1.5 kt. Below MinSpeedKts AT the closest point the aircraft was stopped there as far as
        // the gate is concerned, and a pass is only ever one driven through.
        var gate = new PassingCalloutGate();
        var h = Feat(FeatureKind.Concourse, "Concourse B");
        var said = DriveSamples(gate, h, T0, (140, 90, 1.5), (90, 90, 1.5), (60, 90, 1.5), (75, 90, 1.5), (90, 90, 5), (120, 90, 10));
        Assert.Empty(said);
    }

    [Fact]
    public void An_ordinary_abeam_pass_driven_through_at_taxi_speed_is_still_called()
    {
        var gate = new PassingCalloutGate(); var fuel = Feat(FeatureKind.Fuel, "Fuel");
        var said = DriveSamples(gate, fuel, T0, (140, 60, 10), (100, 70, 10), (60, 85, 10), (40, 90, 10), (45, 100, 10), (60, 120, 10));
        Assert.Single(said);
        Assert.Equal(40, said[0].DistanceMetres);
        Assert.Equal(90, said[0].RelativeBearingDeg);
    }

    // ---- A3-x: a hangar the scenery named only by its kind word is not announceable ----

    [Fact]
    public void A_hangar_the_scenery_named_only_by_its_kind_word_is_not_announced()
    {
        // SceneryModelNameClassifier labels a model called just "Hangar" (KTIW_Hangar) with the kind
        // word itself, marked NameIsGeneric. HasName is true for it, but "Passing Hangar, on the
        // left" names nothing a pilot can look for — which is exactly why an UNNAMED hangar is not
        // announceable. Built from the real classifier output, so a change to how the scenery tier
        // labels a bare hangar is caught here too.
        var c = MSFSBlindAssist.Services.SceneryIndex.SceneryModelNameClassifier.Classify("KTIW_Hangar", "KTIW")!;
        var hangar = new AirportFeature { Kind = c.Kind, Name = c.Name, NameIsGeneric = c.NameIsGeneric, Lat = 33.64, Lon = -84.43, Source = FeatureSource.Scenery };
        Assert.Equal(FeatureKind.Hangar, hangar.Kind);
        Assert.True(hangar.HasName);                                     // what IsAnnounceable used to test
        Assert.False(PassingCalloutGate.IsAnnounceable(hangar));

        var gate = new PassingCalloutGate();
        Assert.Empty(Drive(gate, hangar, T0, 140, 110, 80, 62, 60, 66, 90, 130));
        Assert.Equal(0, gate.TrackCount);
    }

    [Fact]
    public void A_hangar_with_a_proper_name_is_still_announced()
    {
        var gate = new PassingCalloutGate();
        Assert.Equal(new[] { "Titan Airways Hangar" }, Drive(gate, Feat(FeatureKind.Hangar, "Titan Airways Hangar"), T0, 140, 110, 80, 62, 60, 66, 90, 130));
    }
}
