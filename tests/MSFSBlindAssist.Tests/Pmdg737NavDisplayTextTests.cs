// The navigation display's list rows: one row per region, top to bottom, each starting with where it sits
// and carrying the ND's own printed text, listing on the map only what PMDG draws there at this mode, range
// and CTR. The live reading of 2026-10-25 (the -800 at LPPT on the ground, MAP, range 20, track 079.4,
// variation -1.5, EMPAD the TO waypoint 8.1 NM away at 335° true, 6.8 NM along the departure) gives the
// rows below; the expected positions are worked from the places the tests put things, not from the code.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using T = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayText;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayTextTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;
    private static NdRouteFields R => L.Route;

    // ---- the memory ----

    /// <summary>The live reading: LPPT the origin (a kind PMDG does not draw), PT753, IDBID and ORVED 26, 50
    /// and 70 NM away at 040° true, both bottom-corner switches OFF, TCAS OFF (in STBY on the ground).</summary>
    internal static M LiveMemory()
    {
        var m = new M();
        m.I(m.RouteNodes[0] + (uint)R.Validity.Kind, R.Validity.UndrawnKind);
        Place(m, m.RouteNodes[2], 26, 40);
        Place(m, m.RouteNodes[3], 50, 40);
        Place(m, m.RouteNodes[4], 70, 40);

        for (int k = 0; k < 2; k++) m.I(SwitchAt(k), 1);
        SetCondition(m, "TCAS OFF");
        return m;
    }

    private static (double Lat, double Lon) From(double nm, double bearingTrueDeg) =>
        M.Destination(M.AircraftLatitude, M.AircraftLongitude, nm, bearingTrueDeg);

    private static void Place(M m, uint node, double nm, double bearingTrueDeg)
    {
        var (lat, lon) = From(nm, bearingTrueDeg);
        m.Position(node + (uint)R.NodePosition, lat, lon);
    }

    /// <summary>Straight ahead: the top of the map is the track 079.4 magnetic, 077.9 true.</summary>
    private const double Ahead = 77.9;

    private static uint SwitchAt(int k) => M.Glass + (uint)(L.Receivers.Switch.Offset + L.Receivers.Switch.Stride * k);

    private static void SetCondition(M m, string text) => m.SetCondition(text);

    private static NavDisplaySample Sample(M m) => m.Read();

    private static IReadOnlyList<string> Rows(M m) => T.Lines(Sample(m), L);

    // ---- the live reading ----

    [Fact]
    public void Live_ground_reading_rows()
    {
        Assert.Equal(new[]
        {
            "Captain ND: MAP, range 20",
            "Top left: GS 0, TAS ---, wind ---°/---",
            "Top centre: TRK 079 MAG",
            "Top right: EMPAD, 1533.3Z, 6.8 NM",
            "Map: half-range ring 10",
            // The track line up the track-up map, its marks at a quarter, half and three quarters of the 20 NM.
            "Map: track line, straight ahead, marks at 5, 10 and 15 NM",
            "Map: EMPAD, 8.1 NM, 103° left",
            // EMPAD (on the map, beside and behind the aircraft) to PT753 (26 NM, 38° left) leaves by the arc's
            // left part, at about (42, 360) on the screen: nearest the left edge.
            "Map: route goes off the left edge, toward PT753, 26.0 NM, 38° left",
            // The MCP heading 080 against the track 079.4, and the dashed line to it (drawn with no AP roll mode).
            "Map: heading bug, 1° right",
            "Map: dashed line to the heading bug, 1° right",
            "Bottom left: FMC L",
            "Message: TCAS OFF",
        }, Rows(LiveMemory()));
    }

    /// <summary>PMDG's label at the bottom left in MAP and PLN while the map is valid: the FMC the unit's DEU
    /// shows, "FMC L" for FMC 0, "FMC R" otherwise (seen live in every MAP and PLN capture: FMC L).</summary>
    [Fact]
    public void Fmc_label_names_the_fmc_the_unit_shows()
    {
        var m = LiveMemory();
        Assert.Contains("Bottom left: FMC L", Rows(m));
        // Unit 0 (the Captain's full ND) on DEU 1: FMC 1.
        m.I(M.Efis + (uint)L.Units.Deu, 1);
        Assert.Contains("Bottom left: FMC R", Rows(m));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        Assert.Contains("Bottom left: FMC R", Rows(m));
        // Not in VOR; not while the map is invalid.
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Vor);
        Assert.DoesNotContain("Bottom left: FMC R", Rows(m));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Map);
        m.B(M.Fms + (uint)L.MapValidity.FmcValid + 1, false);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Bottom left: FMC", StringComparison.Ordinal));
    }

    // ---- the route line's pieces ----

    /// <summary>A node used only as a position for a line's end.</summary>
    private static uint Spot(M m, int slot, double nm, double bearingTrueDeg)
    {
        uint node = m.AddNode(slot, "SPOT", 0, null, 0, 0);
        Place(m, node, nm, bearingTrueDeg);
        return node;
    }

    [Fact]
    public void No_route_edge_across_a_discontinuity()
    {
        // Live at KATL (2026-10-25): with GRABA deleted the MOD's line ended at HOYYT's discontinuity, and the row
        // still said it went off the right edge toward PADGT. PMDG starts the point after a discontinuity near the
        // next waypoint and draws nothing across the gap.
        var m = LiveMemory();
        uint leadIn = Spot(m, 30, 24, Ahead - 38);
        m.SetPathPoint(0, 1, (leadIn, m.RouteNodes[2]), m.RouteNodes[2]);
        var rows = Rows(m);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left", rows);
        Assert.DoesNotContain(rows, r => r.Contains("route goes off"));
    }

    [Fact]
    public void A_lead_in_line_that_leaves_the_map_is_listed()
    {
        var m = LiveMemory();
        uint leadIn = Spot(m, 30, 5, Ahead);
        m.SetPathPoint(0, 1, (leadIn, m.RouteNodes[2]), m.RouteNodes[2]);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: route goes off the ", StringComparison.Ordinal) && r.EndsWith(" edge, toward PT753, 26.0 NM, 38° left", StringComparison.Ordinal));
    }

    [Fact]
    public void A_turn_joins_the_lines_around_it()
    {
        // PMDG's shape for a turn: the turn is a point of its own with
        // the arc and no line, and the line after it is on the next point, from the arc's end. Point 1's line ends
        // 5 NM ahead (on the map), point 3's starts 30 NM ahead (off it): the turn between them joins them, and
        // that piece leaves the map by its top.
        var m = LiveMemory();
        uint near = Spot(m, 30, 5, Ahead), far = Spot(m, 31, 30, Ahead), farther = Spot(m, 32, 35, Ahead);
        m.SetPathPoint(0, 1, (m.RouteNodes[1], near), m.RouteNodes[2]);
        m.SetPathPoint(0, 2, null, m.RouteNodes[2]);
        m.SetPathPoint(0, 3, (far, farther), m.RouteNodes[3]);
        Assert.DoesNotContain(Rows(m), r => r.Contains("route goes off"));

        m.SetPathPoint(0, 2, null, m.RouteNodes[2], turn: true);
        Assert.Contains("Map: route goes off the top edge, toward IDBID, 50.0 NM, 38° left", Rows(m));
    }

    [Fact]
    public void A_turn_then_a_point_drawing_nothing_does_not_join()
    {
        // A turn's point followed by a discontinuity: the arc, a point with no line,
        // then the line after the gap. PMDG draws nothing across the gap.
        var m = LiveMemory();
        uint near = Spot(m, 30, 5, Ahead), far = Spot(m, 31, 30, Ahead), farther = Spot(m, 32, 35, Ahead);
        m.SetPathPoint(0, 1, (m.RouteNodes[1], near), m.RouteNodes[2]);
        m.SetPathPoint(0, 2, null, m.RouteNodes[2], turn: true);
        m.SetPathPoint(0, 3, null, m.RouteNodes[3]);
        m.SetPathPoint(0, 4, (far, farther), m.RouteNodes[4]);
        Assert.DoesNotContain(Rows(m), r => r.Contains("route goes off"));
    }

    [Fact]
    public void A_turn_drawn_on_the_point_of_its_line_joins_too()
    {
        // Not the shape PMDG builds; the join does not depend on which point holds the arc.
        var m = LiveMemory();
        uint near = Spot(m, 30, 5, Ahead), far = Spot(m, 31, 30, Ahead), farther = Spot(m, 32, 35, Ahead);
        m.SetPathPoint(0, 1, (m.RouteNodes[1], near), m.RouteNodes[2]);
        m.SetPathPoint(0, 2, (far, farther), m.RouteNodes[3], turn: true);
        Assert.Contains("Map: route goes off the top edge, toward IDBID, 50.0 NM, 38° left", Rows(m));
    }

    [Fact]
    public void Centred_map_lists_empad_with_its_position()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        var rows = Rows(m);
        Assert.Equal("Captain ND: MAP, range 20, centred", rows[0]);
        // Centred, the ring PMDG labels is half the compass ring's 10 NM.
        Assert.Contains("Map: half-range ring 5", rows);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left", rows);
    }

    [Fact]
    public void Vsd_on_is_named_in_the_header()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        m.B(M.At(M.Panel, L.Panel.ShowVsd), true);
        Assert.Equal("Captain ND: MAP, range 20, VSD on", Rows(m)[0]);
    }

    [Fact]
    public void Pln_rows_are_compass_words_from_the_centre()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        var empad = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 8.1, 335);
        var pt753 = M.Destination(empad.LatitudeDeg, empad.LongitudeDeg, 5, 315);
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, pt753.LatitudeDeg, pt753.LongitudeDeg);
        m.U(M.Plan(0) + (uint)(R.PlnCentre.RouteInfo + R.PlnCentre.CentreSlot), m.RouteNodes[1]);

        var rows = Rows(m);
        Assert.Equal("Captain ND: PLN, range 20, centred on EMPAD", rows[0]);
        // No TRK readout in PLN: PMDG's compass "N" with the arrow's lines beside it (written ↑) takes its place.
        Assert.Equal("Top centre: N↑", rows[2]);
        Assert.Single(rows, r => r.StartsWith("Top centre:", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: half-range ring", StringComparison.Ordinal));
        Assert.Contains("Top right: EMPAD, 1533.3Z, 6.8 NM", rows);
        Assert.Contains("Map: EMPAD, at the centre", rows);
        Assert.Contains("Map: PT753, 5.0 NM north-west of EMPAD", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: heading bug", StringComparison.Ordinal));

        // PLN's other compass letters where they sit: W at the left edge, E at the right edge (both at the
        // centre's height, so after the map's rows on the left and before the right side's), S at the bottom centre.
        int Row(string text) => rows.ToList().IndexOf(text);
        Assert.True(Row("Left side: W") > Row("Top right: EMPAD, 1533.3Z, 6.8 NM"));
        Assert.True(Row("Map: EMPAD, at the centre") > Row("Left side: W"));
        Assert.True(Row("Right side: E") > Row("Map: PT753, 5.0 NM north-west of EMPAD"));
        Assert.True(Row("Bottom centre: S") > Row("Right side: E"));
        Assert.Equal(Row("Bottom centre: S") + 1, Row("Message: TCAS OFF"));
    }

    /// <summary>PLN centred on a waypoint: PMDG draws the aircraft symbol at the FMS position while the
    /// map is valid, the position valid and on the screen and its latitude within the file's limit (live,
    /// 2026-10-28: about 7.5 NM south-south-east of PADGT); the list names its place from the centre.</summary>
    [Fact]
    public void Pln_lists_the_aircraft_off_the_centre_waypoint()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        var north = From(5, 0);
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, north.Lat, north.Lon);
        m.U(M.Plan(0) + (uint)(R.PlnCentre.RouteInfo + R.PlnCentre.CentreSlot), m.RouteNodes[2]);
        Assert.Contains("Map: aircraft, 5.0 NM south of PT753", Rows(m));

        // Beyond the latitude limit, none.
        m.Position(M.Fms + (uint)R.FmsPosition, 83, 0);
        var farNorth = M.Destination(83, 0, 5, 0);
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, farNorth.LatitudeDeg, farNorth.LongitudeDeg);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: aircraft", StringComparison.Ordinal));

        // No FMS position for the Captain's FMC: the map is invalid, none either.
        m.Position(M.Fms + (uint)R.FmsPosition, M.AircraftLatitude, M.AircraftLongitude);
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, north.Lat, north.Lon);
        Assert.Contains("Map: aircraft, 5.0 NM south of PT753", Rows(m));
        m.B(M.Fms + (uint)L.MapValidity.FmcValid, false);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: aircraft", StringComparison.Ordinal));
    }

    [Fact]
    public void Pln_west_follows_the_map_option_labels()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        var rows = Rows(m).ToList();
        Assert.Equal(rows.IndexOf("Left side: WPT") + 1, rows.IndexOf("Left side: W"));
    }

    [Fact]
    public void No_compass_letters_outside_pln()
    {
        var rows = Rows(LiveMemory());
        Assert.DoesNotContain(rows, r => r is "Left side: W" or "Right side: E" or "Bottom centre: S");
    }

    [Fact]
    public void Pln_without_a_step_waypoint_is_centred_on_the_aircraft()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        var rows = Rows(m);
        Assert.Equal("Captain ND: PLN, range 20, centred on the aircraft", rows[0]);
        // EMPAD 8.1 NM at 335° true from the aircraft: north-north-west.
        Assert.Contains("Map: EMPAD, 8.1 NM north-north-west of the aircraft", rows);
    }

    [Fact]
    public void Data_on_adds_constraint_and_eta()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        var d = R.DataText;
        uint empad = m.RouteNodes[1];
        m.Bytes[empad + (uint)d.ConstraintType] = (byte)d.WindowType;
        m.I(empad + (uint)d.UpperAltitude, 12000);
        m.I(empad + (uint)d.LowerAltitude, 3000);
        m.I(empad + (uint)d.ThirdLine, 250);
        // In the climb (phase 0) the transition altitude is 4000: 12000 prints as a flight level.
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left; FL120B, 3000A, 250, 1533Z", Rows(m));
    }

    /// <summary>DATA off, PMDG's ND_ActWptData option set: PMDG still prints the constraint text of the plan's
    /// key node +28 (live, 2026-10-28: HRMBE, the TO waypoint, with "FL210B" and "16000A"), never its ETA, and none
    /// for the other waypoints.</summary>
    [Fact]
    public void The_active_waypoint_s_constraint_shows_without_data()
    {
        var m = LiveMemory();
        var d = R.DataText;
        uint empad = m.RouteNodes[1];
        m.Bytes[empad + (uint)d.ConstraintType] = (byte)d.WindowType;
        m.I(empad + (uint)d.UpperAltitude, 12000);
        m.I(empad + (uint)d.LowerAltitude, 3000);
        m.U(M.Keys(0) + (uint)d.ActiveSlot, empad);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left", Rows(m));

        m.B(M.At(M.Options, d.ActiveOption), true);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left; FL120B, 3000A", Rows(m));
        // Another node in the slot: EMPAD's text goes.
        m.U(M.Keys(0) + (uint)d.ActiveSlot, m.RouteNodes[2]);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left", Rows(m));
    }

    [Fact]
    public void Data_on_with_the_eta_hidden_prints_the_constraint_alone()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        var d = R.DataText;
        uint empad = m.RouteNodes[1];
        m.Bytes[empad + (uint)d.ConstraintType] = (byte)d.AtType;
        m.I(empad + (uint)d.AtAltitude, 3000);
        m.B(empad + (uint)d.EtaHidden, true);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left; 3000", Rows(m));
    }

    [Fact]
    public void No_active_route_prints_dashes_top_right()
    {
        var m = LiveMemory();
        m.SetKeys(0, 0, 0);
        var s = Sample(m);
        Assert.Null(T.TopRight(s, L));
        Assert.Contains("Top right: ------Z, --- NM", T.Lines(s, L));
    }

    [Fact]
    public void Vector_leg_prints_dashes()
    {
        var m = LiveMemory();
        m.I(m.RouteNodes[1] + (uint)R.NodeType, R.NoDataLegType);
        var s = Sample(m);
        Assert.Equal("EMPAD, ------Z, --- NM", T.TopRight(s, L));
        Assert.Contains("Top right: EMPAD, ------Z, --- NM", T.Lines(s, L));
    }

    [Fact]
    public void Pseudo_nodes_are_skipped()
    {
        var m = LiveMemory();
        uint pseudo = m.AddNode(10, "@P1", 1.0, null, 0, 0);
        Place(m, pseudo, 1, Ahead);
        m.Chain(new[] { m.RouteNodes[0], pseudo, m.RouteNodes[1], m.RouteNodes[2], m.RouteNodes[3], m.RouteNodes[4] });
        var rows = Rows(m);
        Assert.DoesNotContain(rows, r => r.Contains("@P1", StringComparison.Ordinal));
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left", rows);
    }

    [Fact]
    public void Not_on_any_display_is_the_header_alone()
    {
        var m = LiveMemory();
        var u = L.Units;
        m.I(M.Efis + (uint)u.PageOffset, 2);
        Assert.Equal(new[] { "Captain ND: not on any display" }, Rows(m));
    }

    [Fact]
    public void Dark_is_off()
    {
        var m = LiveMemory();
        var u = L.Units;
        for (int k = 0; k < u.UnitCount; k++) m.I(M.Efis + (uint)(k * u.UnitStride + u.PageOffset), u.DarkPages[0]);
        Assert.Equal(new[] { "Captain ND: off" }, Rows(m));
    }

    [Fact]
    public void Top_centre_says_HDG_when_degraded()
    {
        var m = LiveMemory();
        m.I(M.At(M.Iru, L.Orientation.IruCondition), L.Orientation.DegradedIruCondition);
        var rows = Rows(m);
        Assert.Contains("Top centre: HDG 079 MAG", rows);
        // PMDG prints the ground speed only in the IRU's NAV condition.
        Assert.Contains("Top left: GS ---, TAS ---, wind ---°/---", rows);
    }

    [Fact]
    public void A_tuned_vor_is_on_the_map_only_while_its_receiver_is_powered()
    {
        // PMDG draws a tuned station while its tuned flag is set: the systems object's power byte and the
        // tuned state.
        var m = LiveMemory();
        var tu = L.Tuned;
        uint vor = M.Sim + (uint)tu.Stations;
        m.Text(vor + (uint)tu.Ident, "CAS");
        m.I(vor + (uint)tu.State, tu.TunedState);
        var at = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 5, 78.4);
        m.Position(vor + (uint)tu.Position, at.LatitudeDeg, at.LongitudeDeg);
        m.B(M.At(M.Misc, tu.Powered), true);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: VOR 1 CAS, 5.0 NM", StringComparison.Ordinal));
        m.B(M.At(M.Misc, tu.Powered), false);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: VOR 1 CAS", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_is_heading_up_with_the_track_up_option_clear()
    {
        // A crab: heading 075, track 079.4. With PMDG's option set, MAP is track-up; clear, heading-up,
        // and the heading bug (selected 109) is measured off the heading.
        var m = LiveMemory();
        m.D(M.At(M.Glass, L.Orientation.Heading), 75);
        m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 109);
        Assert.Contains("Top centre: TRK 079 MAG", Rows(m));
        Assert.Contains("Map: heading bug, 30° right", Rows(m));
        m.B(M.At(M.Options, L.Orientation.TrackUpOption), false);
        Assert.Contains("Top centre: HDG 075 MAG", Rows(m));
        Assert.Contains("Map: heading bug, 34° right", Rows(m));
    }

    /// <summary>The readout valid but the Captain's FMC without an FMS position: PMDG's map-invalid flag is set, so
    /// PMDG draws the amber MAP flag and the top right's dashes, and no route, map-option label or list (each
    /// tests that flag); the heading bug and the top centre's number test the readout alone.</summary>
    [Fact]
    public void No_fms_position_draws_the_map_flag_and_no_route()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowArpt), true);
        var airport = From(6, Ahead);
        m.AddListObject(L.Lists.AirportVector, "LPCS", airport.Lat, airport.Lon);
        Assert.Contains("Map: airport LPCS, 6.0 NM, ahead", Rows(m));

        m.B(M.Fms + (uint)L.MapValidity.FmcValid, false);
        var s = Sample(m);
        Assert.Null(T.TopRight(s, L));
        Assert.Equal(new[]
        {
            "Captain ND: MAP, range 20",
            "Top left: GS 0, TAS ---, wind ---°/---",
            "Top centre: TRK 079 MAG",
            "Top right: ------Z, --- NM",
            "Map: half-range ring 10",
            // The track line tests the track alone.
            "Map: track line, straight ahead, marks at 5, 10 and 15 NM",
            "Map: heading bug, 1° right",
            "Map: dashed line to the heading bug, 1° right",
            "Message: TCAS OFF",
            "Message: MAP",
        }, T.Lines(s, L));
    }

    [Fact]
    public void Top_centre_says_TRU_with_true_north_and_dashes_while_invalid()
    {
        var m = LiveMemory();
        m.B(M.At(M.Efis, L.Orientation.Magnetic), false);
        Assert.Contains("Top centre: TRK 078 TRU", Rows(m));
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.Contains("Top centre: TRK --- TRU", Rows(m));
    }

    [Fact]
    public void Modified_route_items_name_their_route()
    {
        var m = LiveMemory();
        uint xray = m.AddNode(20, "XRAY", 2.0, null, 0, 0);
        Place(m, xray, 2, Ahead);
        m.Chain(new[] { xray });
        m.SetKeys(R.ModOffset, xray, xray);
        m.B(M.Plan(R.ModOffset) + (uint)R.PlanActive, true);
        var rows = Rows(m);
        int empad = rows.ToList().IndexOf("Map: EMPAD, 8.1 NM, 103° left");
        int xrayRow = rows.ToList().IndexOf("Map: modified route: XRAY, 2.0 NM, ahead");
        Assert.True(empad >= 0 && xrayRow > empad, string.Join(" | ", rows));
    }

    [Fact]
    public void The_modified_route_has_no_data_text()
    {
        // PMDG prints no DATA text for the MOD plans (2 and 3): their waypoints show alone.
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        var d = R.DataText;
        uint xray = m.AddNode(20, "XRAY", 2.0, new NdClock(15, 40, 0), 0, 0);
        Place(m, xray, 2, Ahead);
        m.Bytes[xray + (uint)d.ConstraintType] = (byte)d.AtType;
        m.I(xray + (uint)d.AtAltitude, 3000);
        m.Chain(new[] { xray });
        m.SetKeys(R.ModOffset, xray, xray);
        m.B(M.Plan(R.ModOffset) + (uint)R.PlanActive, true);
        var rows = Rows(m);
        Assert.Contains("Map: modified route: XRAY, 2.0 NM, ahead", rows);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left; 1533Z", rows);
    }

    [Fact]
    public void Route_2_keeps_its_data_text()
    {
        // With RTE 2 the reference route (plan 1) is no MOD plan: its waypoints keep their DATA text.
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        m.I(M.Fms + (uint)R.RefRoute, 1);
        m.SetKeys(1, m.RouteNodes[0], m.RouteNodes[1]);
        Assert.Contains("Map: EMPAD, 8.1 NM, 103° left; 1533Z", Rows(m));
    }

    [Fact]
    public void Heading_bug_beyond_the_screen_has_no_row()
    {
        var m = LiveMemory();
        m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 109);
        Assert.Contains("Map: heading bug, 30° right", Rows(m));
        m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 200);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: heading bug", StringComparison.Ordinal));
    }

    [Fact]
    public void Receiver_corner_rows()
    {
        var m = LiveMemory();
        var tu = L.Tuned;
        var rx = L.Receivers;
        uint vor = M.Sim + (uint)tu.Stations;
        m.Text(vor + (uint)tu.Ident, "CAG");
        m.I(vor + (uint)tu.State, 1);
        m.B(M.Misc + (uint)rx.Powered.Offset, true);
        m.D(M.Glass + (uint)rx.Dme.Offset, 23.4);
        m.I(SwitchAt(0), 0);
        uint adf = M.Sim + (uint)(rx.AdfStations + rx.AdfStride);
        m.Text(adf + (uint)rx.AdfFrequencyText, " 353.0");
        m.I(adf + (uint)rx.AdfValid, 1);
        m.I(SwitchAt(1), 2);

        var rows = Rows(m);
        Assert.Contains("Bottom left: VOR 1, CAG, DME 23.4", rows);
        Assert.Contains("Bottom right: ADF 2, 353.0", rows);

        // A failed receiver: PMDG draws its label in amber inside a box, and no value.
        m.B(M.Glass + (uint)rx.VorFailed.Offset, true);
        m.B(M.Glass + (uint)rx.DmeFailed.Offset, true);
        m.B(M.Glass + (uint)(rx.AdfFailed.Offset + rx.AdfFailed.Stride), true);
        rows = Rows(m);
        Assert.Contains("Bottom left: VOR 1 (amber, boxed), DME (amber, boxed)", rows);
        Assert.Contains("Bottom right: ADF 2 (amber, boxed)", rows);

        // A PMDG build drawing no box says amber alone.
        var unboxed = L with { Receivers = rx with { VorFailedBoxed = false, DmeFailedBoxed = false, AdfFailedBoxed = false } };
        rows = T.Lines(Sample(m), unboxed);
        Assert.Contains("Bottom left: VOR 1 (amber), DME (amber)", rows);
        Assert.Contains("Bottom right: ADF 2 (amber)", rows);
    }

    [Fact]
    public void An_ils_tuned_prints_dashes_for_the_ident_and_the_dme()
    {
        var m = LiveMemory();
        var tu = L.Tuned;
        uint vor = M.Sim + (uint)tu.Stations;
        m.Text(vor + (uint)tu.Ident, "IHZK");
        m.I(vor + (uint)tu.State, 4);
        m.B(M.Misc + (uint)L.Receivers.Powered.Offset, true);
        m.I(SwitchAt(0), 0);
        // Live (ground power, 2026-10-25): an ILS tuned (state 4) prints dashes for the ident and the DME.
        Assert.Contains("Bottom left: VOR 1, ------, DME ---", Rows(m));
    }

    [Fact]
    public void App_mode_lists_its_block_and_the_rows_it_shares_with_map()
    {
        // PMDG's APP and VOR are heading-up (PMDG sets the track-up flag only in MAP), with no map, range
        // ring or top-right waypoint; the top right is the ILS/VOR block (nothing tuned: no ILS frequency).
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        Assert.Equal(new[]
        {
            "Captain ND: APP, range 20",
            "Top left: GS 0, TAS ---, wind ---°/---",
            "Top centre: HDG 079 MAG",
            "Top right: ILS 1, ------, CRS 000, DME 0.0",
            // The track line, heading-up, on the heading (no drift); its marks with no picture or traffic on.
            "Centre: track line, ahead, marks at 5, 10 and 15 NM",
            // The course 000 against the heading 079.
            "Centre: course pointer 000, 79° left",
            // The MCP heading 080 on the compass.
            "Centre: heading bug, 1° right",
            "Centre: dashed line to the heading bug, 1° right",
            // PMDG draws the glideslope's dots in APP whatever is tuned; the pointer needs the glideslope received.
            "Right side: glideslope scale, no pointer",
            "Message: TCAS OFF",
            // The memory's NAV 1 frequency (0) is no ILS one: in APP, PMDG says so.
            "Message: EFIS MODE/NAV FREQ DISAGREE",
        }, Rows(m));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Vor);
        Assert.DoesNotContain(Rows(m), r => r.Contains("not read", StringComparison.Ordinal));
    }

    [Fact]
    public void Objects_nearest_first_by_kind()
    {
        var m = LiveMemory();
        foreach (var f in new[] { L.Panel.ShowSta, L.Panel.ShowWpt, L.Panel.ShowArpt }) m.B(M.At(M.Panel, f), true);
        var li = L.Lists;
        var v1 = From(5, Ahead);
        var v2 = From(3, Ahead);
        var fix = From(4, Ahead);
        var airport = From(6, Ahead);
        m.AddListObject(li.NavaidVector, "VONE", v1.Lat, v1.Lon, flags: 8, @class: 1);
        m.AddListObject(li.NavaidVector, "VTWO", v2.Lat, v2.Lon, flags: 2, @class: 0);
        m.AddListObject(li.FixVector, "ABCDE", fix.Lat, fix.Lon);
        m.AddListObject(li.AirportVector, "LPCS", airport.Lat, airport.Lon);

        var rows = Rows(m).Where(r => r.StartsWith("Map: ", StringComparison.Ordinal) && r.Contains(" NM, ahead", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "Map: VOR VTWO, 3.0 NM, ahead", "Map: VOR/DME VONE, 5.0 NM, ahead", "Map: fix ABCDE, 4.0 NM, ahead", "Map: airport LPCS, 6.0 NM, ahead" }, rows);
        Assert.Contains("Left side: ARPT, WPT, STA", Rows(m));
    }

    [Fact]
    public void Navaids_pmdg_does_not_draw_are_neither_listed_nor_counted()
    {
        // The station flags as measured at KATL: 4 NDB, 1 ILS, 9 ILS/DME, 16 TACAN alone. PMDG draws
        // none of them (bit 2 or 8, and not bit 1), and counts none toward EXCESS DATA.
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowSta), true);
        var li = L.Lists;
        int max = li.MaxElements;
        int[] undrawn = { 4, 1, 1 | 8, 16 };
        for (int k = 0; k < max + 2; k++)
        {
            var p = From(1 + k * 0.05, Ahead);
            m.AddListObject(li.NavaidVector, $"U{k:00}", p.Lat, p.Lon, flags: undrawn[k % undrawn.Length], @class: 1);
        }
        var vor = From(3.5, Ahead);
        m.AddListObject(li.NavaidVector, "VONE", vor.Lat, vor.Lon, flags: 8, @class: 1);
        // A TACAN's bit with a drawn bit (a VORTAC): PMDG draws the symbol of the drawn bit, so the word is its.
        var vortac = From(4.5, Ahead);
        m.AddListObject(li.NavaidVector, "TONE", vortac.Lat, vortac.Lon, flags: 8 | 16, @class: 1);
        var vorTacan = From(5.5, Ahead);
        m.AddListObject(li.NavaidVector, "TTWO", vorTacan.Lat, vorTacan.Lon, flags: 2 | 16, @class: 1);

        var rows = Rows(m);
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: ", StringComparison.Ordinal) && r.Contains(" U", StringComparison.Ordinal));
        Assert.Contains("Map: VOR/DME VONE, 3.5 NM, ahead", rows);
        Assert.Contains("Map: VOR/DME TONE, 4.5 NM, ahead", rows);
        Assert.Contains("Map: VOR TTWO, 5.5 NM, ahead", rows);
        Assert.DoesNotContain("Message: EXCESS DATA", rows);
    }

    [Fact]
    public void Excess_data_when_more_than_pmdg_draws()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowSta), true);
        int max = L.Lists.MaxElements;
        for (int k = 0; k < max + 2; k++)
        {
            var p = From(1 + k * 0.1, Ahead);
            m.AddListObject(L.Lists.NavaidVector, $"N{k:00}", p.Lat, p.Lon, flags: 8, @class: 1);
        }
        var rows = Rows(m);
        // PMDG draws MaxElements + 1, then stops with EXCESS DATA.
        Assert.Equal(max + 1, rows.Count(r => r.StartsWith("Map: VOR/DME N", StringComparison.Ordinal)));
        Assert.Contains("Message: EXCESS DATA", rows);
    }

    [Fact]
    public void Fixes_are_not_drawn_beyond_their_range()
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        var fix = From(4, Ahead);
        m.AddListObject(L.Lists.FixVector, "ABCDE", fix.Lat, fix.Lon);
        Assert.Contains("Map: fix ABCDE, 4.0 NM, ahead", Rows(m));
        // Range index 4 is 80 NM: PMDG draws no fixes beyond 40.
        m.I(M.At(M.Panel, L.Panel.Range), 4);
        Assert.DoesNotContain(Rows(m), r => r.Contains("ABCDE", StringComparison.Ordinal));
    }

    /// <summary>One TCAS target: a traffic advisory (threat 2) climbing 1200 ft above, by default.</summary>
    private static void OneTarget(M m, float rangeNm, double bearingDeg, int threat = 2)
    {
        var t = L.Traffic;
        m.U(M.Tcas + (uint)t.List, M.TrafficList);
        m.B(M.TrafficList + (uint)t.ListShown, true);
        m.B(M.At(M.Tcas, t.Powered), true);
        m.Bytes[M.TrafficList + (uint)t.Count] = 1;
        uint target = M.TrafficList + 0x1000;
        m.U(M.TrafficList + (uint)t.Targets, target);
        m.F(target + (uint)t.RelativeAltitude, 1200);
        m.F(target + (uint)t.Range, rangeNm);
        m.F(target + (uint)t.Bearing, (float)(bearingDeg * Math.PI / 180));
        m.Bytes[target + (uint)t.ThreatAndTrend] = (byte)((threat << t.ThreatShift) | 1);
        m.B(target + (uint)t.Present, true);
    }

    [Fact]
    public void Traffic_rows_with_threat_words()
    {
        var m = LiveMemory();
        OneTarget(m, 6.2f, 30);

        var rows = Rows(m);
        Assert.Contains("Map: traffic advisory, 6.2 NM, 30° right, +12 climbing", rows);
        Assert.Contains("Message: TRAFFIC", rows);
    }

    [Fact]
    public void Traffic_is_listed_on_a_centred_map()
    {
        // The ND draws the traffic in MAP whether centred or not, in VOR and APP only not centred (live at FL250,
        // 2026-10-28: the "+14" diamond on the centred map).
        var m = LiveMemory();
        OneTarget(m, 6.2f, 30);
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: traffic advisory, 6.2 NM, ", StringComparison.Ordinal));
    }

    [Fact]
    public void Offscale_follows_the_tfc_gate()
    {
        // PMDG draws OFFSCALE under the TFC message's gate (TFC button on, and MAP or VOR/APP not centred), for a
        // TA or RA beyond 0.9 of the range: range 20 here, so 19 NM is offscale.
        var m = LiveMemory();
        OneTarget(m, 19f, 0);
        Assert.DoesNotContain("Message: OFFSCALE", Rows(m)); // TFC off
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        Assert.Contains("Message: OFFSCALE", Rows(m));
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Contains("Message: OFFSCALE", Rows(m)); // MAP, centred
    }

    [Fact]
    public void Offscale_also_for_traffic_pmdg_culls()
    {
        // PMDG sets OFFSCALE for a TA or RA its cull drops, not only beyond 0.9 of the range, and draws no culled
        // target: 16 NM behind at range 20 is 0.8 of the range, 560 units below the
        // centre, past the cull's 1010.
        var m = LiveMemory();
        OneTarget(m, 16f, 180);
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        var rows = Rows(m);
        Assert.Contains("Message: OFFSCALE", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: traffic advisory", StringComparison.Ordinal));
    }

    [Fact]
    public void Traffic_cut_by_the_map_s_edge_is_listed_at_the_edge()
    {
        // 18.5 NM at 45° left, range 20: 647 units out, past the arc's 635, inside the cull; the circle's inner edge
        // shows. Beyond 0.9 of the range, so OFFSCALE too.
        var m = LiveMemory();
        OneTarget(m, 18.5f, -45);
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        var rows = Rows(m);
        Assert.Contains("Map: traffic advisory, 18.5 NM, 45° left, +12 climbing, at the edge", rows);
        Assert.Contains("Message: OFFSCALE", rows);
    }

    /// <summary>The TCAS list in the self-test, its pattern showing, with <paramref name="count"/> entries.</summary>
    private static void TcasTest(M m, int count)
    {
        var t = L.Traffic;
        m.U(M.Tcas + (uint)t.List, M.TrafficList);
        m.B(M.TrafficList + (uint)t.ListShown, true);
        m.B(M.At(M.Tcas, t.Powered), true);
        m.Bytes[M.TrafficList + (uint)t.Count] = (byte)count;
        m.I(M.TrafficList + (uint)t.TestField, t.TestValue);
        m.B(M.At(M.Tcas, t.SelfTest), true);
        m.B(M.At(M.Tcas, t.SelfTestShown), true);
    }

    /// <summary>The list's entry <paramref name="k"/>: a present target at its own range and bearing.</summary>
    private static void Entry(M m, int k, float altitudeFt, int threat, int trend, float rangeNm, double bearingDeg)
    {
        var t = L.Traffic;
        uint target = M.TrafficList + 0x1000 + (uint)(0x40 * k);
        m.U(M.TrafficList + (uint)(t.Targets + 4 * k), target);
        m.F(target + (uint)t.RelativeAltitude, altitudeFt);
        m.F(target + (uint)t.Range, rangeNm);
        m.F(target + (uint)t.Bearing, (float)(bearingDeg * Math.PI / 180));
        m.Bytes[target + (uint)t.ThreatAndTrend] = (byte)((threat << t.ThreatShift) | (trend & 0x0F));
        m.B(target + (uint)t.Present, true);
    }

    private static List<string> TrafficRows(IReadOnlyList<string> rows) =>
        rows.Where(r => r.StartsWith("Map: ", StringComparison.Ordinal) && (r.Contains("traffic", StringComparison.Ordinal) || r.Contains("advisory", StringComparison.Ordinal))).ToList();

    [Fact]
    public void The_tcas_test_lists_its_four_symbols()
    {
        // In the test PMDG draws each target at a fixed place by its index in the list, whatever its own range and
        // bearing. Live 2026-10-28 (the flight's cap\tcastest\nd-194526): +10
        // hollow diamond ahead left, -10 and a down arrow filled diamond ahead right, -02 and an up arrow amber
        // circle left, +02 red square right. Their own 30 NM would be OFFSCALE at range 20: the test's places are not.
        var m = LiveMemory();
        TcasTest(m, 4);
        Entry(m, 0, 1000, 0, 0, 30, 100);
        Entry(m, 1, -1000, 1, -1, 30, 100);
        Entry(m, 2, -200, 2, 1, 30, 100);
        Entry(m, 3, 200, 3, 0, 30, 100);
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        var expected = new[]
        {
            "Map: traffic advisory, 2.0 NM, 90° left, -02 climbing",
            "Map: resolution advisory, 2.0 NM, 90° right, +02",
            "Map: other traffic, 3.6 NM, 34° left, +10",
            "Map: proximate traffic, 3.6 NM, 34° right, -10 descending",
        };
        var rows = Rows(m);
        Assert.Equal(expected, TrafficRows(rows));
        Assert.Contains("Message: TRAFFIC", rows);
        Assert.DoesNotContain("Message: OFFSCALE", rows);

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(expected, TrafficRows(Rows(m)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The test's first 0.8 s, before its pattern shows: nothing.
        m.B(M.At(M.Tcas, L.Traffic.SelfTestShown), false);
        Assert.Empty(TrafficRows(Rows(m)));
    }

    [Fact]
    public void A_tcas_test_entry_past_the_test_places_keeps_its_own_and_is_not_filtered()
    {
        // PMDG has places for indexes 0 to 3; entry 4 stays where it is. The filter for other traffic is skipped in
        // the test: 5000 ft above is outside the 2700 ft separation.
        var m = LiveMemory();
        var t = L.Traffic;
        m.I(M.At(M.Options, t.FilterRange), 40);
        m.I(M.At(M.Options, t.FilterSeparation), 2700);
        TcasTest(m, 5);
        for (int k = 0; k < 4; k++) Entry(m, k, 1000, 0, 0, 30, 100);
        Entry(m, 4, 5000, 0, 0, 8, 10);
        Assert.Contains("Map: other traffic, 8.0 NM, 10° right, +50", Rows(m));

        m.I(M.TrafficList + (uint)t.TestField, 0);
        Assert.DoesNotContain(Rows(m), r => r.Contains("+50", StringComparison.Ordinal));
    }

    [Fact]
    public void Route_airports_show_the_runway_close_up()
    {
        var m = LiveMemory();
        var threshold = From(0.5, 200);
        m.AddRouteAirport(0, departure: true, "LPPT", 38.7813, -9.1359, runway: ("RW03", threshold.Lat, threshold.Lon));
        // Range index 2 (20 NM): the runway's outline and its name, then its centreline; 200 - 77.9 = 122.1° right of the top.
        Assert.Contains(Rows(m), r => r.StartsWith("Map: runway RW03, 0.5 NM, 122° right; centreline ", StringComparison.Ordinal));
        m.I(M.At(M.Panel, L.Panel.Range), 4);
        Assert.Contains("Map: airport LPPT, runway RW03, 0.5 NM, 122° right", Rows(m));
    }

    /// <summary>The flight's edge cases (2026-10-28, range 40, cap\s2b-zoom-231209 and s4-arpt-231433), placed at the
    /// distance and angle off the top each had: PMDG draws what passes its cull and the arc clip cuts it.</summary>
    [Fact]
    public void Objects_cut_by_the_map_s_edge_are_listed_at_the_edge()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        m.B(M.At(M.Panel, L.Panel.ShowArpt), true);
        void Add(int vector, string name, double nm, double offTopDeg)
        {
            var p = From(nm, Ahead + offTopDeg);
            m.AddListObject(vector, name, p.Lat, p.Lon);
        }
        // ZMANN: the triangle cut, "MANN" read; ROBNS: read whole; REMAC: its centre off the screen's left, "EMAC" read.
        Add(L.Lists.FixVector, "ZMANN", 37.0, -41.3);
        Add(L.Lists.FixVector, "ROBNS", 37.4, -49.3);
        Add(L.Lists.FixVector, "REMAC", 38.5, -48.6);
        // CTEEE: drawn (inside the cull) but wholly outside the arc: nothing shows.
        Add(L.Lists.FixVector, "CTEEE", 41.8, -40.0);
        // KVPC: the circle outside the arc, the name's lower half in; KJZP: the circle cut; KPUJ: a sliver at most.
        Add(L.Lists.AirportVector, "KVPC", 37.6, -6.0);
        Add(L.Lists.AirportVector, "KJZP", 36.5, 38.0);
        Add(L.Lists.AirportVector, "KPUJ", 39.9, -26.0);

        var rows = Rows(m);
        Assert.Contains("Map: fix ZMANN, 37.0 NM, 41° left, at the edge", rows);
        Assert.Contains("Map: fix ROBNS, 37.4 NM, 49° left, at the edge", rows);
        Assert.Contains("Map: fix REMAC, 38.5 NM, 49° left, at the edge", rows);
        Assert.Contains("Map: airport KVPC, 37.6 NM, 6° left, at the edge", rows);
        Assert.Contains("Map: airport KJZP, 36.5 NM, 38° right, at the edge", rows);
        Assert.DoesNotContain(rows, r => r.Contains("CTEEE", StringComparison.Ordinal) || r.Contains("KPUJ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_route_waypoint_cut_by_the_map_s_edge_is_listed_at_the_edge()
    {
        // PMDG draws a route waypoint on the screen and the arc clip cuts it: PT753 where ZMANN was
        // (37.0 NM, 41.3° left at range 40), its star and name partly on the map. With DATA on, after the DATA text.
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        Place(m, m.RouteNodes[2], 37.0, Ahead - 41.3);
        Assert.Contains("Map: PT753, 37.0 NM, 41° left, at the edge", Rows(m));
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: PT753, 37.0 NM, 41° left;", StringComparison.Ordinal) && r.EndsWith(", at the edge", StringComparison.Ordinal));
        // Off the screen, PMDG draws nothing of it.
        Place(m, m.RouteNodes[2], 39.0, Ahead - 50);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: PT753", StringComparison.Ordinal));
    }

    [Fact]
    public void A_marker_cut_by_the_map_s_edge_is_listed_at_the_edge()
    {
        // A VNAV marker: the 16-unit circle, the label from (+18, -8), on the screen.
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        var mk = R.Markers;
        int td = mk.Labels.First(kv => kv.Value == "T/D").Key;
        var (lat, lon) = From(37.0, Ahead - 41.3);
        m.Position(M.Plan(0) + (uint)(mk.LateralPath + mk.Array + td * mk.Stride), lat, lon);
        m.B(M.Plan(0) + (uint)(R.PerfInfo + R.PerfPath), true);
        Assert.Contains("Map: T/D, 37.0 NM, 41° left, at the edge", Rows(m));
    }

    [Fact]
    public void A_tuned_vor_cut_by_the_map_s_edge_is_listed_at_the_edge()
    {
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        var tu = L.Tuned;
        uint vor = M.Sim + (uint)tu.Stations;
        m.Text(vor + (uint)tu.Ident, "CAS");
        m.I(vor + (uint)tu.State, tu.TunedState);
        m.B(M.At(M.Misc, tu.Powered), true);
        void At(double nm, double offTopDeg)
        {
            var p = From(nm, Ahead + offTopDeg);
            m.Position(vor + (uint)tu.Position, p.Lat, p.Lon);
        }
        At(37.0, -41.3);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: VOR 1 CAS, 37.0 NM, 41° left", StringComparison.Ordinal) && r.EndsWith(", at the edge", StringComparison.Ordinal));
        // 38.42 NM at 50° left: 515 units left of the centre, past the cull's 510, its name reaching onto the map.
        // PMDG skips the cull for a tuned station while its course-line option is set.
        At(38.42, -50);
        m.B(M.At(M.Options, tu.CourseOption), false);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: VOR 1 CAS", StringComparison.Ordinal));
        m.B(M.At(M.Options, tu.CourseOption), true);
        Assert.Contains(Rows(m), r => r.StartsWith("Map: VOR 1 CAS, 38.4 NM, 50° left", StringComparison.Ordinal) && r.EndsWith(", at the edge", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pln_aircraft_cut_by_the_map_s_edge_is_listed_at_the_edge()
    {
        // PLN at range 20 centred on PT753: the 410-unit compass stands for 10 NM, the clip a 400-unit circle. 10 NM
        // south of PT753 the aircraft's symbol (reaching about 38 units) is cut by the circle.
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        var north = From(10, 0);
        m.Position(m.RouteNodes[2] + (uint)R.NodePosition, north.Lat, north.Lon);
        m.U(M.Plan(0) + (uint)(R.PlnCentre.RouteInfo + R.PlnCentre.CentreSlot), m.RouteNodes[2]);
        Assert.Contains("Map: aircraft, 10.0 NM south of PT753, at the edge", Rows(m));
    }

    [Fact]
    public void Excess_data_counts_what_pmdg_draws_beyond_the_arc()
    {
        // PMDG counts each fix its cull passes, the arc clip aside: 30 on the map and
        // 12 drawn above the arc (38.9 NM ahead at range 40: 680 units up, the cull's top at 710) reach the 41st.
        var m = LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        m.B(M.At(M.Panel, L.Panel.ShowWpt), true);
        for (int k = 0; k < 30; k++)
        {
            var p = From(10, Ahead + k);
            m.AddListObject(L.Lists.FixVector, "IN" + k.ToString("00", CultureInfo.InvariantCulture), p.Lat, p.Lon);
        }
        for (int k = 0; k < 12; k++)
        {
            var p = From(38.9, Ahead - 3 + 0.5 * k);
            m.AddListObject(L.Lists.FixVector, "UP" + k.ToString("00", CultureInfo.InvariantCulture), p.Lat, p.Lon);
        }
        var rows = Rows(m);
        Assert.Contains("Message: EXCESS DATA", rows);
        Assert.Equal(30, rows.Count(r => r.StartsWith("Map: fix IN", StringComparison.Ordinal)));
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: fix UP", StringComparison.Ordinal));
    }

    [Fact]
    public void Arpt_lists_a_route_airport_whose_symbol_sits_at_its_runway()
    {
        // PMDG's ARPT list tests no route: it draws the route's airport too, its circle and name at the airport. The
        // route's symbol sits at the runway, so both show (live 2026-10-28, s4-arpt-231433: KATL's cyan circle and
        // name beside the route's white 08R).
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowArpt), true);
        var threshold = From(0.5, 200);
        var airport = From(1.5, 160);
        m.AddRouteAirport(0, departure: true, "LPPT", airport.Lat, airport.Lon, runway: ("RW03", threshold.Lat, threshold.Lon));
        m.AddListObject(L.Lists.AirportVector, "LPPT", airport.Lat, airport.Lon);
        var rows = Rows(m);
        Assert.Contains(rows, r => r.StartsWith("Map: runway RW03, 0.5 NM, 122° right; centreline ", StringComparison.Ordinal));
        Assert.Contains("Map: airport LPPT, 1.5 NM, 82° right", rows);

        m.I(M.At(M.Panel, L.Panel.Range), 4);
        rows = Rows(m);
        Assert.Contains("Map: airport LPPT, runway RW03, 0.5 NM, 122° right", rows);
        Assert.Contains("Map: airport LPPT, 1.5 NM, 82° right", rows);
    }

    [Fact]
    public void Arpt_leaves_out_a_route_airport_drawn_over_it()
    {
        // Without a runway the route's airport is the same circle and name at the airport's own place, drawn after
        // the ARPT list's: one symbol shows, one row.
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowArpt), true);
        var airport = From(6, Ahead);
        m.AddRouteAirport(0, departure: false, "LPFR", airport.Lat, airport.Lon);
        m.AddListObject(L.Lists.AirportVector, "LPFR", airport.Lat, airport.Lon);
        Assert.Equal("Map: airport LPFR, 6.0 NM, ahead", Assert.Single(Rows(m), r => r.Contains("LPFR", StringComparison.Ordinal)));
    }

    [Fact]
    public void Wind_row_with_its_arrow()
    {
        var m = LiveMemory();
        var c = L.Corners;
        m.D(M.At(M.Sim, c.TrueAirspeed), 250);
        m.D(M.At(M.Sim, c.WindSpeed), 14);
        m.D(M.At(M.Glass, c.GroundSpeed), 240);
        // From 351 against the track 079.4: 271.6, 88° left.
        Assert.Contains("Top left: GS 240, TAS 250, wind 351°/14, from 88° left", Rows(m));
    }

    [Fact]
    public void Altitude_arc_ahead_in_a_climb()
    {
        var m = LiveMemory();
        m.D(M.At(M.Sim, L.Symbols.VerticalSpeed), 1000);
        m.D(M.At(M.Glass, L.Corners.GroundSpeed), 150);
        // 5000 - 374 ft at 1000 ft/min: 4.626 minutes; at 150 kt, 11.6 NM.
        Assert.Contains("Map: altitude arc, 11.6 NM ahead", Rows(m));
    }

    /// <summary>A VNAV descent: the deviation, the phase PMDG needs, and the perf info's path byte set
    /// (PMDG gives the deviation only then).</summary>
    private static void Descending(M m, double deviationFt, bool path = true)
    {
        m.D(M.Fms + (uint)R.VnavDeviation, deviationFt);
        m.I(M.Fms + (uint)R.VnavPhase, R.VnavPhaseBelow);
        m.B(M.Plan(0) + (uint)(R.PerfInfo + R.PerfPath), path);
    }

    [Fact]
    public void Vdev_and_rnp_rows_in_descent()
    {
        var m = LiveMemory();
        Descending(m, 250);
        m.B(M.At(M.Options, L.Messages.Rnp.Option), true);
        // The scale's vertical RNP and ANP with the same option (live, cap\vdev-234103: 400 and 154).
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Rnp), 400);
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Anp), 154);
        var rows = Rows(m);
        Assert.Contains("Right side: VDEV 250, above the path, vertical RNP 400, ANP 154", rows);
        Assert.Contains("Bottom: RNP 2.00, ANP 0.05", rows);
    }

    /// <summary>The side the VDEV pointer is drawn on (live, 2026-10-28: -1090 ft, started down early, the diamond at
    /// the top; +1695 ft, the diamond at the bottom; +21 ft centred, no number): a pointer at the top is the path
    /// above the aircraft, so the aircraft below it.</summary>
    [Theory]
    [InlineData(-1090, "Right side: VDEV 1090, below the path")]
    [InlineData(1695, "Right side: VDEV 1695, above the path")]
    [InlineData(21, "Right side: VDEV, on the path")]
    [InlineData(-29, "Right side: VDEV, on the path")]
    public void Vdev_says_the_side_its_pointer_shows(double deviationFt, string row)
    {
        var m = LiveMemory();
        Descending(m, deviationFt);
        Assert.Contains(row, Rows(m));
    }

    /// <summary>The FMC's VTK flag set (glass data, per FMC): PMDG draws no VDEV scale and the
    /// amber letters V, T, K in its place, boxed; the flag of the FMC the Captain's unit shows counts. Listed at
    /// the right side (owner, 2026-10-29: "VTK amber", as G/S), spoken like a message.</summary>
    [Fact]
    public void Vtk_flag_replaces_the_vdev_scale()
    {
        var m = LiveMemory();
        Descending(m, 250);
        var vtk = L.Messages.Vdev.DrawnWhile.Single().Source.Field;
        m.B(M.At(M.Glass, vtk, 0), true);
        var rows = Rows(m);
        Assert.Contains("Right side: VTK (amber, boxed)", rows);
        Assert.DoesNotContain("Message: VTK", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Right side: VDEV", StringComparison.Ordinal));
        Assert.Contains("VTK", T.Messages(Sample(m), L));
        // Unit 0 on DEU 1: FMC 1, whose flag is clear.
        m.I(M.Efis + (uint)L.Units.Deu, 1);
        rows = Rows(m);
        Assert.DoesNotContain(rows, r => r.Contains("VTK", StringComparison.Ordinal));
        Assert.DoesNotContain("VTK", T.Messages(Sample(m), L));
        Assert.Contains("Right side: VDEV 250, above the path", rows);
    }

    [Fact]
    public void Vdev_side_follows_the_way_the_file_draws_the_pointer()
    {
        var flipped = GoldenLayouts.NavDisplayVdevUp;
        var m = LiveMemory();
        Descending(m, 250);
        Assert.Contains("Right side: VDEV 250, below the path", T.Lines(m.Read(flipped), flipped));
    }

    [Fact]
    public void No_vdev_without_the_vnav_path()
    {
        // The path byte clear: PMDG gives -1e6 whatever the field holds, and draws no scale.
        var m = LiveMemory();
        Descending(m, 250, path: false);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Right side:", StringComparison.Ordinal));
    }

    [Fact]
    public void Markers_need_the_vnav_path()
    {
        var m = LiveMemory();
        var mk = R.Markers;
        int td = mk.Labels.First(kv => kv.Value == "T/D").Key;
        var (lat, lon) = From(5, Ahead);
        m.Position(M.Plan(0) + (uint)(mk.LateralPath + mk.Array + td * mk.Stride), lat, lon);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: T/D", StringComparison.Ordinal));
        m.B(M.Plan(0) + (uint)(R.PerfInfo + R.PerfPath), true);
        Assert.Contains("Map: T/D, 5.0 NM, ahead", Rows(m));
    }

    [Fact]
    public void Distance_format_switches_at_99_95()
    {
        Assert.Equal("99.9", T.Distance(99.94));
        Assert.Equal("100", T.Distance(99.95));
        Assert.Equal("6.8", T.Distance(6.8));
        Assert.Equal("150", T.Distance(150.4));
    }

    [Fact]
    public void Eta_and_direction_formats()
    {
        Assert.Equal("1533.3Z", T.Eta(new NdClock(15, 33, 18), R.Clock));
        Assert.Equal("0905.0Z", T.Eta(new NdClock(9, 5, 0), R.Clock));
        Assert.Equal("------Z", T.Eta(null, R.Clock));
        Assert.Equal("20° right", T.Direction(20.4));
        Assert.Equal("104° left", T.Direction(-104));
        Assert.Equal("ahead", T.Direction(0.4));
    }

    /// <summary>PMDG's clock tenths, worked by hand: s / 60 rounds to one decimal half up, and PMDG prints 9 while
    /// the rounded tenths are above 9 (57-59 s round to 1.0).</summary>
    [Theory]
    [InlineData(2, "1533.0Z")]
    [InlineData(3, "1533.1Z")]
    [InlineData(9, "1533.2Z")]
    [InlineData(56, "1533.9Z")]
    [InlineData(57, "1533.9Z")]
    [InlineData(59, "1533.9Z")]
    public void Eta_tenths_round_half_up_and_stop_at_9(int seconds, string printed) =>
        Assert.Equal(printed, T.Eta(new NdClock(15, 33, seconds), R.Clock));

    [Fact]
    public void Top_right_eta_in_the_minute_s_last_seconds()
    {
        var m = LiveMemory();
        m.I(m.RouteNodes[1] + (uint)(R.NodeEta + R.Clock.Seconds), 59);
        Assert.Contains("Top right: EMPAD, 1533.9Z, 6.8 NM", Rows(m));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_are_invariant_under_comma_cultures(string culture)
    {
        var m = LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowData), true);
        m.D(M.At(M.Sim, L.Corners.TrueAirspeed), 250);
        m.B(M.At(M.Options, L.Messages.Rnp.Option), true);
        m.U(M.Keys(0) + (uint)L.Xtk.KeySlot, m.RouteNodes[1]);
        m.D(M.Plan(0) + (uint)(R.Markers.LateralPath + L.Xtk.Value), 9.7);
        var s = Sample(m);
        var previous = CultureInfo.CurrentCulture;
        IReadOnlyList<string> invariant, local;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = T.Lines(s, L);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            local = T.Lines(s, L);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal(invariant, local);
        Assert.Contains("Top right: EMPAD, 1533.3Z, 6.8 NM", local);
        Assert.Contains("Bottom: RNP 2.00, ANP 0.05", local);
        Assert.Contains("Below the aircraft: XTK 9.7 R", local);
    }

    // ---- the cross-track error ----

    /// <summary>The live reading with the RNP option set, the key node +8 EMPAD and the reference route's lateral path
    /// holding <paramref name="xtk"/>.</summary>
    private static M XtkMemory(double xtk)
    {
        var m = LiveMemory();
        m.B(M.At(M.Options, L.Messages.Rnp.Option), true);
        m.U(M.Keys(0) + (uint)L.Xtk.KeySlot, m.RouteNodes[1]);
        m.D(M.Plan(0) + (uint)(R.Markers.LateralPath + L.Xtk.Value), xtk);
        return m;
    }

    /// <summary>The cross-track error under the aircraft (live, 2026-10-28: "0.0" at the gate, "9.7 R",
    /// "26.3 R", "0.7 L" in flight): two decimals at range index 1 and below; "XTK" is the app's word, PMDG prints
    /// the number alone.</summary>
    [Theory]
    [InlineData(2, 0.0, "Below the aircraft: XTK 0.0")]
    [InlineData(2, 9.7, "Below the aircraft: XTK 9.7 R")]
    [InlineData(2, 26.3, "Below the aircraft: XTK 26.3 R")]
    [InlineData(2, -0.7, "Below the aircraft: XTK 0.7 L")]
    [InlineData(1, 0.001, "Below the aircraft: XTK 0.00")]
    [InlineData(1, -0.12, "Below the aircraft: XTK 0.12 L")]
    public void Xtk_is_printed_as_pmdg_prints_it(int range, double xtk, string row)
    {
        var m = XtkMemory(xtk);
        m.I(M.At(M.Panel, L.Panel.Range), range);
        Assert.Contains(row, Rows(m));
    }

    /// <summary>None where PMDG gives none (no FMS position, the path's int negative, no key node, its byte set
    /// with its count below the limit), where it computes one (a conditional node: not read), at |xtk| ≥ 99.95, with
    /// the reference route not active, without the RNP option, outside MAP.</summary>
    [Theory]
    [InlineData("position")]
    [InlineData("path")]
    [InlineData("node")]
    [InlineData("flag")]
    [InlineData("conditional")]
    [InlineData("far")]
    [InlineData("inactive")]
    [InlineData("option")]
    [InlineData("pln")]
    public void No_xtk_where_pmdg_draws_none(string why)
    {
        var m = XtkMemory(9.7);
        Assert.Contains("Below the aircraft: XTK 9.7 R", Rows(m));
        var x = L.Xtk;
        uint node = m.RouteNodes[1], path = M.Plan(0) + (uint)R.Markers.LateralPath;
        switch (why)
        {
            case "position": m.Position(M.Fms + (uint)R.FmsPosition, M.AircraftLatitude, M.AircraftLongitude, valid: false); break;
            case "path": m.I(path + (uint)x.PathInvalid, -1); break;
            case "node": m.U(M.Keys(0) + (uint)x.KeySlot, 0); break;
            case "flag": m.B(node + (uint)x.NodeFlag, true); m.I(node + (uint)x.NodeCount, x.NodeCountBelow - 1); break;
            case "conditional": m.B(node + (uint)R.Validity.Conditional, true); break;
            case "far": m.D(path + (uint)x.Value, x.ShownBelow); break;
            case "inactive": m.B(M.Plan(0) + (uint)R.PlanActive, false); break;
            case "option": m.B(M.At(M.Options, L.Messages.Rnp.Option), false); break;
            default: m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan); break;
        }
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Below the aircraft", StringComparison.Ordinal));
    }
}
