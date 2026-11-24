// The 757 EHSI's TCAS (plan 28): the messages on the left and TRAFFIC on the right, read every tick and spoken once; the
// traffic list, read with the full set, walked all or nothing and listed in MAP where the screen draws each symbol, never
// spoken. The values are plan 28's live measurements (the knob at TA ONLY on the ground, AI traffic at the KSEA gate),
// placed through the test layout.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiTcasTests
{
    private static readonly Bbs757EhsiLayout P = Bbs757EhsiApproachTests.P;
    private static Bbs757EhsiTcas T => P.Tcas!;

    // Where this test's memory puts the TCAS computer and its list (beside the approach's objects).
    private const uint Computer = 0x22000, List = 0x23000;

    /// <summary>One traffic entry as BlueBird keeps it.</summary>
    internal sealed record Entry(int Kind, int Trend, double Feet, double Lat, double Lon);

    /// <summary>The TCAS computer's memory, written over a <see cref="Scene"/> through its Tweak.</summary>
    internal sealed class Tcas
    {
        public bool HasComputer = true, Powered = true, Failed, Ta, Ra, Test, Tfc, Gate = true;
        public int Level = 1;
        public Entry[] Traffic = Array.Empty<Entry>();
        public Action<byte[]>? Tweak;

        public void Write(byte[] b)
        {
            I32(b, Nd + T.NdToTcas, HasComputer ? Computer : 0);
            Flag(b, Nd + T.TrafficSwitch, Tfc);
            Flag(b, Nd + T.MessagesAllowed, Gate);
            Flag(b, Computer + T.TcasPowered, Powered);
            Flag(b, Computer + T.TcasFailed, Failed);
            I32(b, Computer + T.TcasLevel, Level);
            Flag(b, Computer + T.TrafficAdvisory, Ta);
            Flag(b, Computer + T.ResolutionAdvisory, Ra);
            Flag(b, Computer + T.TcasTest, Test);
            uint end = List + (uint)(Traffic.Length * T.TrafficStride);
            I32(b, Computer + T.TrafficList, Traffic.Length == 0 ? 0 : List);
            I32(b, Computer + T.TrafficList + 4, Traffic.Length == 0 ? 0 : end);
            I32(b, Computer + T.TrafficList + 8, Traffic.Length == 0 ? 0 : end);
            for (int i = 0; i < Traffic.Length; i++)
            {
                uint at = List + (uint)(i * T.TrafficStride);
                var e = Traffic[i];
                I32(b, at + T.TrafficKind, e.Kind);
                I32(b, at + T.TrafficTrend, e.Trend);
                F64(b, at + T.TrafficAltitude, e.Feet);
                F64(b, at + 16, -1);   // BlueBird's unused field
                F64(b, at + T.TrafficLongitude, e.Lon);
                F64(b, at + T.TrafficLatitude, e.Lat);
            }
            Tweak?.Invoke(b);
        }
    }

    /// <summary>The scene's map frame: the aircraft at the gate, the map's rotation (the true heading) 134.43.</summary>
    private const double CenterLat = 47.442946, CenterLon = -122.303648, Rotation = 134.43;

    /// <summary>The point <paramref name="nm"/> from the map's center at <paramref name="off"/> degrees off the screen's up
    /// (minus left), on the projection's sphere.</summary>
    internal static (double Lat, double Lon) At(double nm, double off)
    {
        const double R = 6371.0 / 1.852, D = Math.PI / 180;
        double d = nm / R, b = (Rotation + off) * D, p1 = CenterLat * D, l1 = CenterLon * D;
        double p2 = Math.Asin(Math.Sin(p1) * Math.Cos(d) + Math.Cos(p1) * Math.Sin(d) * Math.Cos(b));
        double l2 = l1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(p1), Math.Cos(d) - Math.Sin(p1) * Math.Sin(p2));
        return (p2 / D, l2 / D);
    }

    private static Entry E(int kind, int trend, double feet, double nm, double off)
    {
        var (lat, lon) = At(nm, off);
        return new Entry(kind, trend, feet, lat, lon);
    }

    /// <summary>Plan 28's capture against memory (re-placed in this scene's frame): a hollow diamond, a filled one climbing,
    /// the taxiing TA right behind, and an RA descending; in BlueBird's order, which is not by distance.</summary>
    private static Entry[] Live() => new[]
    {
        E(0, 0, 739, 8.73, -37.6),
        E(1, 1, 1065, 3.76, -10.9),
        E(2, 0, 5, 0.2, 160),
        E(3, 2, -340, 5.0, 30),
    };

    private static Bbs757EhsiReading Read(Tcas tcas, int mode = 2, bool full = true, Action<Scene>? scene = null)
    {
        var s = new Scene { Mode = mode, Range = 20, Tweak = tcas.Write };
        scene?.Invoke(s);
        return Bbs757EhsiReaderTests.Read(s, full, P);
    }

    private static string[] Left(Bbs757EhsiReading r) =>
        Bbs757EhsiText.Rows(r).Where(row => row.StartsWith("Left side: ", StringComparison.Ordinal)
            || row.StartsWith("Right side: TRAFFIC", StringComparison.Ordinal)).ToArray();

    private static string[] TrafficRows(Bbs757EhsiReading r) =>
        Bbs757EhsiText.Rows(r).Where(row => row.StartsWith("Map: ", StringComparison.Ordinal)
            && (row.Contains("traffic", StringComparison.Ordinal) || row.Contains("advisory", StringComparison.Ordinal)
                || row.Contains("TCAS", StringComparison.Ordinal))).ToArray();

    private static string[] Texts(IReadOnlyList<Bbs757SpokenLine> lines) => lines.Select(l => l.Text).ToArray();

    // ---- the traffic ----

    // Review Focus: the list is BlueBird's (changing every read, not by distance); the rows are in the window's screen
    // order, merged with the map's other items, and none of it is ever spoken.
    [Fact]
    public void Traffic_is_listed_in_screen_order_and_not_spoken()
    {
        var reading = Read(new Tcas { Tfc = true, Traffic = Live() });
        Assert.False(reading.Corrupt);
        Assert.Equal(new[]
        {
            "Map: traffic, 8.7 NM, 38° left, +07",
            "Map: resolution advisory, 5.0 NM, 30° right, -03 descending",
            "Map: proximate traffic, 3.8 NM, 11° left, +10 climbing",
            "Map: traffic advisory, 0.2 NM, 160° right, +00",
        }, TrafficRows(reading));
        // Among the map's own items: the TA just behind the aircraft is above the gate's airport, further behind.
        var map = Bbs757EhsiText.Rows(reading).Where(r => r.StartsWith("Map: ", StringComparison.Ordinal)).ToList();
        Assert.True(map.IndexOf("Map: traffic advisory, 0.2 NM, 160° right, +00") < map.IndexOf("Map: KSEA airport, 0.5 NM, 173° left"));

        // The list moving every read, or emptying, says nothing.
        var moved = Read(new Tcas { Tfc = true, Traffic = Live().Reverse().Select(e => e with { Feet = e.Feet + 40 }).ToArray() });
        var empty = Read(new Tcas { Tfc = true });
        Assert.Empty(Bbs757EhsiText.Changes(empty, reading, true));
        Assert.Empty(Bbs757EhsiText.Changes(reading, moved, true));
        Assert.Empty(Bbs757EhsiText.Changes(moved, empty, true));
    }

    [Fact]
    public void Other_and_proximate_traffic_show_only_with_tfc()
    {
        Assert.Equal(new[]
        {
            "Map: resolution advisory, 5.0 NM, 30° right, -03 descending",
            "Map: traffic advisory, 0.2 NM, 160° right, +00",
        }, TrafficRows(Read(new Tcas { Traffic = Live() })));
    }

    [Fact]
    public void The_tag_is_the_screens_hundreds_of_feet()
    {
        var tags = new[] { 739.0, 5, 0, -340, -50, 2525, -1000, 10400 }
            .Select(feet => E(2, 0, feet, 2, 0)).ToArray();
        var rows = TrafficRows(Read(new Tcas { Traffic = tags }));
        Assert.Equal(new[] { "+07", "+00", "+00", "-03", "-00", "+25", "-10", "+104" },
            rows.Select(r => r[(r.LastIndexOf(", ", StringComparison.Ordinal) + 2)..]).ToArray());
    }

    [Fact]
    public void Traffic_only_where_the_screen_draws_it()
    {
        var far = new[] { E(2, 0, 100, 30, 0), E(2, 0, 100, 19, 90), E(2, 0, 100, 2, 0) };
        // Range 20: 30 NM ahead is past the map's edge and 19 NM abeam past the screen's; 2 NM ahead is drawn.
        Assert.Equal(new[] { "Map: traffic advisory, 2.0 NM, ahead, +01" }, TrafficRows(Read(new Tcas { Traffic = far })));
        // MAP only: none in VOR, ILS or PLAN, none with the IRS not valid, none in a light reading (no list read).
        foreach (int mode in new[] { 0, 1, 3 })
            Assert.Empty(TrafficRows(Read(new Tcas { Traffic = far }, mode)));
        Assert.Empty(TrafficRows(Read(new Tcas { Traffic = far }, scene: s => s.Valid = false)));
        Assert.Null(Read(new Tcas { Traffic = far }, full: false).Tcas!.Traffic);
        Assert.Empty(TrafficRows(Read(new Tcas { Traffic = far }, full: false)));
    }

    [Fact]
    public void The_self_test_shows_its_pattern_not_the_list()
    {
        var reading = Read(new Tcas { Test = true, Ra = true, Tfc = true, Traffic = Live() });
        Assert.Equal(new[] { "Map: TCAS test pattern" }, TrafficRows(reading));
        Assert.Equal(new[] { "Right side: TRAFFIC, red", "Left side: TFC", "Left side: TEST", "Left side: TA ONLY" }, Left(reading));
    }

    // ---- the messages ----

    [Fact]
    public void Each_message_shows_on_its_own_condition()
    {
        string[] Rows(Tcas t, int mode = 2, Action<Scene>? scene = null) => Left(Read(t, mode, full: false, scene));

        // TA ONLY, with TFC above it while the switch is on; TA/RA shows TFC alone.
        Assert.Equal(new[] { "Left side: TA ONLY" }, Rows(new Tcas()));
        Assert.Equal(new[] { "Left side: TFC", "Left side: TA ONLY" }, Rows(new Tcas { Tfc = true }));
        Assert.Equal(new[] { "Left side: TFC" }, Rows(new Tcas { Tfc = true, Level = 2 }));
        // Off: any level but BlueBird's two.
        Assert.Equal(new[] { "Left side: TCAS OFF" }, Rows(new Tcas { Level = 0, Tfc = true }));
        Assert.Equal(new[] { "Left side: TCAS OFF" }, Rows(new Tcas { Level = 5 }));
        // FAIL (failed, or not powered) and nothing below it.
        Assert.Equal(new[] { "Left side: TCAS FAIL" }, Rows(new Tcas { Failed = true, Tfc = true }));
        Assert.Equal(new[] { "Left side: TCAS FAIL" }, Rows(new Tcas { Powered = false, Level = 0 }));
        // TEST over whichever is drawn, after its first line.
        Assert.Equal(new[] { "Left side: TCAS OFF", "Left side: TEST" }, Rows(new Tcas { Level = 0, Test = true }));
        // TRAFFIC: amber for a TA, red for an RA, in every mode, only while the IRS is valid (expanded: above the left
        // block, so first).
        Assert.Equal(new[] { "Right side: TRAFFIC, amber", "Left side: TA ONLY" }, Rows(new Tcas { Ta = true }));
        Assert.Equal(new[] { "Right side: TRAFFIC, red", "Left side: TA ONLY" }, Rows(new Tcas { Ta = true, Ra = true }));
        Assert.Equal(new[] { "Right side: TRAFFIC, amber", "Left side: TA ONLY" }, Rows(new Tcas { Ta = true }, mode: 3));
        Assert.Equal(new[] { "Left side: TA ONLY" }, Rows(new Tcas { Ta = true }, scene: s => s.Valid = false));
        // No TCAS computer: nothing at all.
        Assert.Empty(Rows(new Tcas { HasComputer = false, Ta = true, Test = true }));
    }

    // TRAFFIC sits at 0.38 of the height: above the left block when expanded (0.433), below it when centered (0.199).
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void Traffic_comes_before_the_left_block_when_expanded(int mode)
    {
        var all = Bbs757EhsiText.Rows(Read(new Tcas { Ta = true, Tfc = true }, mode, full: false));
        int traffic = Array.IndexOf(all.ToArray(), "Right side: TRAFFIC, amber");
        int left = Array.IndexOf(all.ToArray(), "Left side: TFC");
        Assert.True(traffic > 0 && left > 0);
        Assert.Equal(left - 1, traffic);
        Assert.Equal(new[] { "Right side: TRAFFIC, amber", "Left side: TFC", "Left side: TA ONLY" }, Left(Read(new Tcas { Ta = true, Tfc = true }, mode, full: false)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void Traffic_comes_after_the_left_block_when_centered(int mode)
    {
        var r = Read(new Tcas { Ta = true, Tfc = true }, mode, full: false, scene: s => s.Centered = true);
        // The gate byte is this test's memory, set (BlueBird clears it in full VOR; the order is the same either way).
        Assert.Equal(new[] { "Left side: TFC", "Left side: TA ONLY", "Right side: TRAFFIC, amber" }, Left(r));
    }

    [Fact]
    public void The_nds_message_gate_holds_tfc_off_and_fail_back()
    {
        // Full VOR or ILS and PLAN clear the gate: TA ONLY, TEST and TRAFFIC still show.
        string[] Rows(Tcas t) => Left(Read(t, mode: 0, full: false));
        Assert.Equal(new[] { "Left side: TA ONLY" }, Rows(new Tcas { Gate = false, Tfc = true }));
        Assert.Empty(Rows(new Tcas { Gate = false, Level = 0 }));
        Assert.Equal(new[] { "Right side: TRAFFIC, red", "Left side: TEST" }, Rows(new Tcas { Gate = false, Level = 0, Test = true, Ra = true }));
    }

    // TCAS FAIL is drawn, and hides the rest of the left block, only while the gate is set; with the gate clear a failed
    // TCAS falls through to the level, as a working one does.
    [Fact]
    public void A_failed_tcas_with_the_gate_clear_shows_its_level()
    {
        string[] Rows(Tcas t) => Left(Read(t, mode: 0, full: false));
        foreach (var failure in new Action<Tcas>[] { t => t.Failed = true, t => t.Powered = false })
        {
            Tcas T(int level)
            {
                var t = new Tcas { Gate = false, Tfc = true, Level = level };
                failure(t);
                return t;
            }
            // Level 0: TCAS OFF is gated, so nothing; level 1: TA ONLY (ungated); level 2: TFC is gated, so nothing.
            Assert.Empty(Rows(T(0)));
            Assert.Equal(new[] { "Left side: TA ONLY" }, Rows(T(1)));
            Assert.Empty(Rows(T(2)));
            // The gate set: FAIL alone, at every level.
            foreach (int level in new[] { 0, 1, 2 })
            {
                var gated = T(level);
                gated.Gate = true;
                Assert.Equal(new[] { "Left side: TCAS FAIL" }, Rows(gated));
            }
        }

        // The pilot turning from expanded to full VOR with a failed TCAS at TA ONLY: FAIL goes and TA ONLY comes only
        // because of the gate: nothing said, either way.
        var gateSet = Read(new Tcas { Failed = true }, mode: 0, full: false);
        var gateClear = Read(new Tcas { Failed = true, Gate = false }, mode: 0, full: false);
        Assert.Empty(Bbs757EhsiText.Changes(gateSet, gateClear, true));
        Assert.Empty(Bbs757EhsiText.Changes(gateClear, gateSet, true));
        // The failure itself, under a clear gate, changes nothing drawn at TA ONLY: nothing said.
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Tcas { Gate = false }, mode: 0, full: false), gateClear, true));
    }

    // Review Focus: the pilot turning the TCAS knob from STBY to TA ONLY: one line each way, once; the pilot changing the
    // EFIS mode (which only moves the gate) says nothing.
    [Fact]
    public void A_tcas_mode_message_is_spoken_once()
    {
        var off = Read(new Tcas { Level = 0 }, full: false);
        var taOnly = Read(new Tcas(), full: false);
        var lines = Bbs757EhsiText.Changes(off, taOnly, true);
        Assert.All(lines, l => Assert.Equal(Bbs757EhsiDisplay.MuteKey, l.MuteKey));
        Assert.Equal(new[] { "EHSI message: TA ONLY", "EHSI message gone: TCAS OFF" }, Texts(lines));
        Assert.Empty(Bbs757EhsiText.Changes(taOnly, taOnly, true));
        Assert.Empty(Bbs757EhsiText.Changes(taOnly, Read(new Tcas(), full: true), true));
        Assert.Equal(new[] { "EHSI message: TCAS OFF", "EHSI message gone: TA ONLY" }, Texts(Bbs757EhsiText.Changes(taOnly, off, true)));

        // TRAFFIC and a failure come by themselves: spoken.
        Assert.Equal(new[] { "EHSI message: TRAFFIC" }, Texts(Bbs757EhsiText.Changes(taOnly, Read(new Tcas { Ta = true }, full: false), true)));
        Assert.Equal(new[] { "EHSI message: TCAS FAIL", "EHSI message gone: TA ONLY" },
            Texts(Bbs757EhsiText.Changes(taOnly, Read(new Tcas { Failed = true }, full: false), true)));
        // TFC is the pilot's switch and TEST the pilot's knob: shown, not spoken. The self-test sets the RA byte as
        // BlueBird's does (red TRAFFIC for its ten seconds): TRAFFIC is listed but neither spoken nor spoken gone.
        Assert.Empty(Bbs757EhsiText.Changes(taOnly, Read(new Tcas { Tfc = true }, full: false), true));
        var selfTest = Read(new Tcas { Test = true, Ra = true }, full: false);
        Assert.Contains("Right side: TRAFFIC, red", Left(selfTest));
        Assert.Empty(Bbs757EhsiText.Changes(taOnly, selfTest, true));
        Assert.Empty(Bbs757EhsiText.Changes(selfTest, taOnly, true));

        // From expanded MAP to full VOR the gate clears and TCAS OFF goes: nothing said, either way.
        var offInFullVor = Read(new Tcas { Level = 0, Gate = false }, mode: 0, full: false);
        Assert.Empty(Bbs757EhsiText.Changes(off, offInFullVor, true));
        Assert.Empty(Bbs757EhsiText.Changes(offInFullVor, off, true));
    }

    // Re-review finding 1: a part at its tear limit is null ("not read: unexpected values") for a while and then reads
    // again. Its messages neither go nor come back with it: the screen did not change, the reader lost sight of it.
    [Fact]
    public void A_tcas_part_dropping_out_and_back_speaks_nothing()
    {
        var showing = Read(new Tcas { Ta = true });
        Assert.Contains("Right side: TRAFFIC, amber", Left(showing));
        Assert.Contains("Left side: TA ONLY", Left(showing));
        var dropped = showing with { Tcas = null, TcasMissing = Bbs757EhsiReader.UnexpectedValues };
        Assert.Empty(Bbs757EhsiText.Changes(showing, dropped, true));
        Assert.Empty(Bbs757EhsiText.Changes(dropped, dropped, true));
        Assert.Empty(Bbs757EhsiText.Changes(dropped, showing, true));
        // Back with a different state: the new state is the baseline, silently (nothing was heard about it going).
        var cleared = Read(new Tcas());
        Assert.Empty(Bbs757EhsiText.Changes(dropped, cleared, true));
        // A real change while the part is read in both is still spoken, once.
        Assert.Equal(new[] { "EHSI message gone: TRAFFIC" }, Texts(Bbs757EhsiText.Changes(showing, cleared, true)));
        Assert.Equal(new[] { "EHSI message: TRAFFIC" }, Texts(Bbs757EhsiText.Changes(cleared, showing, true)));
    }

    // ---- all or nothing ----

    [Fact]
    public void A_torn_traffic_list_is_corrupt()
    {
        void Corrupt(Tcas t)
        {
            var reading = Read(t);
            Assert.True(reading.Corrupt);
            Assert.Null(reading.Tcas);
            // A light reading does not walk the list.
            Assert.False(Read(t, full: false).Corrupt);
        }

        uint header = Computer + (uint)T.TrafficList;
        Corrupt(new Tcas { Traffic = Live(), Tweak = b => I32(b, header + 4, List + 3 * 40 + 8) });    // not whole entries
        Corrupt(new Tcas { Traffic = Live(), Tweak = b => I32(b, header + 4, List - 40) });            // end before begin
        Corrupt(new Tcas { Traffic = Live(), Tweak = b => { I32(b, header, Outside); I32(b, header + 4, Outside + 80); } });
        Corrupt(new Tcas { Traffic = Live(), Tweak = b => I32(b, header, 0) });                        // storage null
        Corrupt(new Tcas { Traffic = Live().Append(Live()[0] with { Kind = 4 }).ToArray() });
        Corrupt(new Tcas { Traffic = Live().Append(Live()[0] with { Feet = double.NaN }).ToArray() });
        Corrupt(new Tcas { Traffic = Live().Append(Live()[0] with { Lat = 95 }).ToArray() });
        Corrupt(new Tcas { Traffic = Live().Append(Live()[0] with { Lon = double.PositiveInfinity }).ToArray() });

        // A computer pointer outside the module is torn in every reading; a null one is no TCAS, not corrupt.
        var outside = Read(new Tcas { Tweak = b => I32(b, Nd + T.NdToTcas, Outside) }, full: false);
        Assert.True(outside.Corrupt);
        Assert.False(Read(new Tcas { HasComputer = false }).Corrupt);
        // An empty list is no traffic, not corrupt.
        Assert.Empty(Read(new Tcas { Tfc = true }).Tcas!.Traffic!);
    }

    [Fact]
    public void More_traffic_than_the_cap_is_corrupt()
    {
        var thirty = Enumerable.Range(0, T.MaxTraffic).Select(i => E(0, 0, 100 * i, 1 + i * 0.1, 0)).ToArray();
        Assert.Equal(T.MaxTraffic, Read(new Tcas { Traffic = thirty }).Tcas!.Traffic!.Count);
        var reading = Read(new Tcas { Traffic = thirty.Append(thirty[0]).ToArray() });
        Assert.True(reading.Corrupt);
    }

    [Fact]
    public void The_reading_carries_the_tcas_as_stored()
    {
        var t = Read(new Tcas { Level = 2, Ta = true, Tfc = true, Traffic = Live() }).Tcas!;
        Assert.True(t.Computer);
        Assert.Equal(Bbs757EhsiTcasLevel.TaRa, t.Level);
        Assert.Equal(new Bbs757EhsiTcasWords("TCAS", "FAIL", "OFF", "TFC", "TA ONLY", "TEST", "TRAFFIC"), t.Words);
        Assert.Equal(100, t.TagDivisor);
        Assert.Equal(new[] { Bbs757EhsiTrafficKind.Other, Bbs757EhsiTrafficKind.Proximate, Bbs757EhsiTrafficKind.Advisory, Bbs757EhsiTrafficKind.Resolution },
            t.Traffic!.Select(x => x.Kind));
        Assert.Equal(new[] { Bbs757EhsiTrafficTrend.None, Bbs757EhsiTrafficTrend.Climbing, Bbs757EhsiTrafficTrend.None, Bbs757EhsiTrafficTrend.Descending },
            t.Traffic!.Select(x => x.Trend));
    }
}
