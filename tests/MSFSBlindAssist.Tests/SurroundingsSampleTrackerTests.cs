using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The monitor's per-sample rules, driven the way production drives them: one sample per
/// <see cref="AirportSurroundingsMonitor.PollMs"/>, so the distance between two samples follows
/// from the ground speed (15 kt is 15.4 m a poll, 10 kt 10.3 m). Positions step due north along a
/// meridian at LOWI — where the three measured surface values were confirmed — so every step is
/// exactly the distance intended on TaxiGeo's own sphere. PR #230 review items SC-2, SC-3, SC-4.
/// </summary>
public class SurroundingsSampleTrackerTests
{
    // The AircraftPosition struct delivers the surface fields as doubles; so do these.
    private const double Concrete = 0, Grass = 1, Asphalt = 4;
    private const double LowiLat = 47.2602, LowiLon = 11.3439;
    // TaxiGeo's sphere (R = 6,371,000 m): a pure-meridian step is exactly R x delta-latitude.
    private const double MetresPerDegreeLat = 6_371_000.0 * Math.PI / 180.0;

    private double _lat = LowiLat;   // xUnit builds a fresh instance per test

    /// <summary>Metres covered in ONE monitor poll at this ground speed.</summary>
    private static double PerPoll(double kts) => kts * 1852.0 / 3600.0 * (AirportSurroundingsMonitor.PollMs / 1000.0);

    /// <summary>Move due north by exactly <paramref name="metres"/> and take one sample there.</summary>
    private SurroundingsSample MoveAndSample(SurroundingsSampleTracker t, double metres, double surface,
                                             double kts = 15.0, double valid = 1.0)
    {
        _lat += metres / MetresPerDegreeLat;
        return t.Sample(_lat, LowiLon, surface, valid, kts);
    }

    /// <summary>Taxi one poll at <paramref name="kts"/> and take the sample there.</summary>
    private SurroundingsSample Poll(SurroundingsSampleTracker t, double surface, double kts = 15.0, double valid = 1.0)
        => MoveAndSample(t, PerPoll(kts), surface, kts, valid);

    /// <summary>The surface switch on (the passing one only if asked), already taxiing on asphalt:
    /// the first sample recorded, the baseline set, nothing said.</summary>
    private SurroundingsSampleTracker TaxiingOnAsphalt(bool passingOn = false)
    {
        var t = new SurroundingsSampleTracker();
        if (passingOn) t.SetPassingEnabled(true);
        t.SetSurfaceEnabled(true);
        for (int i = 0; i < 4; i++) Assert.Null(Poll(t, Asphalt).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Paved, t.AnnouncedSurface);
        return t;
    }

    // ---- SC-2, end to end at the production cadence ----

    [Fact]
    public void A_one_poll_clip_of_grass_at_15_knots_says_nothing()
    {
        // One wheel onto the grass at a tight corner and straight back is ONE poll on the grass,
        // arriving 15.4 m after the last asphalt reading — more than ConfirmMetres.
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Null(Poll(t, Asphalt).SurfaceCallout);
        Assert.Null(Poll(t, Asphalt).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Paved, t.AnnouncedSurface);
    }

    [Fact]
    public void Rolling_on_across_the_grass_at_15_knots_is_announced_on_the_second_poll()
    {
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal("Off the pavement, on grass.", Poll(t, Grass).SurfaceCallout);
    }

