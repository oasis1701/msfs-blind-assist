// The ND's details (2026-10-29): symbols PMDG draws that the first build did not list, each from the layout file
// (WASM-2) and listed where PMDG draws it. A detail missing from the layout leaves its row out and names why;
// the rest of the layout stays whole. These tests check the reading and the rows on the golden layout.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using T = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayText;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayDetailTests
{
    private static Pmdg737NavDisplayLayout L => M.Layout;

    private static IReadOnlyList<string> Rows(M m) => T.Lines(m.Read(), L);

    private static void InCulture(string name, Action check)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            check();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- the TCAS ring ----

    [Fact]
    public void Reads_the_traffic_list_and_the_ring_option()
    {
        var m = new M();
        Assert.Equal(new NdDetailReading(false, false), m.Read().Details);
        m.U(M.Tcas + (uint)L.Traffic.List, M.TrafficList);
        m.B(M.At(M.Options, L.Details!.TcasRing!.Option), true);
        Assert.Equal(new NdDetailReading(true, true), m.Read().Details);
    }

    /// <summary>The live memory with the TCAS object's list and the ring's option set (no target).</summary>
    private static M RingMemory()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.U(M.Tcas + (uint)L.Traffic.List, M.TrafficList);
        m.B(M.At(M.Options, L.Details!.TcasRing!.Option), true);
        return m;
    }

    [Fact]
    public void The_tcas_ring_is_listed_with_tfc_or_the_self_test()
    {
        var m = RingMemory();
        Assert.DoesNotContain(Rows(m), r => r.Contains("TCAS ring", StringComparison.Ordinal));
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        Assert.Contains("Map: TCAS ring, 3 NM", Rows(m));
        InCulture("sv-SE", () => Assert.Contains("Map: TCAS ring, 3 NM", Rows(m)));
        m.B(M.At(M.Panel, L.Panel.ShowTfc), false);
        m.B(M.At(M.Tcas, L.Traffic.SelfTest), true);
        Assert.Contains("Map: TCAS ring, 3 NM", Rows(m));
    }

    [Fact]
    public void The_tcas_ring_follows_its_gates()
    {
        var m = RingMemory();
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        bool Listed() => Rows(m).Contains("Map: TCAS ring, 3 NM");

        // Not in the TCAS test (the list's int 24 at 8), drawn at 2.
        m.I(M.TrafficList + (uint)L.Traffic.TestField, L.Traffic.TestValue);
        Assert.False(Listed());
        m.I(M.TrafficList + (uint)L.Traffic.TestField, 2);
        Assert.True(Listed());
        m.I(M.TrafficList + (uint)L.Traffic.TestField, 0);
        // Range index 4 is 80 NM, index 5 160.
        m.I(M.At(M.Panel, L.Panel.Range), 4);
        Assert.True(Listed());
        m.I(M.At(M.Panel, L.Panel.Range), 5);
        Assert.False(Listed());
        m.I(M.At(M.Panel, L.Panel.Range), 2);
        // PMDG's option.
        m.B(M.At(M.Options, L.Details!.TcasRing!.Option), false);
        Assert.False(Listed());
        m.B(M.At(M.Options, L.Details.TcasRing.Option), true);
        // No list, no ring.
        m.U(M.Tcas + (uint)L.Traffic.List, 0);
        Assert.False(Listed());
    }

    [Fact]
    public void The_tcas_ring_is_drawn_where_the_traffic_is()
    {
        var m = RingMemory();
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Contains("Map: TCAS ring, 3 NM", Rows(m)); // MAP, centred
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        Assert.DoesNotContain(Rows(m), r => r.Contains("TCAS ring", StringComparison.Ordinal)); // APP centred: no traffic
        m.B(M.At(M.Panel, L.Panel.Centered), false);
        Assert.Contains("Centre: TCAS ring, 3 NM", Rows(m));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        Assert.DoesNotContain(Rows(m), r => r.Contains("TCAS ring", StringComparison.Ordinal));
    }

    // ---- the dashed line to the heading bug ----

    /// <summary>The heading line's option set and the FMA roll mode at <paramref name="roll"/>, the sim's clock at
    /// <paramref name="now"/>, the selected heading changed at <paramref name="changed"/>.</summary>
    private static void Timed(M m, int roll, double now, double changed)
    {
        var h = L.Symbols.HeadingLine;
        m.B(M.At(M.Options, h.Option), true);
        m.I(M.At(M.Glass, h.RollMode), roll);
        m.D(M.At(M.Sim, h.SimTime), now);
        m.D(M.Generator + (uint)h.ChangedAt.Offset, changed);
    }

    [Fact]
    public void Reads_the_heading_change_s_time_only_while_the_line_is_timed()
    {
        var m = new M();
        Timed(m, 5, 100, 95);
        Assert.Equal(new NdDetailReading(false, false, true, 5, 100, 95), m.Read().Details);
        m.I(M.At(M.Glass, L.Symbols.HeadingLine.RollMode), 3); // HDG SEL: not timed
        Assert.Equal(new NdDetailReading(false, false, true, 3, 0, 0), m.Read().Details);
    }

    [Fact]
    public void The_dashed_line_to_the_heading_bug()
    {
        // PMDG draws the line from the aircraft to the bug, to the compass, after the bug itself;
        // with the selected heading 109 against the track 079.4 (and the heading 079.4), 30° right.
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 109);
        var rows = Rows(m).ToList();
        Assert.Equal(rows.IndexOf("Map: heading bug, 30° right") + 1, rows.IndexOf("Map: dashed line to the heading bug, 30° right"));
        // The bug behind the aircraft is below the screen, its line drawn on it.
        m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 259);
        rows = Rows(m).ToList();
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: heading bug", StringComparison.Ordinal));
        Assert.Contains("Map: dashed line to the heading bug, 180° right", rows);
        // Centred MAP draws it; not with the readout invalid.
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Contains("Map: dashed line to the heading bug, 180° right", Rows(m));
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.DoesNotContain(Rows(m), r => r.Contains("dashed line", StringComparison.Ordinal));
    }

    [Fact]
    public void The_dashed_line_goes_10_seconds_after_a_heading_change_in_lnav_or_vor_loc_with_pmdg_s_option()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        bool Listed() => Rows(m).Any(r => r.StartsWith("Map: dashed line to the heading bug", StringComparison.Ordinal));
        Timed(m, 5, 100, 95);
        Assert.True(Listed());
        Timed(m, 5, 100, 89.9);
        Assert.False(Listed()); // LNAV, 10.1 s after the change
        Timed(m, 4, 100, 89.9);
        Assert.False(Listed()); // VOR/LOC
        Timed(m, 3, 100, 89.9);
        Assert.True(Listed());  // HDG SEL: always
        Timed(m, 5, 100, 89.9);
        m.B(M.At(M.Options, L.Symbols.HeadingLine.Option), false);
        Assert.True(Listed());  // the option clear: always
    }

    [Fact]
    public void The_dashed_line_in_app_and_vor()
    {
        foreach (int mode in new[] { L.Geometry.Modes.App, L.Geometry.Modes.Vor })
        {
            var m = Pmdg737NavDisplayTextTests.LiveMemory();
            m.I(M.At(M.Panel, L.Panel.Mode), mode);
            m.I(M.At(M.Glass, L.Symbols.SelectedHeading), 109);
            Assert.Contains("Centre: dashed line to the heading bug, 30° right", Rows(m));
            // Centred, PMDG draws the bug and no line.
            m.B(M.At(M.Panel, L.Panel.Centered), true);
            var rows = Rows(m);
            Assert.Contains("Centre: heading bug, 30° right", rows);
            Assert.DoesNotContain(rows, r => r.Contains("dashed line", StringComparison.Ordinal));
        }
    }

    // ---- the radar's tilt and VAR ----

    [Fact]
    public void Reads_the_captain_s_tilt_and_the_radar_s_bytes()
    {
        var m = new M();
        var w = L.Messages.Wxr;
        m.I(M.At(M.Weather, w.Tilt, 0), -3);
        m.I(M.At(M.Weather, w.Tilt, 1), 5);
        m.B(M.At(M.Weather, w.Auto), true);
        m.B(M.At(M.Weather, w.Variable, 1), true);
        var d = m.Read().Details!;
        Assert.Equal((-3, true, false), (d.WxrTilt, d.WxrAuto, d.WxrVariable));
    }

    [Fact]
    public void The_tilt_and_var_follow_the_wxr_label()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        var w = L.Messages.Wxr;
        string? Left() => Rows(m).FirstOrDefault(r => r.StartsWith("Left side:", StringComparison.Ordinal));
        Assert.Null(Left());
        m.B(M.At(M.Panel, L.Panel.ShowWxr), true);
        // Tilt 0 in manual, the gain variable (its byte clear).
        Assert.Equal("Left side: WXR, tilt 0M, VAR", Left());
        m.I(M.At(M.Weather, w.Tilt, 0), 2);
        m.B(M.At(M.Weather, w.Auto), true);
        m.B(M.At(M.Weather, w.Variable, 0), true);
        Assert.Equal("Left side: WXR, tilt +2A", Left());
        m.I(M.At(M.Weather, w.Tilt, 0), -3);
        m.B(M.At(M.Weather, w.Variable, 0), false);
        Assert.Equal("Left side: WXR, tilt -3A, VAR", Left());
        // The inboard map page (the half format) prints "VAR/".
        var u = L.Units;
        m.I(M.Efis + (uint)u.PageOffset, u.MapPages[0]);
        Assert.Equal("Left side: WXR, tilt -3A, VAR/", Left());
        // Not with WXR off.
        m.B(M.At(M.Panel, L.Panel.ShowWxr), false);
        Assert.Null(Left());
    }

    // ---- the terrain numbers ----

    /// <summary>The terrain record of the Captain's unit (index 0: slot 0) with TERR on and PMDG's option set.</summary>
    private static void Terrain(M m, int highest, int highestColour, int lowest, int lowestColour)
    {
        var t = L.Messages.Terrain;
        m.B(M.At(M.Panel, L.Panel.ShowTerr), true);
        m.B(M.At(M.Options, t.Option), true);
        m.I(M.Generator + (uint)t.Highest.Offset, highest);
        m.I(M.Generator + (uint)t.HighestColour.Offset, highestColour);
        m.I(M.Generator + (uint)t.Lowest.Offset, lowest);
        m.I(M.Generator + (uint)t.LowestColour.Offset, lowestColour);
    }

    [Fact]
    public void Reads_the_terrain_record_while_terr_is_on()
    {
        var m = new M();
        Terrain(m, 4520, 2, 1180, 1);
        var d = m.Read().Details!;
        Assert.Equal((true, false, 4520, 2, 1180, 1), (d.TerrainOption, d.GpwsTest, d.TerrainHighest, d.TerrainHighestColour, d.TerrainLowest, d.TerrainLowestColour));
        // Another slot's record is not the Captain's.
        m.I(M.Generator + (uint)(L.Messages.Terrain.Highest.Offset + L.Messages.Terrain.Highest.Stride), 9900);
        Assert.Equal(4520, m.Read().Details!.TerrainHighest);
        // Not read during the GPWS test, nor with TERR off.
        m.B(M.At(M.Gpws, L.Messages.Terrain.GpwsTest), true);
        d = m.Read().Details!;
        Assert.Equal((true, 0, 0), (d.GpwsTest, d.TerrainHighest, d.TerrainLowest));
        m.B(M.At(M.Gpws, L.Messages.Terrain.GpwsTest), false);
        m.B(M.At(M.Panel, L.Panel.ShowTerr), false);
        Assert.Equal(0, m.Read().Details!.TerrainHighest);
    }

    [Fact]
    public void The_terrain_numbers_on_the_left_side()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        string? Left() => Rows(m).FirstOrDefault(r => r.StartsWith("Left side:", StringComparison.Ordinal));
        // 4520 ft is printed 045 (hundreds, half up), 1180 ft 012.
        Terrain(m, 4520, 2, 1180, 1);
        Assert.Equal("Left side: TERR, highest 045 (amber), lowest 012 (green)", Left());
        InCulture("sv-SE", () => Assert.Equal("Left side: TERR, highest 045 (amber), lowest 012 (green)", Left()));
        Terrain(m, 4550, 3, -150, 2);
        Assert.Equal("Left side: TERR, highest 046 (red), lowest -01 (amber)", Left());
        // The highest's colour code 0: no lowest.
        Terrain(m, 4520, 0, 1180, 1);
        Assert.Equal("Left side: TERR, highest 045 (green)", Left());
        // No highest elevation: the lowest alone.
        Terrain(m, -999999, 1, 1180, 1);
        Assert.Equal("Left side: TERR, lowest 012 (green)", Left());
        // Not during the GPWS test or with PMDG's option clear.
        Terrain(m, 4520, 2, 1180, 1);
        m.B(M.At(M.Options, L.Messages.Terrain.Option), false);
        Assert.Equal("Left side: TERR", Left());
        m.B(M.At(M.Options, L.Messages.Terrain.Option), true);
        m.B(M.At(M.Gpws, L.Messages.Terrain.GpwsTest), true);
        Assert.DoesNotContain("highest", Left() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void The_radar_lines_give_way_to_the_terrain_numbers()
    {
        // Once PMDG enters its terrain block it prints no tilt or VAR: the block returns before the radar lines.
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.B(M.At(M.Panel, L.Panel.ShowWxr), true);
        m.I(M.At(M.Weather, L.Messages.Wxr.Tilt, 0), 2);
        Terrain(m, 4520, 2, 1180, 1);
        string left = Rows(m).First(r => r.StartsWith("Left side:", StringComparison.Ordinal));
        Assert.Contains("highest 045 (amber)", left, StringComparison.Ordinal);
        Assert.DoesNotContain("tilt", left, StringComparison.Ordinal);
        // Not entered (PMDG's option clear): the tilt again.
        m.B(M.At(M.Options, L.Messages.Terrain.Option), false);
        Assert.Contains("tilt +2M", Rows(m).First(r => r.StartsWith("Left side:", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    // ---- the runway's extended centreline ----

    [Fact]
    public void Reads_the_runway_s_heading_and_length()
    {
        var m = new M();
        var (lat, lon) = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 1, 90);
        m.AddRouteAirport(0, departure: true, "KATL", lat, lon, runway: ("08R", lat, lon), runwayHeadingDeg: 84, runwayLengthFt: 10000);
        var a = Assert.Single(m.Read().RouteAirports);
        Assert.Equal((84, 10000), (a.RunwayHeadingDeg, a.RunwayLengthFt));
    }

    /// <summary>The live memory with the departure runway 08R (heading <paramref name="heading"/>, 10000 ft) at
    /// <paramref name="nm"/> NM, <paramref name="bearingTrueDeg"/> true from the aircraft.</summary>
    private static M WithRunway(double nm, double bearingTrueDeg, int heading)
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        var (lat, lon) = M.Destination(M.AircraftLatitude, M.AircraftLongitude, nm, bearingTrueDeg);
        m.AddRouteAirport(0, departure: true, "KATL", lat, lon, runway: ("08R", lat, lon), runwayHeadingDeg: heading, runwayLengthFt: 10000);
        return m;
    }

    private static string? RunwayRow(M m) => Rows(m).SingleOrDefault(r => r.Contains("08R", StringComparison.Ordinal));

    [Fact]
    public void The_runway_row_names_its_extended_centreline()
    {
        // The top of the map is the track, 077.9 true. The runway 1.0 NM to the right, heading 084 magnetic (082.5 true):
        // its centreline runs 5° right of the top and passes 1.0 NM right of the aircraft, which is beside it.
        const double Ahead = 77.9;
        var m = WithRunway(1, Ahead + 90, 84);
        Assert.Equal("Map: runway 08R, 1.0 NM, 90° right; centreline 14.2 NM each way, running 5° right of the top, you are 1.0 NM left of it", RunwayRow(m));
        InCulture("sv-SE", () => Assert.Equal("Map: runway 08R, 1.0 NM, 90° right; centreline 14.2 NM each way, running 5° right of the top, you are 1.0 NM left of it", RunwayRow(m)));
        // The other end's runway, 264: the same line on the screen.
        Assert.Equal("Map: runway 08R, 1.0 NM, 90° right; centreline 14.2 NM each way, running 5° right of the top, you are 1.0 NM left of it",
            RunwayRow(WithRunway(1, Ahead + 90, 264)));
        // Ahead on the track: on the line 5 NM out; 16 NM out, beyond its 14.2 NM: not beside it.
        Assert.Equal("Map: runway 08R, 5.0 NM, ahead; centreline 14.2 NM each way, running to the top, you are on it", RunwayRow(WithRunway(5, Ahead, 79)));
        Assert.Equal("Map: runway 08R, 16.0 NM, ahead; centreline 14.2 NM each way, running to the top", RunwayRow(WithRunway(16, Ahead, 79)));
        // Farther than range index 3 PMDG draws the airport's circle and a tick, no outline: no centreline.
        m.I(M.At(M.Panel, L.Panel.Range), 4);
        Assert.Equal("Map: airport KATL, runway 08R, 1.0 NM, 90° right", RunwayRow(m));
    }

    // ---- the FIX page's abeam line ----

    /// <summary>A FIX page entry for EMPAD 12 NM 20° right of the top (the track, 077.9 true), R090 and a 10 NM ring,
    /// with its abeam point (the FIX page's ABEAM) at <paramref name="abeamNm"/> NM, <paramref name="abeamDeg"/>° off the
    /// top, or none.</summary>
    private static void FixEntry(M m, double? abeamNm = null, double abeamDeg = 0)
    {
        const double Ahead = 77.9;
        var f = L.FixPage;
        uint page = M.FixPageArea, entry = page + 0x400, fix = page + 0x800;
        m.U(M.Cdu + (uint)(f.Pages + 4 * f.FixPage), page);
        m.U(page + (uint)f.Entries, entry);
        m.U(entry + (uint)f.FixPointer, fix);
        m.Text(fix, "EMPAD");
        var (lat, lon) = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 12, Ahead + 20);
        m.Position(fix + (uint)f.ObjectPosition, lat, lon);
        m.I(fix + (uint)f.ObjectType, f.FixType);
        for (int k = 0; k < f.RingCount; k++) m.D(entry + (uint)(f.Rings + 8 * k), k == 0 ? 10 : f.InvalidAtOrBelow);
        for (int k = 0; k < f.RadialCount; k++) m.D(entry + (uint)(f.Radials + 8 * k), k == 0 ? 90 : f.InvalidAtOrBelow);
        if (abeamNm is { } nm)
        {
            var (aLat, aLon) = M.Destination(M.AircraftLatitude, M.AircraftLongitude, nm, Ahead + abeamDeg);
            m.Position(entry + (uint)f.Abeam, aLat, aLon);
        }
        else m.Position(entry + (uint)f.Abeam, 0, 0, valid: false);
    }

    [Fact]
    public void Reads_the_fix_page_s_abeam_point()
    {
        var m = new M();
        FixEntry(m);
        Assert.Null(Assert.Single(m.Read().FixPage).Abeam);
        FixEntry(m, 8.2, -10);
        Assert.NotNull(Assert.Single(m.Read().FixPage).Abeam);
    }

    [Fact]
    public void The_fix_row_names_its_abeam_line()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        FixEntry(m);
        Assert.Contains("Map: FIX EMPAD, 12.0 NM, 20° right: R090 ring 10", Rows(m));
        // After the FIX page's ABEAM, PMDG draws a line from the fix to the abeam point on the route.
        FixEntry(m, 8.2, -10);
        Assert.Contains("Map: FIX EMPAD, 12.0 NM, 20° right: R090 ring 10; abeam line to the route at 8.2 NM, 10° left", Rows(m));
        InCulture("sv-SE", () => Assert.Contains("Map: FIX EMPAD, 12.0 NM, 20° right: R090 ring 10; abeam line to the route at 8.2 NM, 10° left", Rows(m)));
    }

    // ---- the holds ----

    /// <summary>A hold at EMPAD as PMDG makes one: a copy of the fix (its name, position and variation, -1.5),
    /// chained right after it, its byte 936 set; the turn byte, the inbound course (true) and the HOLD page's legs.</summary>
    private static uint HoldAtEmpad(M m, char turn, double inboundTrueDeg, double legMinutes, double legNm)
    {
        var h = L.Details!.Hold!;
        var v = L.Route.Validity;
        uint fix = m.RouteNodes[1], node = M.Node(12);
        Array.Copy(m.Bytes, (int)fix, m.Bytes, (int)node, L.Route.NodeSize);
        var (lat, lon) = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 8.1, 335); // EMPAD's place (NdTestMemory)
        m.Position(node + (uint)L.Route.NodePosition, lat, lon, variationDeg: -1.5);
        m.B(node + (uint)v.Hold, true);
        m.Bytes[node + (uint)(h.Record + h.Turn)] = (byte)turn;
        m.D(node + (uint)(h.Record + h.Inbound), inboundTrueDeg);
        m.D(node + (uint)h.LegTime, legMinutes);
        m.D(node + (uint)h.LegDistance, legNm);
        m.Chain(new[] { m.RouteNodes[0], fix, node, m.RouteNodes[2], m.RouteNodes[3], m.RouteNodes[4] });
        return node;
    }

    [Fact]
    public void Reads_a_hold_node()
    {
        var m = new M();
        uint node = HoldAtEmpad(m, 'R', 268.6, -1e6, 4);
        var route = m.Read().Route;
        Assert.Equal(new[] { "LPPT", "EMPAD", "EMPAD", "PT753", "IDBID", "ORVED" }, route.Select(w => w.Name));
        Assert.Equal((false, true), (route[1].IsHold, route[2].IsHold));
        Assert.Equal(new NdHold((byte)'R', 268.6, -1e6, 4), route[2].Hold);
        // With its skip byte set PMDG draws it as a waypoint.
        m.B(node + (uint)L.Route.Validity.HoldSkip, true);
        Assert.False(m.Read().Route[2].IsHold);
    }

    [Fact]
    public void A_hold_inbound_that_is_no_number_discards_the_reading()
    {
        var m = new M();
        HoldAtEmpad(m, 'R', double.NaN, -1e6, 4);
        using var sampler = m.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.Contains("hold", corrupt);
    }

    [Fact]
    public void A_hold_node_s_variation_that_is_no_number_discards_the_reading()
    {
        // The HOLD page prints the inbound less it; a NaN would read "inbound 000°" (review minor 2).
        var m = new M();
        uint node = HoldAtEmpad(m, 'R', 268.6, -1e6, 4);
        m.D(node + (uint)(L.Route.NodePosition + L.Route.Position.MagneticVariationDeg), double.NaN);
        using var sampler = m.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.Contains("hold", corrupt);
    }

    private static List<string> EmpadRows(IReadOnlyList<string> rows) =>
        rows.Where(r => r.StartsWith("Map: ", StringComparison.Ordinal) && r.Contains("EMPAD", StringComparison.Ordinal)).ToList();

    [Fact]
    public void The_hold_row_follows_its_fix()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        HoldAtEmpad(m, 'R', 268.6, -1e6, 4);
        // The inbound course 268.6 true less the variation -1.5: 270.1, printed "%03d" rounded as the HOLD page does.
        var expected = new[] { "Map: EMPAD, 8.1 NM, 103° left", "Map: hold at EMPAD, inbound 270°, right turns, legs 4.0 NM" };
        Assert.Equal(expected, EmpadRows(Rows(m)));
        InCulture("sv-SE", () => Assert.Equal(expected, EmpadRows(Rows(m))));
        // Left turns, and the HOLD page's time when no distance is set; neither: no legs.
        HoldAtEmpad(m, 'L', 268.6, 1.5, -1e6);
        Assert.Equal("Map: hold at EMPAD, inbound 270°, left turns, 1.5 minute legs", EmpadRows(Rows(m))[1]);
        InCulture("de-DE", () => Assert.Equal("Map: hold at EMPAD, inbound 270°, left turns, 1.5 minute legs", EmpadRows(Rows(m))[1]));
        HoldAtEmpad(m, 'L', 268.6, -1e6, -1e6);
        Assert.Equal("Map: hold at EMPAD, inbound 270°, left turns", EmpadRows(Rows(m))[1]);
    }

    [Fact]
    public void A_hold_is_listed_where_its_fix_is_on_the_map()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        HoldAtEmpad(m, 'R', 268.6, -1e6, 4);
        // Range index 0 (5 NM): EMPAD, 8.1 NM away, is off the map, and so is its hold.
        m.I(M.At(M.Panel, L.Panel.Range), 0);
        Assert.DoesNotContain(Rows(m), r => r.Contains("hold at", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_layout_s_hold_a_hold_node_lists_nothing()
    {
        var l = L with { Details = L.Details! with { Hold = null, Failures = new[] { "the hold: a leg printed \"% 4.1f\", not one number" } } };
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        HoldAtEmpad(m, 'R', 268.6, -1e6, 4);
        // PMDG draws the hold node as a hold, not a waypoint: no second EMPAD row, and no hold row without its fields.
        Assert.Equal(new[] { "Map: EMPAD, 8.1 NM, 103° left" }, EmpadRows(T.Lines(m.Read(l), l)));
    }

    // ---- the vertical RNP and ANP ----

    [Fact]
    public void Reads_the_vertical_rnp_and_anp()
    {
        var m = new M();
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Rnp), 400);
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Anp), 154);
        var d = m.Read().Details!;
        Assert.Equal((400, 154), (d.VerticalRnp, d.VerticalAnp));
    }

    [Fact]
    public void The_vdev_row_has_the_vertical_rnp_and_anp_with_the_rnp_option()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        var r = L.Route;
        m.D(M.Fms + (uint)r.VnavDeviation, -1090);
        m.I(M.Fms + (uint)r.VnavPhase, r.VnavPhaseBelow);
        m.B(M.Plan(0) + (uint)(r.PerfInfo + r.PerfPath), true);
        string Right() => Rows(m).Single(x => x.StartsWith("Right side: VDEV", StringComparison.Ordinal));
        Assert.Equal("Right side: VDEV 1090, below the path", Right());
        m.B(M.At(M.Options, L.Messages.Rnp.Option), true);
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Rnp), -999999);
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Anp), -999999);
        Assert.Equal("Right side: VDEV 1090, below the path, vertical RNP ---, ANP -.--", Right());
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Rnp), 400);
        m.I(M.Fms + (uint)(L.Messages.Rnp.PosData + L.Messages.Vdev.VerticalRnp.Anp), 154);
        m.D(M.Fms + (uint)r.VnavDeviation, 21);
        Assert.Equal("Right side: VDEV, on the path, vertical RNP 400, ANP 154", Right());
    }

    // ---- PLN's range rings ----

    [Fact]
    public void Pln_lists_its_range_rings()
    {
        // At range index 2 (20 NM) PLN's outer ring is 10 NM out (the radius halved) and its inner 5: labels "5" and "10",
        // each printed above and below the centre. The row comes first on the map.
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        var rows = Rows(m);
        Assert.Equal("Map: range rings 5 and 10", rows.First(r => r.StartsWith("Map:", StringComparison.Ordinal)));
        m.I(M.At(M.Panel, L.Panel.Range), 0);
        Assert.Contains("Map: range rings 1.25 and 2.5", Rows(m));
        // Only in PLN.
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Map);
        Assert.DoesNotContain(Rows(m), r => r.StartsWith("Map: range rings", StringComparison.Ordinal));
    }

    // ---- the trend vector ----

    [Fact]
    public void Reads_the_turn_rate()
    {
        var m = new M();
        m.D(M.At(M.Sim, L.Details!.TrendVector!.TurnRate), 0.05);
        Assert.Equal(0.05, m.Read().Details!.TurnRate);
    }

    /// <summary>The live memory at <paramref name="groundSpeed"/> knots turning <paramref name="degreesPerSecond"/>
    /// (right positive) at range index <paramref name="range"/>.</summary>
    private static M Moving(double groundSpeed, double degreesPerSecond, int range)
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.D(M.At(M.Glass, L.Corners.GroundSpeed), groundSpeed);
        m.D(M.At(M.Sim, L.Details!.TrendVector!.TurnRate), degreesPerSecond * Math.PI / 180);
        m.I(M.At(M.Panel, L.Panel.Range), range);
        return m;
    }

    private static string? TrendRow(M m) => Rows(m).SingleOrDefault(r => r.StartsWith("Map: trend vector", StringComparison.Ordinal));

    [Fact]
    public void The_trend_vector_straight_ahead()
    {
        // 252 kt is 0.07 NM a second: three segments end at 90 s, 6.3 NM; two at 60 s; one at 30 s. Listed after the
        // altitude arc's place (PMDG draws it after POS), before the tuned stations.
        var m = Moving(252, 0, 4);
        var rows = Rows(m).ToList();
        int at = rows.IndexOf("Map: trend vector, straight ahead, 6.3 NM in 90 seconds");
        Assert.Equal("Map: dashed line to the heading bug, 1° right", rows[at - 1]);
        Assert.Equal("Bottom left: FMC L", rows[at + 1]);
        // Climbing 1500 ft/min to 10000 ft from 5000: the altitude arc 3.3 min ahead, listed just before.
        m.D(M.At(M.Sim, L.Symbols.VerticalSpeed), 1500);
        m.I(M.At(M.Glass, L.Symbols.McpAltitude), 10000);
        m.D(M.At(M.Glass, L.Symbols.Altitude), 5000);
        rows = Rows(m).ToList();
        Assert.StartsWith("Map: altitude arc, ", rows[rows.IndexOf("Map: trend vector, straight ahead, 6.3 NM in 90 seconds") - 1]);
        InCulture("de-DE", () => Assert.Equal("Map: trend vector, straight ahead, 6.3 NM in 90 seconds", TrendRow(m)));
        Assert.Equal("Map: trend vector, straight ahead, 4.2 NM in 60 seconds", TrendRow(Moving(252, 0, 2)));
        Assert.Equal("Map: trend vector, straight ahead, 2.1 NM in 30 seconds", TrendRow(Moving(252, 0, 1)));
        // Below 0.03 °/s either way PMDG draws it straight.
        Assert.Equal("Map: trend vector, straight ahead, 6.3 NM in 90 seconds", TrendRow(Moving(252, -0.029, 4)));
    }

    [Fact]
    public void The_trend_vector_curving()
    {
        // 2 °/s at 252 kt: 180° in 90 s on a radius of 0.07 / (2 × π/180) = 2.0 NM.
        Assert.Equal("Map: trend vector, curving right through 180° in 90 seconds, 2.0 NM radius", TrendRow(Moving(252, 2, 4)));
        Assert.Equal("Map: trend vector, curving left through 60° in 30 seconds, 2.0 NM radius", TrendRow(Moving(252, -2, 0)));
        var slow = Moving(252, 0.031, 4);
        Assert.Equal("Map: trend vector, curving right through 3° in 90 seconds, 129 NM radius", TrendRow(slow));
        InCulture("sv-SE", () => Assert.Equal("Map: trend vector, curving right through 180° in 90 seconds, 2.0 NM radius", TrendRow(Moving(252, 2, 4))));
        // The layout's side from the drawing: a file curving left for a positive rate says left.
        var left = GoldenLayouts.NavDisplayTrendLeft;
        Assert.Contains("Map: trend vector, curving left through 180° in 90 seconds, 2.0 NM radius", T.Lines(Moving(252, 2, 4).Read(left), left));
    }

    [Fact]
    public void The_trend_vector_follows_its_gates()
    {
        // Above 80 kt only, with the map valid, in MAP (centred too), never in PLN, APP or VOR.
        Assert.Null(TrendRow(Moving(80, 0, 4)));
        Assert.NotNull(TrendRow(Moving(80.5, 0, 4)));
        var m = Moving(252, 0, 4);
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Equal("Map: trend vector, straight ahead, 6.3 NM in 90 seconds", TrendRow(m));
        m.B(M.At(M.Panel, L.Panel.Centered), false);
        foreach (int mode in new[] { L.Geometry.Modes.Plan, L.Geometry.Modes.App, L.Geometry.Modes.Vor })
        {
            m.I(M.At(M.Panel, L.Panel.Mode), mode);
            Assert.DoesNotContain(Rows(m), r => r.Contains("trend vector", StringComparison.Ordinal));
        }
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Map);
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.Null(TrendRow(m));
    }

    // ---- the track line ----

    [Fact]
    public void Reads_the_range_arcs_option()
    {
        var m = new M();
        Assert.False(m.Read().Details!.RangeArcsOption);
        m.B(M.At(M.Options, L.Details!.TrackLine!.RangeArcsOption), true);
        Assert.True(m.Read().Details!.RangeArcsOption);
    }

    private static string? TrackRow(M m) => Rows(m).SingleOrDefault(r => r.Contains("track line", StringComparison.Ordinal) || r.Contains("track pointer", StringComparison.Ordinal));

    [Fact]
    public void The_track_line_in_map()
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        // Track-up, with no picture or traffic on: the marks at a quarter, half and three quarters of the 20 NM the
        // compass spans; at range 40, 10, 20 and 30 NM.
        Assert.Equal("Map: track line, straight ahead, marks at 5, 10 and 15 NM", TrackRow(m));
        m.I(M.At(M.Panel, L.Panel.Range), 3);
        Assert.Equal("Map: track line, straight ahead, marks at 10, 20 and 30 NM", TrackRow(m));
        m.I(M.At(M.Panel, L.Panel.Range), 0);
        Assert.Equal("Map: track line, straight ahead, marks at 1.25, 2.5 and 3.75 NM", TrackRow(m));
        InCulture("de-DE", () => Assert.Equal("Map: track line, straight ahead, marks at 1.25, 2.5 and 3.75 NM", TrackRow(m)));
        m.I(M.At(M.Panel, L.Panel.Range), 2);
        // TFC on with PMDG's ND_RangeArcs option: the range arcs instead of the marks; centred, the marks again, the
        // compass spanning half the range.
        m.B(M.At(M.Panel, L.Panel.ShowTfc), true);
        m.B(M.At(M.Options, L.Details!.TrackLine!.RangeArcsOption), true);
        Assert.Equal("Map: track line, straight ahead", TrackRow(m));
        m.B(M.At(M.Panel, L.Panel.Centered), true);
        Assert.Equal("Map: track line, straight ahead, marks at 2.5, 5 and 7.5 NM", TrackRow(m));
        m.B(M.At(M.Panel, L.Panel.Centered), false);
        m.B(M.At(M.Options, L.Details.TrackLine.RangeArcsOption), false);
        Assert.Equal("Map: track line, straight ahead, marks at 5, 10 and 15 NM", TrackRow(m));
        // No track: no line; the marks with TFC on, none without.
        m.D(M.At(M.Glass, L.Orientation.Track), -999999);
        Assert.Equal("Map: track line marks at 5, 10 and 15 NM", TrackRow(m));
        m.B(M.At(M.Panel, L.Panel.ShowTfc), false);
        Assert.Null(TrackRow(m));
    }

    [Fact]
    public void The_track_line_leans_by_the_drift_heading_up()
    {
        // Heading 075, track 079.4: heading-up (the track-up option clear) the line leans 4° right of the top; with the
        // readout invalid PMDG leaves it straight up.
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.D(M.At(M.Glass, L.Orientation.Heading), 75);
        Assert.Equal("Map: track line, straight ahead, marks at 5, 10 and 15 NM", TrackRow(m));
        m.B(M.At(M.Options, L.Orientation.TrackUpOption), false);
        Assert.Equal("Map: track line, 4° right, marks at 5, 10 and 15 NM", TrackRow(m));
        var rows = Rows(m).ToList();
        Assert.Equal("Map: half-range ring 10", rows[rows.IndexOf("Map: track line, 4° right, marks at 5, 10 and 15 NM") - 1]);
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.Equal("Map: track line, straight ahead, marks at 5, 10 and 15 NM", TrackRow(m));
        // Not in PLN.
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.Plan);
        Assert.Null(TrackRow(m));
    }

    [Fact]
    public void The_track_line_and_pointer_in_app_and_vor()
    {
        foreach (int mode in new[] { L.Geometry.Modes.App, L.Geometry.Modes.Vor })
        {
            var m = Pmdg737NavDisplayTextTests.LiveMemory();
            m.I(M.At(M.Panel, L.Panel.Mode), mode);
            m.D(M.At(M.Glass, L.Orientation.Heading), 75);
            var rows = Rows(m).ToList();
            Assert.Equal("Centre: track line, 4° right, marks at 5, 10 and 15 NM", TrackRow(m));
            Assert.StartsWith("Top right: ", rows[rows.IndexOf("Centre: track line, 4° right, marks at 5, 10 and 15 NM") - 1]);
            // Centred on the full ND, the line; on the inboard map page (the half format), the pointer at the compass
            // alone, only with the readout valid.
            m.B(M.At(M.Panel, L.Panel.Centered), true);
            Assert.Equal("Centre: track line, 4° right, marks at 2.5, 5 and 7.5 NM", TrackRow(m));
            m.I(M.Efis + (uint)L.Units.PageOffset, L.Units.MapPages[0]);
            Assert.Equal("Centre: track pointer, 4° right", TrackRow(m));
            m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
            Assert.Null(TrackRow(m));
        }
    }
}
