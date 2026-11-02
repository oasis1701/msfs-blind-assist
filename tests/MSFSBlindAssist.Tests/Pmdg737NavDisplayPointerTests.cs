using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using T = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayText;

namespace MSFSBlindAssist.Tests;

/// <summary>The bearing pointers: the optional part, the sample and the rows, on the golden layout.</summary>
public class Pmdg737NavDisplayPointerTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;

    /// <summary>A layout whose pointer part did not derive.</summary>
    private static Pmdg737NavDisplayLayout Broken { get; } =
        L with { Pointers = null, PointersFailure = "pointers: draw() does not draw drawStationPointers in every mode but PLN" };

    private static NdPointerFields P => L.Pointers!;

    private static M InMode(int mode)
    {
        var m = Pmdg737NavDisplayTextTests.LiveMemory();
        m.I(M.At(M.Panel, L.Panel.Mode), mode);
        return m;
    }

    private static void Switch(M m, int k, int position) => m.I(M.At(M.Glass, L.Receivers.Switch, k), position);

    /// <summary>VOR k tuned (powered, in the tuned state) and its pointer valid, at a bearing from the nose.</summary>
    private static void VorPointer(M m, int k, double bearing)
    {
        Switch(m, k, P.VorPosition);
        m.B(M.At(M.Misc, P.VorPower, k), true);
        m.I(M.Sim + (uint)(L.Tuned.Stations + k * L.Tuned.Stride + L.Tuned.State), P.TunedState);
        m.B(M.At(M.Glass, P.VorValid, k), true);
        m.D(M.At(M.Glass, P.VorBearing, k), bearing);
    }

    /// <summary>ADF k powered and receiving, at a bearing from the nose.</summary>
    private static void AdfPointer(M m, int k, double bearing)
    {
        Switch(m, k, P.AdfPosition);
        m.B(M.At(M.Misc, P.AdfPower, k), true);
        m.I(M.Sim + (uint)(L.Receivers.AdfStations + k * L.Receivers.AdfStride + P.AdfSignal), 1);
        m.D(M.At(M.Glass, P.AdfBearing, k), bearing);
    }

    private static IReadOnlyList<string> Rows(M m) => T.Lines(m.Read(), L);

    private static IReadOnlyList<string> PointerRows(M m) =>
        Rows(m).Where(r => System.Text.RegularExpressions.Regex.IsMatch(r, @"(VOR|ADF) \d pointer")).ToList();

    // ---- the part left out ----

    [Fact]
    public void A_broken_pointer_part_leaves_the_rest_whole()
    {
        // The rows: with the part left out, the rest is unchanged.
        var modes = L.Geometry.Modes;
        foreach (int mode in new[] { modes.Map, modes.Plan, modes.App, modes.Vor })
        {
            var m = InMode(mode);
            Assert.Equal(T.Lines(m.Read(), L), T.Lines(m.Read(Broken), Broken));
        }
        // Where PMDG would draw one (a switch on VOR or ADF, not in PLN), one row says why there are none.
        var vor = InMode(modes.Map);
        VorPointer(vor, 0, 326);
        Assert.Contains("Pointers and POS: not read: PMDG file not recognised", T.Lines(vor.Read(Broken), Broken));
        vor.I(M.At(M.Panel, L.Panel.Mode), modes.Plan);
        Assert.DoesNotContain(T.Lines(vor.Read(Broken), Broken), r => r.Contains("not read", StringComparison.Ordinal));
        Assert.Contains(Pmdg737LayoutDescriptions.Describe(Broken), l => l.StartsWith("  pointers: not derived: ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_session_logs_an_underived_pointer_part()
    {
        var log = new List<string>();
        ModuleSession.Analyse(new[] { "nd" }, new FakeLayoutSource(f => new LayoutLookup(Pmdg737TestBuilds.Of(f, nd: Broken), null)), log.Add);
        Assert.Contains(log, l => l.StartsWith("file analysis: nd: navigation display pointers not recognised: ", StringComparison.Ordinal));
    }

    // ---- the sample and the rows ----

    [Fact]
    public void Pointer_rows_in_map_app_and_vor()
    {
        var modes = L.Geometry.Modes;
        var m = InMode(modes.Map);
        VorPointer(m, 0, 326);
        AdfPointer(m, 1, 20);
        Assert.Equal(new[] { "Map: VOR 1 pointer, 34° left", "Map: ADF 2 pointer, 20° right" }, PointerRows(m));
        foreach (int mode in new[] { modes.App, modes.Vor })
        {
            m.I(M.At(M.Panel, L.Panel.Mode), mode);
            Assert.Equal(new[] { "Centre: VOR 1 pointer, 34° left", "Centre: ADF 2 pointer, 20° right" }, PointerRows(m));
        }
    }

    [Fact]
    public void A_track_up_map_turns_the_pointers_by_the_drift()
    {
        // PMDG turns a pointer by heading - up: a crab (heading 075, track 079.4) moves it 4.4° left on a
        // track-up MAP; APP is heading-up.
        var m = InMode(L.Geometry.Modes.Map);
        m.D(M.At(M.Glass, L.Orientation.Heading), 75);
        VorPointer(m, 0, 326);
        Assert.Equal(new[] { "Map: VOR 1 pointer, 38° left" }, PointerRows(m));
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        Assert.Equal(new[] { "Centre: VOR 1 pointer, 34° left" }, PointerRows(m));
    }

    [Fact]
    public void No_pointer_rows_in_pln()
    {
        var m = InMode(L.Geometry.Modes.Plan);
        VorPointer(m, 0, 326);
        Assert.Empty(PointerRows(m));
        Assert.Null(m.Read().Pointers);
    }

    [Fact]
    public void No_pointer_while_its_switch_is_off()
    {
        var m = InMode(L.Geometry.Modes.Map);
        VorPointer(m, 0, 326);
        Switch(m, 0, 1);
        Assert.Empty(PointerRows(m));
    }

    [Fact]
    public void A_vor_pointer_needs_its_power_its_tuned_state_and_its_valid_flag()
    {
        // Not the valid flag alone: the tuned flag (power and state) gates the pointer too.
        var m = InMode(L.Geometry.Modes.Map);
        VorPointer(m, 0, 326);
        m.B(M.At(M.Glass, P.VorValid, 0), false);
        Assert.Empty(PointerRows(m));
        VorPointer(m, 0, 326);
        m.B(M.At(M.Misc, P.VorPower, 0), false);
        Assert.Empty(PointerRows(m));
        VorPointer(m, 0, 326);
        m.I(M.Sim + (uint)(L.Tuned.Stations + L.Tuned.State), 4);
        Assert.Empty(PointerRows(m));
        // VOR 2's valid flag is its own (one stride on from VOR 1's).
        VorPointer(m, 1, 10);
        Assert.Equal(new[] { "Map: VOR 2 pointer, 10° right" }, PointerRows(m));
    }

    [Fact]
    public void An_adf_pointer_needs_its_power_and_its_signal()
    {
        // Not the signal alone: the systems object's power byte gates the pointer too.
        var m = InMode(L.Geometry.Modes.Map);
        AdfPointer(m, 1, 20);
        m.B(M.At(M.Misc, P.AdfPower, 1), false);
        Assert.Empty(PointerRows(m));
        AdfPointer(m, 1, 20);
        m.I(M.Sim + (uint)(L.Receivers.AdfStations + L.Receivers.AdfStride + P.AdfSignal), 0);
        Assert.Empty(PointerRows(m));
    }

    // ---- POS (the VOR and ADF lines, the IRS and GPS positions) ----

    private const uint GpsBlock = 0x3D0000;

    /// <summary>MAP with POS on; VOR 1 tuned at a station 5 NM away (its radial 89.6, the correction and the station's
    /// variation 0), whatever the switch; ADF 2 receiving at 20° from the nose; the IRS and GPS positions valid.</summary>
    private static M PosOn()
    {
        var m = InMode(L.Geometry.Modes.Map);
        m.B(M.At(M.Panel, L.Panel.ShowPos), true);
        var tu = L.Tuned;
        uint vor = M.Sim + (uint)tu.Stations;
        m.Text(vor + (uint)tu.Ident, "CAS");
        m.I(vor + (uint)tu.State, tu.TunedState);
        m.B(M.At(M.Misc, P.VorPower, 0), true);
        var at = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 5, 107.9);
        m.Position(vor + (uint)tu.Position, at.LatitudeDeg, at.LongitudeDeg);
        m.D(vor + (uint)P.Pos.StationRadial, 89.6);
        m.B(M.At(M.Misc, P.AdfPower, 1), true);
        m.I(M.Sim + (uint)(L.Receivers.AdfStations + L.Receivers.AdfStride + P.AdfSignal), 1);
        m.D(M.At(M.Glass, P.AdfBearing, 1), 20);
        var irs = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 0.2, 47.9);
        m.Position(M.Iru + (uint)P.Pos.IruPosition, irs.LatitudeDeg, irs.LongitudeDeg);
        m.U(M.Isdu + (uint)P.Pos.GpsArray, GpsBlock);
        var gps = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 0.1, 77.9);
        m.Position(GpsBlock + (uint)P.Pos.GpsPosition, gps.LatitudeDeg, gps.LongitudeDeg);
        return m;
    }

    private static IReadOnlyList<string> PosRows(M m) =>
        Rows(m).Where(r => (r.Contains(" line", StringComparison.Ordinal) || r.Contains(" position", StringComparison.Ordinal))
            && !r.Contains("heading bug", StringComparison.Ordinal) && !r.Contains("track line", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Pos_rows_in_map_with_pos_on()
    {
        Assert.Equal(new[]
        {
            "Map: VOR 1 line toward CAS, R-090, 30° right",
            "Map: ADF 2 line, A2, 20° right",
            "Map: IRS position, 0.2 NM, 30° left",
            "Map: GPS position, 0.1 NM, ahead",
        }, PosRows(PosOn()));
    }

    [Fact]
    public void A_vor_line_s_radial_is_corrected_by_the_station_s_variation_and_prints_0_as_360()
    {
        var m = PosOn();
        uint vor = M.Sim + (uint)L.Tuned.Stations;
        m.D(vor + (uint)P.Pos.StationCorrection, 2.5);
        var at = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 5, 107.9);
        m.Position(vor + (uint)L.Tuned.Position, at.LatitudeDeg, at.LongitudeDeg, variationDeg: -3);
        Assert.Contains("Map: VOR 1 line toward CAS, R-095, 30° right", PosRows(m));
        m.D(vor + (uint)P.Pos.StationRadial, -5.2);
        Assert.Contains("Map: VOR 1 line toward CAS, R-360, 30° right", PosRows(m));
    }

    [Fact]
    public void A_vor_line_s_radial_is_not_corrected_when_the_layout_has_no_correction()
    {
        // The MSFS 2020 file prints the simulator's radial as it is: its layout has no correction field.
        var m = PosOn();
        uint vor = M.Sim + (uint)L.Tuned.Stations;
        m.D(vor + (uint)P.Pos.StationCorrection!.Value, 2.5);
        var at = M.Destination(M.AircraftLatitude, M.AircraftLongitude, 5, 107.9);
        m.Position(vor + (uint)L.Tuned.Position, at.LatitudeDeg, at.LongitudeDeg, variationDeg: -3);
        var uncorrected = L with { Pointers = P with { Pos = P.Pos with { StationCorrection = null } } };
        var rows = T.Lines(m.Read(uncorrected), uncorrected).Where(r => r.Contains(" line", StringComparison.Ordinal)).ToList();
        Assert.Contains("Map: VOR 1 line toward CAS, R-090, 30° right", rows);
    }

    [Fact]
    public void A_vor_line_is_labelled_by_its_receiver_on_the_inboard_map()
    {
        var m = PosOn();
        m.I(M.Efis + (uint)L.Units.PageOffset, L.Units.MapPages[0]);
        Assert.Contains("Map: VOR 1 line toward CAS, V1, 30° right", PosRows(m));
    }

    [Fact]
    public void No_pos_rows_with_pos_off_outside_map_or_the_readout_invalid()
    {
        var m = PosOn();
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        Assert.DoesNotContain(PosRows(m), r => r.StartsWith("Map: VOR 1 line", StringComparison.Ordinal));
        Assert.Contains("Map: ADF 2 line, A2, 20° right", PosRows(m));
        m = PosOn();
        m.B(M.At(M.Panel, L.Panel.ShowPos), false);
        Assert.Empty(PosRows(m));
        Assert.Null(m.Read().Pos);
        m = PosOn();
        m.I(M.At(M.Panel, L.Panel.Mode), L.Geometry.Modes.App);
        Assert.Empty(PosRows(m));
        Assert.Null(m.Read().Pos);
    }

    [Fact]
    public void Pos_positions_are_read_only_with_pos_on()
    {
        // A GPS pointer outside the module is corrupt only while PMDG draws POS.
        var m = PosOn();
        m.U(M.Isdu + (uint)P.Pos.GpsArray, 0xFFFFFF00);
        using (var sampler = m.Sampler())
            Assert.False(sampler.TryRead(out _, out _));
        m.B(M.At(M.Panel, L.Panel.ShowPos), false);
        Assert.Null(m.Read().Pos);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Pointer_and_pos_rows_are_invariant_under_comma_cultures(string culture)
    {
        var m = PosOn();
        VorPointer(m, 0, 326.4);
        var invariant = T.Lines(m.Read(), L);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Assert.Equal(invariant, T.Lines(m.Read(), L));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
        Assert.Contains("Map: VOR 1 pointer, 34° left", invariant);
        Assert.Contains("Map: VOR 1 line toward CAS, R-090, 30° right", invariant);
        Assert.Contains("Map: IRS position, 0.2 NM, 30° left", invariant);
    }

    [Fact]
    public void A_vor_line_s_radial_is_not_checked_while_the_readout_is_invalid()
    {
        // PMDG draws no VOR line with the readout's copy clear (a normal ground state): a radial PMDG does not
        // print never makes the reading corrupt (WASM-14); with the readout valid it does.
        var m = PosOn();
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), false);
        m.D(M.Sim + (uint)(L.Tuned.Stations + P.Pos.StationRadial), double.NaN);
        using (var sampler = m.Sampler())
            Assert.True(sampler.TryRead(out _, out var corrupt), corrupt);
        m.B(M.At(M.Glass, L.Orientation.ReadoutValid), true);
        using (var sampler = m.Sampler())
        {
            Assert.False(sampler.TryRead(out _, out var corrupt));
            Assert.Contains("radial", corrupt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pos_on_without_the_part_says_why()
    {
        var m = InMode(L.Geometry.Modes.Map);
        m.B(M.At(M.Panel, L.Panel.ShowPos), true);
        Assert.Contains("Pointers and POS: not read: PMDG file not recognised", T.Lines(m.Read(Broken), Broken));
    }

    [Fact]
    public void Reads_a_pointer_s_bearing_only_while_drawn()
    {
        var m = InMode(L.Geometry.Modes.Map);
        Switch(m, 0, 1);
        m.D(M.At(M.Glass, P.VorBearing, 0), double.NaN);
        var s = m.Read();
        Assert.Equal(2, s.Pointers!.Count);
        Assert.Equal(0, s.Pointers[0].Bearing);
        VorPointer(m, 0, double.NaN);
        using var sampler = m.Sampler();
        Assert.False(sampler.TryRead(out _, out var corrupt));
        Assert.Contains("bearing", corrupt, StringComparison.Ordinal);
    }
}
