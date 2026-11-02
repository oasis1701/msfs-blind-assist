// The navigation display sampler reads one raw reading through the derived layout: every object pointer
// again on every read, each object range-checked against the module's committed memory (WASM-4); the
// route as a bounded walk, the nearby lists, the traffic, the CDU FIX page and every other source. A
// pointer leaving the module, a loop, a route over the cap or a node, element or target that does not look
// real discards the whole reading (never half a list); unreadable memory is "lost", not corrupt.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;

namespace MSFSBlindAssist.Tests;

public class ModuleNavDisplaySamplerTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;
    private static NdRouteFields R => L.Route;

    private static NavDisplaySample Read(M memory)
    {
        using var sampler = memory.Sampler();
        Assert.True(sampler.TryRead(out var sample, out var corrupt), corrupt);
        return sample;
    }

    private static string Corrupt(M memory)
    {
        using var sampler = memory.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.NotNull(corrupt);
        return corrupt!;
    }

    private static double Deg(double rad) => rad * 180 / Math.PI;

    [Fact]
    public void Reads_the_track_up_option()
    {
        var m = new M();
        Assert.True(Read(m).TrackUpOption);
        m.B(M.At(M.Options, L.Orientation.TrackUpOption), false);
        Assert.False(Read(m).TrackUpOption);
    }

    // ---- the route ----

    [Fact]
    public void Reads_the_live_route_through_the_layout()
    {
        var x = Read(new M());

        Assert.Equal(new[] { "LPPT", "EMPAD", "PT753", "IDBID", "ORVED" }, x.Route.Select(w => w.Name));
        Assert.Equal(new[] { false, true, false, false, false }, x.Route.Select(w => w.IsTo));
        Assert.Equal(new[] { 0, 6.8, 23.0, 30.5, 18.1 }, x.Route.Select(w => w.Distance));
        Assert.Equal(new NdClock?[] { null, new(15, 33, 18), new(15, 37, 12), new(15, 41, 30), new(15, 43, 54) }, x.Route.Select(w => w.Eta));
        Assert.Equal("PT753", x.Route[2].MapName);
        Assert.Equal(39.10, Deg(x.Route[2].Position!.Value.LatitudeRad), 6);

        var to = Assert.IsType<NdWaypoint>(x.Active);
        Assert.Equal(("EMPAD", 6.8, (NdClock?)new NdClock(15, 33, 18)), (to.Name, to.Distance, to.Eta));
        Assert.Equal(38.77427, Deg(x.Aircraft!.Value.LatitudeRad), 6);
        Assert.Equal(-9.13153, Deg(x.Aircraft!.Value.LongitudeRad), 6);
        Assert.Null(x.ModifiedRoute);
        Assert.Equal((4000, 6000), (x.TransitionAltitude, x.TransitionLevel));

        Assert.Equal((L.Geometry.Modes.Map, 2, true), (x.Panel.Mode, x.Panel.Range, x.Panel.Powered));
        Assert.Equal(L.Units.FullNdPages[0], x.UnitPages[0]);
        Assert.Equal((79.4, 79.4, true, -1.5, 6, true), (x.HeadingDeg, x.TrackDeg, x.MagneticReference, x.MagneticVariationDeg, x.IruCondition, x.ReadoutValid));
        Assert.Equal((0.4, 351.0, 13.8), (x.TrueAirspeed, x.WindDirection, x.WindSpeed));
        Assert.Equal((80, 5000, 374.0), (x.SelectedHeadingDeg, x.McpAltitudeFt, x.AltitudeFt));
        Assert.Equal((2.0, 0.05), (x.Rnp, x.Anp));
        Assert.Empty(x.Markers);
        Assert.Empty(x.Objects);
        Assert.Empty(x.Traffic);
        Assert.Empty(x.FixPage);
        Assert.Null(x.PlnCentre);
    }

    [Fact]
    public void Pseudo_nodes_are_walked_and_marked()
    {
        var m = new M();
        uint pseudo = m.AddNode(10, "@P1", 2.0, null, 38.8, -9.1);
        m.Chain(new[] { m.RouteNodes[0], pseudo, m.RouteNodes[1], m.RouteNodes[2], m.RouteNodes[3], m.RouteNodes[4] });
        m.SetKeys(0, m.RouteNodes[0], pseudo);

        var x = Read(m);
        // The TO corner skips "@" nodes; the map's walk carries them as PMDG's filter leaves them.
        Assert.Equal("EMPAD", x.Active!.Name);
        Assert.Equal(new[] { "LPPT", "@P1", "EMPAD", "PT753", "IDBID", "ORVED" }, x.Route.Select(w => w.Name));
        Assert.True(x.Route[1].IsTo);
    }

    [Fact]
    public void Undrawn_and_skipped_nodes_are_left_out_of_the_route()
    {
        var m = new M();
        var v = R.Validity;
        m.B(m.RouteNodes[0] + (uint)v.StartSkip, true);        // the start search walks past LPPT
        m.B(m.RouteNodes[2] + (uint)v.UndrawnUnlessTo, true);  // PT753: not the TO node, not drawn
        m.I(m.RouteNodes[3] + (uint)v.Kind, v.UndrawnKind);     // IDBID: a kind PMDG does not draw
        m.B(m.RouteNodes[1] + (uint)v.UndrawnUnlessTo, true);  // EMPAD is the TO node: drawn anyway

        Assert.Equal(new[] { "EMPAD", "ORVED" }, Read(m).Route.Select(w => w.Name));
    }

    [Fact]
    public void A_hidden_node_with_a_junk_name_is_walked_past()
    {
        var m = new M();
        uint hidden = m.AddNode(10, "", 0, null, 0, 0);
        m.B(hidden + (uint)R.Validity.Hidden, true);
        m.Text(hidden + (uint)R.NodeName, "\u0001\u0002", terminated: false);
        m.Chain(new[] { m.RouteNodes[0], m.RouteNodes[1], hidden, m.RouteNodes[2], m.RouteNodes[3], m.RouteNodes[4] });

        Assert.Equal(new[] { "LPPT", "EMPAD", "PT753", "IDBID", "ORVED" }, Read(m).Route.Select(w => w.Name));
    }

    [Fact]
    public void A_next_pointer_outside_the_module_discards_the_reading()
    {
        var m = new M();
        m.U(m.RouteNodes[2] + (uint)R.NodeNext, 0x7FFF0000);
        Assert.Contains("outside the module", Corrupt(m));
    }

    [Fact]
    public void A_loop_in_the_route_discards_the_reading()
    {
        var m = new M();
        m.U(m.RouteNodes[4] + (uint)R.NodeNext, m.RouteNodes[1]);
        Assert.Contains("twice", Corrupt(m));
    }

    [Fact]
    public void A_route_over_the_cap_discards_the_reading()
    {
        var m = new M();
        var chain = Enumerable.Range(0, R.MaxNodes + 1).Select(k => m.AddNode(k, $"W{k}", 1, null, 10, 10)).ToList();
        m.Chain(chain);
        m.SetKeys(0, chain[0], chain[1]);
        Assert.Contains("512", Corrupt(m));
    }

    [Theory]
    [InlineData("empty name", "name")]
    [InlineData("unprintable name", "name")]
    [InlineData("name without its NUL", "name")]
    [InlineData("latitude", "position")]
    [InlineData("longitude", "position")]
    [InlineData("distance not finite", "distance")]
    [InlineData("distance negative", "distance")]
    [InlineData("distance beyond half the earth", "distance")]
    [InlineData("clock hour", "clock")]
    [InlineData("clock minute", "clock")]
    public void A_bad_name_position_distance_or_clock_discards_the_reading(string bad, string check)
    {
        // Names and positions are checked on every node; the distance and clock only on the TO node (EMPAD),
        // the one node PMDG prints them for.
        var m = new M();
        uint n = bad.StartsWith("distance", StringComparison.Ordinal) || bad.StartsWith("clock", StringComparison.Ordinal) ? m.RouteNodes[1] : m.RouteNodes[2];
        uint p = n + (uint)R.NodePosition, clock = n + (uint)R.NodeEta;
        switch (bad)
        {
            case "empty name": m.Text(n + (uint)R.NodeName, ""); break;
            case "unprintable name": m.Text(n + (uint)R.NodeName, "PT\u0001753"); break;
            case "name without its NUL": m.Text(n + (uint)R.NodeName, new string('A', R.NodeNameLength), terminated: false); break;
            case "latitude": m.D(p + (uint)R.Position.LatitudeRad, 1.7); break;
            case "longitude": m.D(p + (uint)R.Position.LongitudeRad, -3.2); break;
            case "distance not finite": m.D(n + (uint)R.NodeDistance, double.NaN); break;
            case "distance negative": m.D(n + (uint)R.NodeDistance, -1); break;
            case "distance beyond half the earth": m.D(n + (uint)R.NodeDistance, 10_801); break;
            case "clock hour": m.I(clock + (uint)R.Clock.Hours, 24); break;
            case "clock minute": m.I(clock + (uint)R.Clock.Minutes, 60); break;
        }
        Assert.Contains(check, Corrupt(m));
    }

    [Fact]
    public void A_later_waypoint_s_distance_and_clock_are_not_checked()
    {
        // PMDG prints no distance for a later node, and its ETA only with DATA: a value out of range there is
        // no reason to discard the reading; the clock reads as no ETA.
        var m = new M();
        uint n = m.RouteNodes[2], clock = n + (uint)R.NodeEta;
        m.D(n + (uint)R.NodeDistance, double.NaN);
        m.I(clock + (uint)R.Clock.Hours, 24);
        var x = Read(m);
        Assert.Equal("PT753", x.Route[2].Name);
        Assert.Null(x.Route[2].Eta);
        Assert.Equal(new NdClock(15, 33, 18), x.Active!.Eta);
    }

    [Theory]
    [InlineData("(1500)")]
    [InlineData("(INTC)")]
    [InlineData("RW09L")]
    public void Conditional_leg_names_are_accepted(string name)
    {
        var m = new M();
        m.Text(m.RouteNodes[2] + (uint)R.NodeName, name);
        Assert.Equal(name, Read(m).Route[2].Name);
    }

    [Fact]
    public void No_active_route_gives_an_empty_route()
    {
        var m = new M();
        m.SetKeys(0, 0, 0);
        var x = Read(m);
        Assert.Empty(x.Route);
        Assert.Null(x.Active);
    }

    [Fact]
    public void A_pending_modification_is_read_as_the_modified_route()
    {
        // As live at KATL (2026-10-25): a waypoint deleted on LEGS, not executed: the MOD plan (reference +
        // ModOffset) active with its own nodes, a hidden discontinuity in the deleted waypoint's place.
        var m = new M();
        uint from = m.AddNode(20, "LPPT", 0, null, 38.7813, -9.1359), to = m.AddNode(21, "EMPAD", 6.8, null, 38.86, -9.02);
        uint disc = m.AddNode(22, "DISC", 0, null, 0, 0);
        m.B(disc + (uint)R.Validity.Hidden, true);
        uint after = m.AddNode(23, "IDBID", 30.5, null, 39.45, -8.20);
        m.Chain(new[] { from, to, disc, after });
        m.SetKeys(R.ModOffset, from, to);
        m.B(M.Plan(R.ModOffset) + (uint)R.PlanActive, true);

        var x = Read(m);
        Assert.Equal(new[] { "LPPT", "EMPAD", "IDBID" }, x.ModifiedRoute!.Select(w => w.Name));
        Assert.Equal(new[] { "LPPT", "EMPAD", "PT753", "IDBID", "ORVED" }, x.Route.Select(w => w.Name));
    }

    // ---- the route line ----

    [Fact]
    public void Reads_the_route_line()
    {
        var x = Read(new M());
        Assert.Equal(4, x.RoutePath.Count);
        Assert.All(x.RoutePath, p => Assert.False(p.Turn));
        Assert.All(x.RoutePath, p => Assert.Null(p.SecondLine));
        Assert.Equal(new[] { "EMPAD", "PT753", "IDBID", "ORVED" }, x.RoutePath.Select(p => p.Toward!.Name));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, x.RoutePath.Select(p => p.Toward!.RouteIndex));
        Assert.Equal(new NdPathLine(x.Route[0].Position!.Value, x.Route[1].Position!.Value), x.RoutePath[0].FirstLine);
        Assert.Equal(x.Route[1].Position, x.RoutePath[0].Toward!.Position);
    }

    [Fact]
    public void The_test_memory_s_line_follows_a_moved_waypoint()
    {
        // The test memory's guard (a text test once passed on a line left at a waypoint's old place).
        var m = new M();
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, 39.5, -8.0);
        var x = Read(m);
        Assert.Equal(x.Route[2].Position, x.RoutePath[1].FirstLine!.Value.To);
        Assert.Equal(x.Route[2].Position, x.RoutePath[2].FirstLine!.Value.From);
    }

    [Fact]
    public void No_route_line_while_the_path_is_not_valid()
    {
        var m = new M();
        m.B(M.Plan(0) + (uint)(R.Markers.LateralPath + R.LateralPath.Invalid), true);
        Assert.Empty(Read(m).RoutePath);
    }

    [Fact]
    public void The_route_line_stops_at_the_first_free_point()
    {
        var m = new M();
        m.SetPathPoint(0, 1, null, 0, inUse: false);
        Assert.Single(Read(m).RoutePath);
    }

    [Fact]
    public void A_short_or_unplaced_line_is_not_drawn()
    {
        var m = new M();
        var lp = R.LateralPath;
        uint point = M.Plan(0) + (uint)(R.Markers.LateralPath + lp.Points + lp.PointStride);
        m.D(point + (uint)(lp.FirstLine + lp.LineLength), lp.LineMinLength);
        m.B(M.Plan(0) + (uint)(R.Markers.LateralPath + lp.Points + 2 * lp.PointStride + lp.FirstLine + lp.LineStart + R.Position.Valid), false);
        var x = Read(m);
        Assert.Null(x.RoutePath[1].FirstLine);
        Assert.Null(x.RoutePath[2].FirstLine);
        Assert.NotNull(x.RoutePath[3].FirstLine);
    }

    [Fact]
    public void A_line_heading_for_an_undrawn_node_names_the_next_drawn_waypoint()
    {
        // A discontinuity (a hidden node) between PT753 and IDBID: the point's node is the discontinuity.
        var m = new M();
        uint disc = m.AddNode(10, "DISC", 0, null, 39.2, -8.5);
        m.B(disc + (uint)R.Validity.Hidden, true);
        m.Chain(new[] { m.RouteNodes[0], m.RouteNodes[1], m.RouteNodes[2], disc, m.RouteNodes[3], m.RouteNodes[4] });
        m.SetPathPoint(0, 2, (m.RouteNodes[2], m.RouteNodes[3]), disc);
        var toward = Read(m).RoutePath[2].Toward!;
        Assert.Equal(("IDBID", (int?)3), (toward.Name, toward.RouteIndex));
    }

    [Fact]
    public void A_line_heading_for_a_node_off_the_route_names_the_node_itself()
    {
        var m = new M();
        uint other = m.AddNode(11, "XRAY", 0, null, 39.9, -7.5);
        m.SetPathPoint(0, 3, (m.RouteNodes[3], other), other);
        var toward = Read(m).RoutePath[3].Toward!;
        Assert.Equal(("XRAY", (int?)null), (toward.Name, toward.RouteIndex));
        Assert.Equal(39.9, Deg(toward.Position!.Value.LatitudeRad), 6);
    }

    [Fact]
    public void A_line_heading_for_an_unnamed_node_off_the_route_heads_for_nothing()
    {
        // PMDG prints nothing of that node (only its direct-to intercept drawing takes it): its name is not
        // checked as a printed one (WASM-14), so a bad one leaves the line heading for nothing, not a corrupt reading.
        var m = new M();
        uint other = m.AddNode(11, "", 0, null, 39.9, -7.5);
        m.SetPathPoint(0, 3, (m.RouteNodes[3], other), other);
        Assert.Null(Read(m).RoutePath[3].Toward);
        m.Bytes[other + (uint)R.NodeMapName] = 0x07;
        Assert.Null(Read(m).RoutePath[3].Toward);
    }

    [Fact]
    public void A_line_heading_for_a_node_off_the_route_with_a_bad_position_names_it_without_one()
    {
        var m = new M();
        uint other = m.AddNode(11, "XRAY", 0, null, 39.9, -7.5);
        m.SetPathPoint(0, 3, (m.RouteNodes[3], other), other);
        m.D(other + (uint)(R.NodePosition + R.Position.LatitudeRad), 10.0);
        var toward = Read(m).RoutePath[3].Toward!;
        Assert.Equal(("XRAY", (GeoPoint?)null), (toward.Name, toward.Position));
    }

    [Fact]
    public void A_line_heading_for_a_node_outside_the_module_discards_the_reading()
    {
        var m = new M();
        m.SetPathPoint(0, 3, (m.RouteNodes[3], m.RouteNodes[4]), 0xFFFFFF00);
        Assert.Contains("the route line", Corrupt(m));
    }

    [Fact]
    public void A_route_line_longer_than_its_plan_discards_the_reading()
    {
        var m = new M();
        m.I(M.Plan(0) + (uint)(R.Markers.LateralPath + R.LateralPath.Count), 100000);
        Assert.Contains("the route line", Corrupt(m));
    }

    [Fact]
    public void A_pending_modification_reads_its_own_line()
    {
        var m = new M();
        uint from = m.AddNode(20, "LPPT", 0, null, 38.7813, -9.1359), to = m.AddNode(21, "EMPAD", 6.8, null, 38.86, -9.02);
        m.Chain(new[] { from, to });
        m.SetKeys(R.ModOffset, from, to);
        m.SetPath(R.ModOffset, new[] { from, to });
        m.B(M.Plan(R.ModOffset) + (uint)R.PlanActive, true);
        var x = Read(m);
        Assert.Equal("EMPAD", Assert.Single(x.ModifiedRoutePath!).Toward!.Name);
        Assert.Equal(4, x.RoutePath.Count);
    }

    // ---- the route's airports ----

    [Fact]
    public void Reads_the_route_airports()
    {
        // PMDG draws the departure at its runway's threshold when a runway is set, else at the
        // airport; then the arrival. Neither is set in the live reading's memory.
        var m = new M();
        Assert.Empty(Read(m).RouteAirports);
        m.AddRouteAirport(0, departure: true, "LPPT", 38.7813, -9.1359, runway: ("RW21", 38.7930, -9.1260));
        m.AddRouteAirport(0, departure: false, "LPFR", 37.0144, -7.9659);

        var x = Read(m);
        Assert.Equal(new[] { ("LPPT", "RW21", true), ("LPFR", (string?)null, false) }, x.RouteAirports.Select(a => (a.Name, a.Runway, a.Departure)));
        Assert.Equal((38.7930, -9.1260), (Deg(x.RouteAirports[0].Position!.Value.LatitudeRad), Deg(x.RouteAirports[0].Position!.Value.LongitudeRad)), new PointComparer());
        Assert.Equal((37.0144, -7.9659), (Deg(x.RouteAirports[1].Position!.Value.LatitudeRad), Deg(x.RouteAirports[1].Position!.Value.LongitudeRad)), new PointComparer());
        Assert.Null(x.ModifiedRouteAirports);
    }

    private sealed class PointComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) a, (double, double) b) => Math.Abs(a.Item1 - b.Item1) < 1e-6 && Math.Abs(a.Item2 - b.Item2) < 1e-6;

        public int GetHashCode((double, double) p) => 0;
    }

    [Fact]
    public void A_route_airport_without_a_valid_position_has_none()
    {
        var m = new M();
        uint lppt = m.AddRouteAirport(0, departure: true, "LPPT", 38.7813, -9.1359);
        m.B(lppt + (uint)(R.Airports.AirportPosition + R.Position.Valid), false);
        Assert.Null(Read(m).RouteAirports.Single().Position);
    }

    [Fact]
    public void A_route_airport_outside_the_module_discards_the_reading()
    {
        var m = new M();
        m.U(M.Plan(0) + (uint)(R.PlnCentre.RouteInfo + R.Airports.ArrivalRunway), 0x7FFF0000);
        m.AddRouteAirport(0, departure: false, "LPFR", 37.0144, -7.9659);
        Assert.Contains("outside the module", Corrupt(m));
    }

    [Fact]
    public void A_route_airport_with_a_bad_runway_name_discards_the_reading()
    {
        var m = new M();
        m.AddRouteAirport(0, departure: true, "LPPT", 38.7813, -9.1359, runway: ("RW\u000121", 38.7930, -9.1260));
        Assert.Contains("name", Corrupt(m));
    }

    [Fact]
    public void A_pending_modification_reads_its_own_airports()
    {
        var m = new M();
        m.SetKeys(R.ModOffset, 0, 0);
        m.B(M.Plan(R.ModOffset) + (uint)R.PlanActive, true);
        m.AddRouteAirport(R.ModOffset, departure: false, "LPPR", 41.2481, -8.6814);

        var x = Read(m);
        Assert.Equal(new[] { "LPPR" }, x.ModifiedRouteAirports!.Select(a => a.Name));
        Assert.Empty(x.RouteAirports);
    }

    [Fact]
    public void Longitudes_read_east_positive_from_PMDG_s_west_positive()
    {
        // PMDG's position record holds longitude positive west (live: KATL 84.4 W reads +84.4).
        var m = new M();
        m.D(M.Fms + (uint)(R.FmsPosition + R.Position.LongitudeRad), 84.43861 * Math.PI / 180);
        Assert.Equal(-84.43861, Deg(Read(m).Aircraft!.Value.LongitudeRad), 5);
    }

    [Fact]
    public void A_list_longer_than_the_old_cap_is_read()
    {
        // 441 fixes lay in PMDG's list at KATL at range 40 (live, 2026-10-25): far more than PMDG draws.
        var m = new M();
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        for (int k = 0; k < 600; k++) m.AddListObject(L.Lists.FixVector, $"F{k:000}", 33 + k * 0.001, -84);
        var x = Read(m);
        Assert.Equal(600, x.FixCount);
        Assert.Equal(600, x.Objects.Count);
    }

    [Fact]
    public void No_to_waypoint_while_the_readout_is_invalid()
    {
        var m = new M();
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.Null(Read(m).Active);
    }

    /// <summary>PMDG shows no TO waypoint while the map is invalid (its map-invalid flag), so with the
    /// readout valid and the FMC without an FMS position too.</summary>
    [Fact]
    public void No_to_waypoint_without_an_fms_position()
    {
        var m = new M();
        m.B(M.Fms + (uint)L.MapValidity.FmcValid, false);
        var x = Read(m);
        Assert.True(x.ReadoutValid);
        Assert.False(x.MapValid);
        Assert.Null(x.Active);
    }

    /// <summary>PMDG's cross-track error reads the TO waypoint's plan: plan 4's lateral path while that plan is active, else the
    /// reference route's.</summary>
    [Fact]
    public void Xtk_follows_the_to_waypoint_s_plan()
    {
        var m = new M();
        var x = L.Xtk;
        m.B(M.At(M.Options, L.Messages.Rnp.Option), true);
        foreach (int p in new[] { 0, R.ToNodePlan }) m.U(M.Keys(p) + (uint)x.KeySlot, m.RouteNodes[1]);
        m.D(M.Plan(0) + (uint)(R.Markers.LateralPath + x.Value), 1.5);
        m.D(M.Plan(R.ToNodePlan) + (uint)(R.Markers.LateralPath + x.Value), 2.5);
        Assert.Equal(1.5, Read(m).Xtk);
        m.B(M.Plan(R.ToNodePlan) + (uint)R.PlanActive, true);
        Assert.Equal(2.5, Read(m).Xtk);
    }

    // ---- the map's validity ----

    /// <summary>The map's FMC is the EFIS object's entry for the DEU of the unit drawing the Captain's ND; its position
    /// is valid while the FMS position is valid and the FMS object's byte at the EFIS object's entry for that FMC is set
    /// (for -1 the master FMC's).</summary>
    [Fact]
    public void The_map_s_fmc_is_the_one_the_captain_s_deu_shows()
    {
        var m = new M();
        var v = L.MapValidity;
        (int, bool) Fmc() => Read(m) is var x ? (x.Fmc, x.FmsPositionValid) : default;
        Assert.Equal((0, true), Fmc());
        m.B(M.Fms + (uint)v.FmcValid, false);
        Assert.Equal((0, false), Fmc());
        // Unit 0 (the Captain's full ND) on DEU 1: the EFIS object's entry 1 is FMC 1, whose own entry is 1, its byte set.
        m.I(M.Efis + (uint)L.Units.Deu, 1);
        Assert.Equal((1, true), Fmc());
        // FMC -1: the master FMC's entry.
        m.I(M.At(M.Efis, v.FmcIndex, 1), v.NoFmc);
        m.I(M.Efis + (uint)v.MasterFmc, 0);
        Assert.Equal((-1, false), Fmc());
        m.I(M.Efis + (uint)v.MasterFmc, 1);
        Assert.Equal((-1, true), Fmc());
        // No FMS position: not valid, whatever the byte.
        m.Position(M.Fms + (uint)R.FmsPosition, M.AircraftLatitude, M.AircraftLongitude, valid: false);
        Assert.Equal((-1, false), Fmc());
    }

    [Theory]
    [InlineData("deu")]
    [InlineData("fmc")]
    [InlineData("entry")]
    public void A_deu_fmc_or_entry_outside_its_object_discards_the_reading(string bad)
    {
        var m = new M();
        var v = L.MapValidity;
        switch (bad)
        {
            case "deu": m.I(M.Efis + (uint)L.Units.Deu, -1); break;
            case "fmc": m.I(M.At(M.Efis, v.FmcIndex), 1_000_000); break;
            default: m.I(M.At(M.Efis, v.FmcIndex), 1); m.I(M.At(M.Efis, v.FmcIndex, 1), 100_000_000); break;
        }
        Assert.Contains("the map's FMC", Corrupt(m));
    }

    // ---- the other sources ----

    [Fact]
    public void Nearby_lists_hold_more_than_PMDG_draws()
    {
        var m = new M();
        foreach (var f in new[] { L.Panel.ShowSta, L.Panel.ShowWpt, L.Panel.ShowArpt }) m.B(M.At(M.Panel, f), true);
        for (int k = 0; k < L.Lists.MaxElements + 1; k++) m.AddListObject(L.Lists.NavaidVector, $"V{k:00}", 38 + k * 0.01, -9, flags: L.Lists.VorFlag, @class: 1);
        m.AddListObject(L.Lists.FixVector, "ABCDE", 38.5, -9.5);
        m.AddListObject(L.Lists.AirportVector, "LPPT", 38.78, -9.14);

        var x = Read(m);
        Assert.Equal((L.Lists.MaxElements + 1, 1, 1), (x.NavaidCount, x.FixCount, x.AirportCount));
        Assert.Equal(new NdMapObject(NdObjectKind.Navaid, "V00", x.Objects[0].Position, L.Lists.VorFlag, 1), x.Objects[0]);
        Assert.Equal(new[] { "ABCDE", "LPPT" }, x.Objects.Where(o => o.Kind != NdObjectKind.Navaid).Select(o => o.Name));
    }

    [Fact]
    public void A_list_element_outside_the_module_discards_the_reading()
    {
        var m = new M();
        m.B(M.At(M.Panel, L.Panel.ShowSta), true);
        m.AddListObject(L.Lists.NavaidVector, "CAS", 38, -9);
        m.U(M.NavaidElements + (uint)L.Lists.ObjectPointer, 0x7FFF0000);
        Assert.Contains("outside the module", Corrupt(m));
    }

    // PMDG keeps its vectors filled after a button goes off (live, KATL 2026-10-25: 441 fixes with WPT off) and
    // reading them all cost 90 ms a reading on the UI thread; it draws a list only while its button is on.
    [Fact]
    public void Lists_are_counted_but_not_read_while_their_buttons_are_off()
    {
        var m = new M();
        m.AddListObject(L.Lists.NavaidVector, "CAS", 38, -9);
        m.AddListObject(L.Lists.FixVector, "ABCDE", 38.5, -9.5);
        m.AddListObject(L.Lists.AirportVector, "LPPT", 38.78, -9.14);
        // An element PMDG does not draw is not read: a junk pointer there is never seen.
        m.U(M.FixElements + (uint)L.Lists.ObjectPointer, 0x7FFF0000);

        var x = Read(m);
        Assert.Equal((1, 1, 1), (x.NavaidCount, x.FixCount, x.AirportCount));
        Assert.Empty(x.Objects);
    }

    [Theory]
    [InlineData(NdObjectKind.Navaid)]
    [InlineData(NdObjectKind.Fix)]
    [InlineData(NdObjectKind.Airport)]
    public void Each_button_reads_its_own_list(NdObjectKind kind)
    {
        var m = new M();
        m.AddListObject(L.Lists.NavaidVector, "CAS", 38, -9);
        m.AddListObject(L.Lists.FixVector, "ABCDE", 38.5, -9.5);
        m.AddListObject(L.Lists.AirportVector, "LPPT", 38.78, -9.14);
        var button = kind switch { NdObjectKind.Navaid => L.Panel.ShowSta, NdObjectKind.Fix => L.Panel.ShowWpt, _ => L.Panel.ShowArpt };
        m.B(M.At(M.Panel, button), true);

        Assert.Equal(new[] { kind }, Read(m).Objects.Select(o => o.Kind));
    }

    [Fact]
    public void Fixes_are_not_read_beyond_their_range()
    {
        var m = new M();
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        m.AddListObject(L.Lists.FixVector, "ABCDE", 38.5, -9.5);
        int beyond = L.Geometry.SelectedRangesNm.ToList().FindIndex(r => r > L.Lists.FixesUpTo);
        m.I(M.At(M.Panel, L.Panel.Range), beyond);
        Assert.Empty(Read(m).Objects);

        m.I(M.At(M.Panel, L.Panel.Range), beyond - 1);
        Assert.Single(Read(m).Objects);
    }

    private static void TrafficList(M m, int count)
    {
        var t = L.Traffic;
        m.U(M.Tcas + (uint)t.List, M.TrafficList);
        m.B(M.TrafficList + (uint)t.ListShown, true);
        m.Bytes[M.TrafficList + (uint)t.Count] = (byte)count;
        for (int k = 0; k < count; k++) m.U(M.TrafficList + (uint)(t.Targets + 4 * k), M.TrafficList + 0x1000 + (uint)(0x40 * k));
    }

    [Fact]
    public void Reads_the_present_traffic_and_its_gates()
    {
        var m = new M();
        var t = L.Traffic;
        TrafficList(m, 2);
        uint a = M.TrafficList + 0x1000, b = a + 0x40;
        m.F(a + (uint)t.RelativeAltitude, 1200);
        m.F(a + (uint)t.Range, 6.2f);
        m.F(a + (uint)t.Bearing, 0.5f);
        m.Bytes[a + (uint)t.ThreatAndTrend] = (2 << 4) | 0x0F; // a TA, descending (-1)
        m.B(a + (uint)t.Present, true);
        m.B(b + (uint)t.Present, false);                        // not present: PMDG's first skip
        m.B(M.At(M.Tcas, t.Powered), true);
        m.I(M.At(M.Options, t.FilterRange), 40);
        m.I(M.At(M.Options, t.FilterSeparation), 2700);

        var x = Read(m);
        Assert.Equal(new NdTraffic(1200, 6.2f, 0.5f, 2, -1, 0), Assert.Single(x.Traffic));
        Assert.Equal(new NdTrafficState(true, true, false, false, 0, false, false, 40, 2700), x.TrafficState);
    }

    [Fact]
    public void A_target_s_index_counts_the_targets_not_present()
    {
        // PMDG's traffic count runs over every entry, skipped ones too, and picks a target's place in the TCAS test.
        var m = new M();
        var t = L.Traffic;
        TrafficList(m, 3);
        uint a = M.TrafficList + 0x1000, b = a + 0x40, c = b + 0x40;
        m.B(a + (uint)t.Present, false);
        m.B(b + (uint)t.Present, true);
        m.B(c + (uint)t.Present, true);
        m.B(M.At(M.Tcas, t.Powered), true);
        Assert.Equal(new[] { 1, 2 }, Read(m).Traffic.Select(x => x.Index));
    }

    [Fact]
    public void A_short_traffic_list_at_the_end_of_memory_is_read()
    {
        // PMDG reads the target pointers up to the list's count: a short list ending at the end of
        // committed memory is valid, though room for the most targets would not fit.
        var m = new M();
        var t = L.Traffic;
        uint list = (uint)(M.Size - (t.Targets + 4));
        m.U(M.Tcas + (uint)t.List, list);
        m.B(list + (uint)t.ListShown, true);
        m.Bytes[list + (uint)t.Count] = 1;
        uint a = M.TrafficList + 0x1000;
        m.U(list + (uint)t.Targets, a);
        m.F(a + (uint)t.Range, 6.2f);
        m.B(a + (uint)t.Present, true);
        Assert.Equal(6.2f, Assert.Single(Read(m).Traffic).RangeNm);
    }

    [Fact]
    public void Traffic_count_over_the_cap_discards_the_reading()
    {
        var m = new M();
        TrafficList(m, L.Traffic.MaxTargets + 1);
        Assert.Contains("targets", Corrupt(m));
    }

    [Fact]
    public void Reads_the_receivers_tuned_stations_and_their_gates()
    {
        var m = new M();
        var tu = L.Tuned;
        var rx = L.Receivers;
        uint vor0 = M.Sim + (uint)tu.Stations;
        m.Text(vor0 + (uint)tu.Ident, "CAS");
        m.Text(vor0 + (uint)rx.VorFrequencyText, "114.30");
        m.I(vor0 + (uint)tu.State, tu.TunedState);
        m.Position(vor0 + (uint)tu.Position, 38.8, -9.4);
        m.F(vor0 + (uint)tu.DmeDistance, 23.4f);
        m.D(M.At(M.Glass, tu.Course), 345);
        m.B(M.At(M.Misc, rx.Powered), true);
        m.B(M.At(M.Glass, rx.DmeFailed, 1), true);
        m.D(M.At(M.Glass, rx.Dme), 23.4);
        m.I(M.At(M.Glass, rx.Switch, 1), rx.AdfPosition);
        uint adf1 = M.Sim + (uint)(rx.AdfStations + rx.AdfStride);
        m.Text(adf1 + (uint)rx.AdfIdent, "FTM");
        m.I(adf1 + (uint)rx.AdfValid, 1);

        var x = Read(m);
        Assert.Equal(new NdTunedStation("CAS", x.Tuned[0].Position, tu.TunedState, 23.4f, 345), x.Tuned[0]);
        Assert.Equal(38.8, Deg(x.Tuned[0].Position!.Value.LatitudeRad), 6);
        Assert.Null(x.Tuned[1].Position);
        Assert.Equal(new NdReceiver(rx.VorPosition, "CAS", "114.30", tu.TunedState, true, false, false, 23.4, "", "", 0, false), x.Receivers[0]);
        Assert.Equal((rx.AdfPosition, "FTM", 1, true, false), (x.Receivers[1].Switch, x.Receivers[1].AdfIdent, x.Receivers[1].AdfValid, x.Receivers[1].DmeFailed, x.Receivers[1].Powered));
    }

    private static void FixPageEntry(M m, bool valid = true)
    {
        var f = L.FixPage;
        uint page = M.FixPageArea, entry = page + 0x400, fix = page + 0x800;
        m.U(M.Cdu + (uint)(f.Pages + 4 * f.FixPage), page);
        m.U(page + (uint)f.Entries, entry);
        m.U(entry + (uint)f.FixPointer, fix);
        m.Text(fix, "EMPAD");
        m.Position(fix + (uint)f.ObjectPosition, 38.86, -9.02, valid);
        m.I(fix + (uint)f.ObjectType, f.FixType);
        for (int k = 0; k < f.RingCount; k++) m.D(entry + (uint)(f.Rings + 8 * k), k == 0 ? 10 : f.InvalidAtOrBelow);
        for (int k = 0; k < f.RadialCount; k++) m.D(entry + (uint)(f.Radials + 8 * k), k == 0 ? 90 : f.InvalidAtOrBelow);
    }

    [Fact]
    public void A_fix_page_entry_without_a_valid_position_is_left_out()
    {
        // PMDG places the fix without testing its position: the reader cannot place it, so leaves it
        // out rather than discard the whole reading.
        var m = new M();
        FixPageEntry(m, valid: false);
        Assert.Empty(Read(m).FixPage);
    }

    [Fact]
    public void Reads_the_cdu_fix_page()
    {
        var m = new M();
        var f = L.FixPage;
        FixPageEntry(m);

        var ring = Assert.Single(Read(m).FixPage);
        Assert.Equal(("EMPAD", f.FixType), (ring.Name, ring.Type));
        Assert.Equal(new[] { 10, f.InvalidAtOrBelow, f.InvalidAtOrBelow }, ring.RingsNm);
        Assert.Equal(new[] { 90, f.InvalidAtOrBelow, f.InvalidAtOrBelow }, ring.RadialsDeg);
    }

    [Fact]
    public void Reads_the_markers_and_the_pln_centre()
    {
        var m = new M();
        var mk = R.Markers;
        int k = mk.Labels.First(kv => kv.Value == "T/C").Key;
        m.Position(M.Plan(0) + (uint)(mk.LateralPath + mk.Array + k * mk.Stride), 39.0, -8.9);
        var pc = R.PlnCentre;
        m.I(M.Fms + (uint)pc.EditedRoute, 0);
        m.U(M.Plan(0) + (uint)(pc.RouteInfo + pc.CentreSlot), m.RouteNodes[2]);

        var x = Read(m);
        Assert.Equal("T/C", Assert.Single(x.Markers).Label);
        Assert.Equal("PT753", x.PlnCentreName);
        Assert.Equal(39.10, Deg(x.PlnCentre!.Value.LatitudeRad), 6);
    }

    [Fact]
    public void The_pln_centre_is_named_as_the_map_labels_it()
    {
        // Live at KATL (2026-10-25): the centre "(INTC)" is labelled "INTC" on the map; the owner asked for the
        // waypoint's name without the brackets, which is the map's own label (the node's map name).
        var m = new M();
        var pc = R.PlnCentre;
        m.I(M.Fms + (uint)pc.EditedRoute, 0);
        m.U(M.Plan(0) + (uint)(pc.RouteInfo + pc.CentreSlot), m.RouteNodes[2]);
        m.Text(m.RouteNodes[2] + (uint)R.NodeName, "(INTC)");
        m.Text(m.RouteNodes[2] + (uint)R.NodeMapName, "INTC");

        Assert.Equal("INTC", Read(m).PlnCentreName);
    }

    [Fact]
    public void Reads_each_message_conditions_source()
    {
        var m = new M();
        var off = L.Messages.Messages.First(x => x.Text == "TCAS OFF").AnyOf[0][0].Source;
        m.B(M.At(M.Glass, off.Field), true);

        var x = Read(m);
        Assert.Equal(1, x.ConditionValues[off]);
        Assert.All(L.Messages.Messages.Concat(L.Messages.MapOptionLabels).SelectMany(t => t.AnyOf.SelectMany(a => a))
            .Where(c => c.Source.Kind is NdSourceKind.Object or NdSourceKind.Panel), c => Assert.True(x.ConditionValues.ContainsKey(c.Source)));
    }

    // ---- lost memory ----

    [Fact]
    public void An_object_committed_after_start_is_read()
    {
        var m = new M();
        var low = m.Bytes.AsSpan(0, (int)M.Fms).ToArray();
        var high = m.Bytes.AsSpan((int)M.Fms).ToArray();
        var src = new FakeProcessMemory().Add(new Services.WasmMemory.MemoryRegion(M.Base, M.Base, M.Fms, true, true, true), low);
        using var sampler = m.Sampler(src);

        Assert.False(sampler.TryRead(out _, out var before));
        Assert.Null(before);
        src.Add(new Services.WasmMemory.MemoryRegion(M.Base + M.Fms, M.Base, (ulong)high.Length, true, true, true), high);
        Assert.True(sampler.TryRead(out var x, out var after), after);
        Assert.Equal(5, x.Route.Count);
    }

    [Fact]
    public void A_reading_asks_for_each_region_once()
    {
        // VirtualQueryEx costs ~200 µs on the sim (live, 2026-10-25): asked for every field and object, a
        // reading took 25 ms, 90 with the 441 fixes of WPT at KATL, on the UI thread.
        var m = new M();
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        for (int k = 0; k < 50; k++) m.AddListObject(L.Lists.FixVector, $"F{k:000}", 38 + k * 0.001, -9);
        var src = m.Source();
        using var sampler = m.Sampler(src);

        Assert.True(sampler.TryRead(out _, out var corrupt), corrupt);
        Assert.Equal(1, src.RegionQueries);
        Assert.True(sampler.TryRead(out _, out corrupt), corrupt);
        Assert.Equal(2, src.RegionQueries); // asked again by the next reading
    }

    [Fact]
    public void Lost_memory_is_false_without_corrupt()
    {
        var m = new M();
        m.U(L.Objects.Efis.Global, 0);
        using var sampler = m.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.Null(corrupt);
    }
}