    [Fact]
    public void At_10_knots_it_is_announced_on_the_third_poll()
    {
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass, kts: 10.0).SurfaceCallout);
        Assert.Null(Poll(t, Grass, kts: 10.0).SurfaceCallout);
        Assert.Equal("Off the pavement, on grass.", Poll(t, Grass, kts: 10.0).SurfaceCallout);
    }

    [Fact]
    public void Regaining_the_taxiway_is_announced_the_same_way()
    {
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal("Off the pavement, on grass.", Poll(t, Grass).SurfaceCallout);
        Assert.Null(Poll(t, Concrete).SurfaceCallout);
        Assert.Equal("Back on pavement.", Poll(t, Concrete).SurfaceCallout);
    }

    [Fact]
    public void Only_the_sentence_leaving_the_pavement_says_so()
    {
        // The landing roll's own "Off pavement." may already have covered that one; nothing else.
        var t = TaxiingOnAsphalt();
        Assert.False(Poll(t, Grass).LeavesPavement);
        var off = Poll(t, Grass);
        Assert.Equal("Off the pavement, on grass.", off.SurfaceCallout);
        Assert.True(off.LeavesPavement);
        Assert.False(Poll(t, Concrete).LeavesPavement);
        var back = Poll(t, Concrete);
        Assert.Equal("Back on pavement.", back.SurfaceCallout);
        Assert.False(back.LeavesPavement);
    }

    // ---- first samples and teleports ----

    [Fact]
    public void The_first_ground_sample_is_recorded_never_judged()
    {
        // The first ground sample after a landing, a Reset() or a pause has nothing to measure it
        // from — no distance, no jump test — so it is only recorded: no callout, and not even the
        // baseline, which the NEXT sample sets.
        var t = new SurroundingsSampleTracker();
        t.SetSurfaceEnabled(true);
        var first = Poll(t, Asphalt);
        Assert.True(first.Usable);
        Assert.True(first.First);
        Assert.Null(first.SurfaceCallout);
        Assert.Equal(SurfaceFamily.Unknown, t.AnnouncedSurface);   // not even a baseline yet
        for (int i = 0; i < 5; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
    }

    [Fact]
    public void A_teleport_onto_the_grass_is_a_silent_baseline_and_is_reported_as_a_jump()
    {
        // The gate-teleport dialog, a slew, a flight reload: the pilot was PUT on the grass.
        var t = TaxiingOnAsphalt();
        var landed = MoveAndSample(t, 3000.0, Grass);
        Assert.True(landed.Jumped);
        Assert.False(landed.First);
        Assert.Null(landed.SurfaceCallout);
        for (int i = 0; i < 5; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
    }

    // ---- SC-4: nothing non-finite ever confirms ----

    [Theory]
    [InlineData(double.NaN, LowiLon)]
    [InlineData(LowiLat, double.NaN)]
    [InlineData(double.PositiveInfinity, LowiLon)]
    public void An_unreadable_position_is_skipped_and_never_becomes_the_reference(double lat, double lon)
    {
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);                  // the grass evidence opens
        var bad = t.Sample(lat, lon, Grass, 1.0, 15.0);
        Assert.False(bad.Usable);
        Assert.Null(bad.SurfaceCallout);
        // The next READABLE poll is measured from the last readable position — one poll of grass,
        // enough to confirm. Kept as the reference, the NaN would have made that distance NaN too.
        var next = Poll(t, Grass);
        Assert.True(next.Usable);
        Assert.False(next.First);
        Assert.Equal("Off the pavement, on grass.", next.SurfaceCallout);
    }

    [Fact]
    public void A_non_finite_surface_type_is_no_surface_at_all()
    {
        // (int)NaN is 0 on .NET 9 and later — CONCRETE. Read that way, a NaN type would tell a
        // pilot still on the grass that they were back on pavement.
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal("Off the pavement, on grass.", Poll(t, Grass).SurfaceCallout);
        for (int i = 0; i < 5; i++) Assert.Null(Poll(t, double.NaN).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
    }

    [Fact]
    public void A_non_finite_validity_flag_is_not_valid()
    {
        // NaN != 0 is TRUE, so a bare "!= 0" would call an unreadable flag valid.
        var t = TaxiingOnAsphalt();
        for (int i = 0; i < 5; i++) Assert.Null(Poll(t, Grass, valid: double.NaN).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Paved, t.AnnouncedSurface);
    }

    // ---- SC-3: a switch turned back on starts from a silent baseline ----

    [Fact]
    public void Turning_the_surface_switch_back_on_starts_from_a_silent_baseline()
    {
        // The passing switch keeps the monitor sampling while this one is off, so the position
        // stays current — it is the SURFACE the gate last believed that is stale.
        var t = TaxiingOnAsphalt(passingOn: true);
        Assert.False(t.SetSurfaceEnabled(false));
        for (int i = 0; i < 4; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);   // driven off, unwatched
        Assert.True(t.SetSurfaceEnabled(true));
        var after = Poll(t, Grass);
        Assert.False(after.First);                                               // position kept
        Assert.Null(after.SurfaceCallout);                                       // not news now
        for (int i = 0; i < 3; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
        Assert.Null(Poll(t, Asphalt).SurfaceCallout);
        Assert.Equal("Back on pavement.", Poll(t, Asphalt).SurfaceCallout);      // driven while watched
    }

    [Fact]
    public void After_both_switches_were_off_nothing_driven_during_the_pause_is_measured_or_announced()
    {
        // Review item A2-5: with both off the monitor samples nothing, so the last position froze
        // where the pause began. Re-enabling 200 m further on — under the 250 m jump test — used to
        // credit all 200 m to the grass and announce it on the spot.
        var t = TaxiingOnAsphalt();            // surface on, passing off
        t.SetSurfaceEnabled(false);            // both off: the monitor stops sampling
        _lat += 200.0 / MetresPerDegreeLat;    // taxied 200 m onto the grass, unsampled
        Assert.True(t.SetSurfaceEnabled(true));
        var resumed = t.Sample(_lat, LowiLon, Grass, 1.0, 15.0);
        Assert.True(resumed.First);            // nothing to measure from
        Assert.Null(resumed.SurfaceCallout);
        for (int i = 0; i < 4; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
    }

    [Fact]
    public void Turning_the_passing_switch_on_after_a_pause_forgets_the_last_position_too()
    {
        var t = new SurroundingsSampleTracker();
        Assert.True(t.SetPassingEnabled(true));
        Assert.True(Poll(t, Asphalt).First);
        Assert.False(Poll(t, Asphalt).First);
        t.SetPassingEnabled(false);            // both off: the monitor stops sampling
        _lat += 200.0 / MetresPerDegreeLat;
        Assert.True(t.SetPassingEnabled(true));
        Assert.True(Poll(t, Asphalt).First);   // the passing half waits a tick, as after a liftoff
    }

    [Fact]
    public void Each_switch_reports_only_its_own_off_to_on_edge()
    {
        // The settings dialog assigns both switches on every OK: re-assigning ON is not an edge.
        var t = new SurroundingsSampleTracker();
        Assert.True(t.SetPassingEnabled(true));
        Assert.False(t.SetPassingEnabled(true));
        Assert.False(t.SetPassingEnabled(false));
        Assert.False(t.SetPassingEnabled(false));
        Assert.True(t.SetPassingEnabled(true));

        Assert.True(t.SetSurfaceEnabled(true));
        Assert.False(t.SetSurfaceEnabled(true));
        Assert.False(t.SetSurfaceEnabled(false));
        Assert.True(t.SetSurfaceEnabled(true));

        Assert.True(t.PassingEnabled);
        Assert.True(t.SurfaceEnabled);
    }

    [Fact]
    public void Reassigning_the_surface_switch_the_value_it_has_keeps_an_excursion_s_evidence()
    {
        var t = TaxiingOnAsphalt();
        Assert.Null(Poll(t, Grass).SurfaceCallout);    // evidence open
        Assert.False(t.SetSurfaceEnabled(true));       // a settings OK that touched nothing
        Assert.Equal("Off the pavement, on grass.", Poll(t, Grass).SurfaceCallout);
    }

    [Fact]
    public void Turning_the_passing_switch_on_never_disturbs_a_surface_excursion_in_progress()
    {
        // The two callouts are independent on purpose: switching the convenience one on must not
        // cost the safety one the evidence it is collecting right now.
        var t = TaxiingOnAsphalt();                    // surface on, passing off
        Assert.Null(Poll(t, Grass).SurfaceCallout);    // evidence open
        Assert.True(t.SetPassingEnabled(true));
        var next = Poll(t, Grass);
        Assert.False(next.First);
        Assert.Equal("Off the pavement, on grass.", next.SurfaceCallout);
    }

    [Fact]
    public void While_the_surface_switch_is_off_nothing_is_said()
    {
        var t = new SurroundingsSampleTracker();
        t.SetPassingEnabled(true);
        for (int i = 0; i < 4; i++) Poll(t, Asphalt);
        for (int i = 0; i < 6; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Unknown, t.AnnouncedSurface);   // never fed
    }

    [Fact]
    public void Reset_forgets_the_position_and_the_baseline_but_keeps_the_switches()
    {
        // A reconnect, an aircraft or database switch, a turnaround liftoff, an airborne episode.
        var t = TaxiingOnAsphalt(passingOn: true);
        t.Reset();
        Assert.True(t.SurfaceEnabled);
        Assert.True(t.PassingEnabled);
        Assert.True(Poll(t, Grass).First);
        for (int i = 0; i < 4; i++) Assert.Null(Poll(t, Grass).SurfaceCallout);
        Assert.Equal(SurfaceFamily.Grass, t.AnnouncedSurface);
    }
}
