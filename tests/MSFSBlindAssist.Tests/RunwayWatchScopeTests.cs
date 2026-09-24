// RunwayWatchScopes — which runway(s) the ground-traffic runway watch covers, and the runway's
// identity across the whole departure. PR #247 review R2/R3: no ordinary hold-short was watched,
// the backtrack and a crossing in progress were unwatched, and the hold → lineup hand-over changed
// the watch key, so the lineup restarted the watch and baselined whatever was on final in silence.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayWatchScopeTests
{
    // 09/27 at 50°N, ~3 km, due east from (50, 0); 04/22 crossing it diagonally near lon 0.02.
    private static readonly TaxiGraph.RunwayCenterline Rwy0927 = new()
    {
        Name1 = "09", Name2 = "27", Lat1 = 50.0, Lon1 = 0.0, Lat2 = 50.0, Lon2 = 0.042,
        HeadingDeg1 = 90, HalfWidthMeters = 22.5,
    };
    private static readonly TaxiGraph.RunwayCenterline Rwy0422 = new()
    {
        Name1 = "04", Name2 = "22", Lat1 = 49.99, Lon1 = 0.01, Lat2 = 50.01, Lon2 = 0.03,
        HeadingDeg1 = 45, HalfWidthMeters = 22.5,
    };
    private static readonly TaxiGraph.RunwayCenterline[] Runways = { Rwy0927, Rwy0422 };

    private static RunwayWatchInputs Inputs(
        TaxiGuidanceState state = TaxiGuidanceState.Taxiing, string? held = null, string? progressive = null,
        string? destination = "Runway 27", bool runwayLineup = true, string[]? under = null, string? takeoff = null,
        bool landingExit = false, double? gs = null, string? vacatingRunwayKey = null)
        => new(state, held, progressive, destination, runwayLineup, under ?? Array.Empty<string>(), takeoff, Runways,
            landingExit, gs, vacatingRunwayKey);

    [Theory]
    [InlineData("27")]
    [InlineData("09")]
    [InlineData("Runway 27")]
    [InlineData("9")]
    public void Both_ends_of_one_runway_share_one_key(string designator)
        => Assert.Equal("09/27", RunwayWatchScopes.RunwayKey(Runways, designator));

    [Fact]
    public void A_designator_with_no_centerline_keys_on_itself()
        => Assert.Equal("18", RunwayWatchScopes.RunwayKey(Runways, "18"));

    [Fact]
    public void Designators_reads_labels_and_bare_designators()
    {
        Assert.Equal(new[] { "27" }, RunwayWatchScopes.Designators("Runway 27"));
        Assert.Equal(new[] { "09" }, RunwayWatchScopes.Designators("B, Runway 09"));
        Assert.Equal(new[] { "27", "04" }, RunwayWatchScopes.Designators("runway 27 and runway 04"));
        Assert.Equal(new[] { "27L" }, RunwayWatchScopes.Designators("27l"));
        Assert.Empty(RunwayWatchScopes.Designators(null));
        Assert.Empty(RunwayWatchScopes.Designators("  "));
    }

    [Fact]
    public void A_crossing_hold_watches_the_runway_it_names()
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "B, Runway 09"));
        Assert.Equal(RunwayWatchMode.Holding, w.Mode);
        Assert.Equal(new WatchedRunway("09", "09/27"), Assert.Single(w.Runways));
        Assert.False(w.RunwayEventsInterrupt);
    }

    [Fact]
    public void A_staged_hold_watches_every_runway_it_guards()
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "runway 27 and runway 04"));
        Assert.Equal(new[] { "04/22", "09/27" }, w.Runways.Select(r => r.Key).OrderBy(k => k));
        Assert.Equal("04/22,09/27", w.Key);
    }

    [Fact]
    public void The_departure_hold_backtrack_lineup_and_takeoff_wait_are_one_watch()
    {
        string hold = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "Runway 27")).Key;
        var back = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.BacktrackDeparture, under: new[] { "09" }));
        var lineup = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.LiningUp, under: new[] { "27" }));
        var wait = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Inactive, destination: null, under: new[] { "27" }, takeoff: "27"));

        Assert.Equal("09/27", hold);
        Assert.Equal(hold, back.Key);
        Assert.Equal(hold, lineup.Key);
        Assert.Equal(hold, wait.Key);
        Assert.Equal(RunwayWatchMode.OnRunway, back.Mode);
        Assert.Equal(RunwayWatchMode.LiningUp, lineup.Mode);
        Assert.Equal(RunwayWatchMode.TakeoffWait, wait.Mode);
        Assert.Equal("27", Assert.Single(wait.Runways).Designator);   // the end the pilot departs from
    }

    [Fact]
    public void A_gate_lineup_watches_nothing()
        => Assert.False(RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.LiningUp, runwayLineup: false)).IsActive);

    [Fact]
    public void Standing_on_a_runway_watches_it_in_any_state()
    {
        var crossing = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, under: new[] { "22" }));
        Assert.Equal(RunwayWatchMode.OnRunway, crossing.Mode);
        Assert.Equal("04/22", crossing.Key);
        Assert.True(crossing.RunwayEventsInterrupt);

        var stoppedAfterLanding = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.LandingRollout, under: new[] { "27" }));
        Assert.Equal(RunwayWatchMode.OnRunway, stoppedAfterLanding.Mode);
    }

    // ── Vacating after landing (PR #247 B2 review Important 3) ────────────────────────────
    // At every landing-exit hand-off the rollout switches to Taxiing while the aircraft is still on the
    // runway just landed on. While the pilot turns off it and taxi guidance speaks the exit, runway
    // traffic waits its turn. Any other runway entry — a crossing no hold could be placed for, straying
    // onto a runway, or STOPPING on the runway on that route — is on the runway, and interrupts.

    [Fact]
    public void On_a_runway_off_any_landing_exit_route_the_watch_interrupts()
    {
        // An unheld crossing ("…with no hold short point for runway 06L") or a stray onto a runway.
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, under: new[] { "27" }, gs: 12));
        Assert.Equal(RunwayWatchMode.OnRunway, w.Mode);
        Assert.True(w.RunwayEventsInterrupt);
    }

    [Fact]
    public void Turning_off_on_a_landing_exit_route_is_vacating_and_waits_its_turn()
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, under: new[] { "27" }, landingExit: true, gs: 12));
        Assert.Equal(RunwayWatchMode.Vacating, w.Mode);
        Assert.Equal("09/27", w.Key);
        Assert.False(w.RunwayEventsInterrupt);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(2.99)]
    public void Stopped_on_the_runway_on_a_landing_exit_route_is_on_the_runway(double gs)
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, under: new[] { "27" }, landingExit: true, gs: gs));
        Assert.Equal(RunwayWatchMode.OnRunway, w.Mode);
        Assert.True(w.RunwayEventsInterrupt);
    }

    [Fact]
    public void Vacating_begins_at_the_minimum_ground_speed()
        => Assert.Equal(RunwayWatchMode.Vacating, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: RunwayWatchScopes.VacatingMinGsKts)).Mode);

    [Fact]
    public void An_unknown_ground_speed_on_a_landing_exit_route_is_on_the_runway()
        => Assert.Equal(RunwayWatchMode.OnRunway, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: null)).Mode);

    // ── Vacating hysteresis (PR #247 B3 review Important 1) ────────────────────────────
    // An ordinary deceleration through the turn must not flip Vacating to OnRunway tick by tick: once
    // already vacating, the mode holds down to VacatingHoldGsKts; a fresh evaluation that was not
    // already vacating still needs the full VacatingMinGsKts to enter it. The hysteresis is scoped to
    // the runway that was vacating (PR #247 B4 review Important 2): VacatingRunwayKey must name the
    // SAME runway that is now under the aircraft, or a different runway entered right after the exit
    // must not inherit it.

    [Fact]
    public void Vacating_holds_below_the_minimum_speed_once_already_vacating()
        => Assert.Equal(RunwayWatchMode.Vacating, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: 2.0,
            vacatingRunwayKey: RunwayWatchScopes.RunwayKey(Runways, "27"))).Mode);

    [Fact]
    public void Not_yet_vacating_at_the_same_speed_is_on_the_runway()
        => Assert.Equal(RunwayWatchMode.OnRunway, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: 2.0, vacatingRunwayKey: null)).Mode);

    [Fact]
    public void Vacating_ends_below_the_hold_speed_even_if_it_was_already_vacating()
        => Assert.Equal(RunwayWatchMode.OnRunway, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: 0.9,
            vacatingRunwayKey: RunwayWatchScopes.RunwayKey(Runways, "27"))).Mode);

    [Fact]
    public void Vacating_holds_at_the_hold_speed_boundary()
        => Assert.Equal(RunwayWatchMode.Vacating, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: RunwayWatchScopes.VacatingHoldGsKts,
            vacatingRunwayKey: RunwayWatchScopes.RunwayKey(Runways, "27"))).Mode);

    [Fact]
    public void VacatingRunwayKey_grants_no_hysteresis_off_a_landing_exit_route()
        => Assert.Equal(RunwayWatchMode.OnRunway, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: false, gs: 5,
            vacatingRunwayKey: RunwayWatchScopes.RunwayKey(Runways, "27"))).Mode);

    [Fact]
    public void A_different_runways_vacating_key_grants_no_hysteresis()
        => Assert.Equal(RunwayWatchMode.OnRunway, RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing,
            under: new[] { "27" }, landingExit: true, gs: 2.0,
            vacatingRunwayKey: RunwayWatchScopes.RunwayKey(Runways, "22"))).Mode);

    // ── The first status re-armed on entering the runway (PR #247 final review H2) ────────────
    // A status queued at a hold or while vacating can be cut off by the very instruction that moved
    // the pilot ("Continuing.", "Entering Runway 27L…", the backtrack instruction, "Lined up.
    // Activating takeoff assist.", or stopping after a landing exit), and its latches already marked
    // every occupant and final known — so the SAME watch changing from a queuing mode into an
    // interrupting one re-arms it once, spoken only if something is on the runway or on short final.

    private static readonly RunwayWatchMode[] QueuingModes = { RunwayWatchMode.Holding, RunwayWatchMode.Vacating };
    private static readonly RunwayWatchMode[] InterruptingModes =
        { RunwayWatchMode.OnRunway, RunwayWatchMode.LiningUp, RunwayWatchMode.TakeoffWait };

    public static IEnumerable<object[]> EveryModeChange()
        => from f in Enum.GetValues<RunwayWatchMode>()
           from t in Enum.GetValues<RunwayWatchMode>()
           select new object[] { f, t, QueuingModes.Contains(f) && InterruptingModes.Contains(t) };

    [Theory]
    [MemberData(nameof(EveryModeChange))]
    public void Only_a_queuing_mode_into_an_interrupting_one_re_arms_the_first_status(
        RunwayWatchMode from, RunwayWatchMode to, bool expected)
        => Assert.Equal(expected, RunwayWatchScopes.ShouldRearmOnModeChange(from, to));

    [Fact]
    public void Six_modes_and_exactly_six_mode_changes_re_arm()
    {
        var modes = Enum.GetValues<RunwayWatchMode>();
        Assert.Equal(6, modes.Length);   // a new mode must be placed in one of the two sets above
        Assert.Equal(6, modes.SelectMany(f => modes.Where(t => RunwayWatchScopes.ShouldRearmOnModeChange(f, t))).Count());
    }

    [Fact]
    public void A_backtrack_outranks_vacating()
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.BacktrackDeparture, under: new[] { "09" },
            landingExit: true, gs: 12));
        Assert.Equal(RunwayWatchMode.OnRunway, w.Mode);
        Assert.True(w.RunwayEventsInterrupt);
    }

    [Fact]
    public void Lineup_and_the_takeoff_wait_interrupt()
    {
        var lineup = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.LiningUp, under: new[] { "27" }));
        var wait = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Inactive, destination: null, under: new[] { "27" }, takeoff: "27"));
        Assert.True(lineup.RunwayEventsInterrupt);
        Assert.True(wait.RunwayEventsInterrupt);
    }

    [Fact]
    public void A_hold_never_interrupts()
        => Assert.False(RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "B, Runway 09")).RunwayEventsInterrupt);

    [Fact]
    public void A_crossing_hold_and_the_crossing_itself_share_the_key()
    {
        var hold = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "B, Runway 04"));
        var crossing = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, under: new[] { "22" }));
        Assert.Equal(hold.Key, crossing.Key);
    }

    [Fact]
    public void A_progressive_hold_watches_its_runway_at_the_hold_only()
    {
        Assert.Equal(RunwayWatchMode.Holding,
            RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.ProgressiveHold, progressive: "27")).Mode);
        Assert.False(RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.Taxiing, progressive: "27")).IsActive);
        Assert.False(RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.ProgressiveHold)).IsActive);
    }

    [Fact]
    public void A_runway_the_graph_does_not_carry_is_never_watched()
        => Assert.False(RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.HoldShort, held: "Runway 18")).IsActive);

    [Fact]
    public void Precedence_picks_the_strongest_mode_and_one_entry_per_runway()
    {
        var w = RunwayWatchScopes.Resolve(Inputs(TaxiGuidanceState.LiningUp, under: new[] { "09" }));
        Assert.Equal(RunwayWatchMode.LiningUp, w.Mode);
        Assert.Equal("27", Assert.Single(w.Runways).Designator);
    }

    [Fact]
    public void RunwaysUnder_names_the_nearer_end_of_the_pavement_the_aircraft_is_on()
    {
        Assert.Equal(new[] { "09" }, RunwayWatchScopes.RunwaysUnder(Runways, 50.0, 0.005));
        Assert.Equal(new[] { "27" }, RunwayWatchScopes.RunwaysUnder(Runways, 50.0, 0.037));
        Assert.Empty(RunwayWatchScopes.RunwaysUnder(Runways, 50.0 + 100 / 110540.0, 0.005));
    }

    [Fact]
    public void IsLocal_is_false_for_a_context_from_another_airport()
    {
        var route = new[]
        {
            new GroundTrafficRoutePoint(50.0, -0.002, "A", 0),
            new GroundTrafficRoutePoint(50.0, 0.0, "A", 143),
        };
        Assert.True(RunwayWatchScopes.IsLocal(route, Runways, 50.0, 0.001));
        Assert.True(RunwayWatchScopes.IsLocal(Array.Empty<GroundTrafficRoutePoint>(), Runways, 50.02, 0.0));
        Assert.False(RunwayWatchScopes.IsLocal(route, Runways, 51.0, 0.0));
        Assert.False(RunwayWatchScopes.IsLocal(null, null, 50.0, 0.0));
    }
}
