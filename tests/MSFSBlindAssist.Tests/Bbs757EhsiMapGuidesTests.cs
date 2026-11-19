// The 757 EHSI's map guides (plan 28): the FIX pages' fixes, circles and radials, the off-path descent circles, the
// altitude range arc and the VNAV path deviation scale, read in a full reading and listed where the screen draws them.
// Nothing here is spoken. The values are plan 28's live ones (FIX INFO TCM: a TACAN placed by its second position, the
// entries 300/300 and /15; the OFFPATH DES point 45.5948, -122.5956), placed through the test layout.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiMapGuidesTests
{
    private static readonly Bbs757EhsiLayout P = Bbs757EhsiApproachTests.P;
    private static Bbs757EhsiMapGuides G => P.MapGuides!;

    // Where this test's memory puts the map guides' objects (the FMS is the map options' one).
    private const uint Fms = Bbs757EhsiMapOptionsTests.Fms, FixObject1 = 0x34000, FixObject2 = 0x34800, Entries1 = 0x35000,
        Entries2 = 0x35100, Nodes = 0x35200, Adc = 0x36000, AirGround = 0x36800;

    /// <summary>The scene's route path (System.IO's Path shadows the reader tests' name).</summary>
    private const uint PathAt = Bbs757EhsiReaderTests.Path;

    /// <summary>One FIX page's fix: its name, its kind, its two positions, its capability set and its entries (radial,
    /// distance; -1 none).</summary>
    internal sealed record Fix(string Name, int Kind, (double Lat, double Lon) Primary, (double Lat, double Lon) Second,
        int[] Capabilities, (int Radial, int Distance)[] Entries);

    /// <summary>FIX INFO's TCM as measured: a TACAN (kind 2) whose set {9, 19, 18, 3} picks its second position, the
    /// primary (0, 0); "300/300" on line 2 and "/15" on line 3.</summary>
    internal static readonly Fix Tcm = new("TCM", 2, (0, 0), (47.14767, -122.47504), new[] { 9, 19, 18, 3 },
        new[] { (300, 300), (-1, 15) });

    /// <summary>The map guides' memory as BlueBird keeps it, written over a <see cref="Scene"/> through its Tweak.</summary>
    internal sealed class Guides
    {
        public bool HasFms = true, Fix1On = true, Fix2On = true;
        public Fix? Fix1, Fix2;
        public bool OffpathOn, ValidA = true, ValidB = true;
        public (double Lat, double Lon) OffpathPoint = (45.5948, -122.5956);
        public double Clean = 45, Speedbrake = 38;
        public bool HasAdc = true, HasAirGround = true, OnGround;
        public double Selected, Altitude, VerticalSpeed, Track = 134.43;
        public int Phase;
        public double Deviation;
        public Action<byte[]>? Tweak;

        public void Write(byte[] b)
        {
            I32(b, Nd + G.NdToFms, HasFms ? Fms : 0);
            Flag(b, Fms + G.FmsFix1On, Fix1On);
            Flag(b, Fms + G.FmsFix2On, Fix2On);
            PutFix(b, Fms + (uint)G.FmsFix1, FixObject1, Fms + (uint)G.FmsFix1Entries, Entries1, Nodes, Fix1);
            PutFix(b, Fms + (uint)G.FmsFix2, FixObject2, Fms + (uint)G.FmsFix2Entries, Entries2, Nodes + 0x100, Fix2);

            Flag(b, Fms + G.OffpathOn, OffpathOn);
            Flag(b, Fms + G.OffpathValidA, ValidA);
            Flag(b, Fms + G.OffpathValidB, ValidB);
            F64(b, Fms + G.OffpathLatitude, OffpathPoint.Lat);
            F64(b, Fms + G.OffpathLongitude, OffpathPoint.Lon);
            F64(b, Fms + G.OffpathCleanRadius, Clean);
            F64(b, Fms + G.OffpathSpeedbrakeRadius, Speedbrake);

            F64(b, Mcp + G.McpAltitude, Selected);
            I32(b, Source + G.SourceToAdc, HasAdc ? Adc : 0);
            F64(b, Adc + G.AdcIndicatedAltitude, Altitude);
            F64(b, Adc + G.AdcVerticalSpeed, VerticalSpeed);
            I32(b, Nd + G.NdToAirGround, HasAirGround ? AirGround : 0);
            Flag(b, AirGround + G.AirGroundOnGround, OnGround);
            F64(b, Irs + G.IrsTrueTrack, Track);

            I32(b, PathAt + G.PathPhase, Phase);
            F64(b, PathAt + G.PathDeviation, Deviation);
            Tweak?.Invoke(b);
        }

        private static void PutFix(byte[] b, uint pointer, uint at, uint header, uint entries, uint nodes, Fix? fix)
        {
            I32(b, pointer, fix is null ? 0 : at);
            if (fix is null) return;
            var l = P;
            I32(b, at + l.LegKind, fix.Kind);
            // The primary where plan 26's rule reads it for this kind (its LegPositions entry, else the plain offsets).
            var special = l.LegPositions.Where(p => p.Kind == fix.Kind).Select(p => (Bbs757EhsiLegPosition?)p).FirstOrDefault();
            F64(b, at + (special?.Latitude ?? l.LegLatitude), fix.Primary.Lat);
            F64(b, at + (special?.Longitude ?? l.LegLongitude), fix.Primary.Lon);
            F64(b, at + 880, fix.Second.Lat);
            F64(b, at + 888, fix.Second.Lon);
            var x = P.MapExtras!;
            I32(b, at + x.LegCapabilityFirst, fix.Capabilities.Length == 0 ? 0 : nodes);
            I32(b, at + x.LegCapabilityCount, fix.Capabilities.Length);
            for (int i = 0; i < fix.Capabilities.Length; i++)
            {
                uint node = nodes + (uint)(i * 16);
                I32(b, node + x.CapabilityNodeNext, i + 1 < fix.Capabilities.Length ? node + 16 : 0);
                I32(b, node + x.CapabilityNodeValue, fix.Capabilities[i]);
            }
            // The name as a narrow string where the draw takes it; the other one holds a decoy.
            bool navaid = G.NavaidLegKinds.Contains(fix.Kind);
            PutNarrow(b, at + (uint)(navaid ? G.LegNavaidName : G.LegName), fix.Name);
            PutNarrow(b, at + (uint)(navaid ? G.LegName : G.LegNavaidName), "DECOY");

            int bytes = fix.Entries.Length * G.FixEntryStride;
            I32(b, header, bytes == 0 ? 0 : entries);
            I32(b, header + 4, bytes == 0 ? 0 : entries + bytes);
            I32(b, header + 8, bytes == 0 ? 0 : entries + bytes);
            for (int i = 0; i < fix.Entries.Length; i++)
            {
                I32(b, entries + i * G.FixEntryStride + G.FixEntryRadial, fix.Entries[i].Radial);
                I32(b, entries + i * G.FixEntryStride + G.FixEntryDistance, fix.Entries[i].Distance);
            }
        }
    }

    /// <summary>A short narrow std::string (up to 10 characters inline, byte 11 the size).</summary>
    private static void PutNarrow(byte[] b, uint at, string text)
    {
        for (int i = 0; i < 12; i++) b[at + i] = 0;
        for (int i = 0; i < text.Length; i++) b[at + i] = (byte)text[i];
        b[at + 11] = (byte)text.Length;
    }

    internal static Bbs757EhsiReading Read(Guides guides, int mode = 2, double range = 40, bool full = true,
        Action<Scene>? scene = null, Bbs757EhsiLayout? layout = null)
    {
        var s = new Scene { Mode = mode, Range = range, Tweak = guides.Write };
        scene?.Invoke(s);
        using var r = Reader(s.Build(), full, layout: layout ?? P);
        Assert.True(r.TryRead(out var reading));
        return reading;
    }

    private static string[] Rows(Bbs757EhsiReading r) => Bbs757EhsiText.Rows(r).ToArray();

    private static string[] Starting(Bbs757EhsiReading r, string prefix) =>
        Rows(r).Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

    /// <summary>Plan 26's map rotation with the scene's other fields, for a case that needs another up.</summary>
    private static Action<Scene> Rotated(double rotation, double? number = null) => s =>
    {
        var tweak = s.Tweak;
        if (number is { } n) s.Number = n;
        s.Tweak = b =>
        {
            tweak?.Invoke(b);
            F64(b, Nd + P.Rotation, rotation);
        };
    };

    // ---- the FIX page ----

    [Fact]
    public void The_live_fix_lists_its_point_circles_and_radial_where_drawn()
    {
        // TCM is 19.1 NM from the gate, 67° right of the map's up (bearing 201.5° true against the rotation 134.43). At range
        // 40 its point, its 15 NM circle and its radial 300 (300 - 119 = 181° off the screen's up: down, from the point at
        // y 594 to the screen's bottom) are on the screen; the 300 NM circle passes 281 NM away and is not.
        var reading = Read(new Guides { Fix1 = Tcm });
        Assert.False(reading.Corrupt);
        Assert.Equal(new[] { "Map: fix TCM, 19.1 NM, 67° right", "Map: fix TCM, circle 15 NM", "Map: fix TCM, radial 300" }
            .OrderBy(s => s).ToArray(), Starting(reading, "Map: fix").OrderBy(s => s).ToArray());
        var fix = reading.MapGuides!.Fixes.Single();
        Assert.Equal("TCM", fix.Name);
        Assert.Equal(new Bbs757EhsiMapPoint(47.14767, -122.47504), fix.Position);
        Assert.Equal(new[] { new Bbs757EhsiFixEntry(300, 300), new Bbs757EhsiFixEntry(null, 15) }, fix.Entries);

        // The 300 NM circle where its near side is on the screen: range 320 with the map turned so that side (21.5° true
        // from the aircraft, 281 NM) is straight up. The margin is small: the nearest sample (the circle's 20° point) is
        // 511.0 px from the aircraft against the clip's 516.0 px (859 × 0.6775 - 66), 5.0 px or 2.7 NM inside.
        Assert.Contains("Map: fix TCM, circle 300 NM", Starting(Read(new Guides { Fix1 = Tcm }, range: 320, scene: Rotated(21.5)), "Map: fix"));

        // Never spoken; the light reading reads no guides; VOR and ILS draw none.
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Guides()), reading, true));
        Assert.Null(Read(new Guides { Fix1 = Tcm }, full: false).MapGuides);
        foreach (int mode in new[] { 0, 1 })
        {
            var vor = Read(new Guides { Fix1 = Tcm }, mode);
            Assert.Null(vor.MapGuides);
            Assert.Empty(Starting(vor, "Map: fix"));
        }
    }

    [Fact]
    public void A_circle_partly_on_the_screen_is_listed_and_one_wholly_off_is_not()
    {
        // Range 10: TCM itself is 1108 px away, off the screen; its 15 NM circle comes within 4 NM of the aircraft.
        var range10 = Read(new Guides { Fix1 = Tcm }, range: 10);
        Assert.Equal(new[] { "Map: fix TCM, circle 15 NM" }, Starting(range10, "Map: fix"));
        // A 2 NM circle around it stays 17 NM away: wholly off the screen.
        var small = Tcm with { Entries = new[] { (-1, 2) } };
        Assert.Empty(Starting(Read(new Guides { Fix1 = small }, range: 10), "Map: fix"));
    }

    // A large circle at a small range: 5-degree samples of a 300 NM circle are 26 NM apart, wider than the screen at range
    // 10 or 20, so the samples follow the circle's size on the screen.
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void A_large_circle_crossing_the_screen_at_a_small_range_is_listed(double range)
    {
        // A fix 295 NM behind the aircraft: its 300 NM circle passes 5 NM ahead, across the screen.
        var behind = Bbs757EhsiTcasTests.At(295, 180 - 2.5);
        var crossing = new Fix("FAR", 4, behind, (0, 0), Array.Empty<int>(), new[] { (-1, 300) });
        Assert.Equal(new[] { "Map: fix FAR, circle 300 NM" }, Starting(Read(new Guides { Fix1 = crossing }, range: range), "Map: fix"));
        // 270 NM behind: the circle passes 30 NM ahead, off the screen.
        var far = crossing with { Primary = Bbs757EhsiTcasTests.At(270, 180 - 2.5) };
        Assert.Empty(Starting(Read(new Guides { Fix1 = far }, range: range), "Map: fix"));
    }

    [Fact]
    public async Task A_radial_with_wild_dash_constants_ends()
    {
        // The sampler itself, whatever dash constants reach it (the layout check refuses these first: below): a dash
        // length or step that is not a finite positive number is no radial on the screen, the dash count and the samples
        // inside one dash are capped, so the rows end. TCM's radial 300 at range 10 points away from the screen.
        var radialOnly = Tcm with { Entries = new[] { (300, -1) } };
        var good = Read(new Guides { Fix1 = radialOnly }, range: 10);
        Assert.False(good.Corrupt);
        foreach (var dashes in new[]
                 {
                     new Bbs757EhsiRadialDashes(27, 18, 46, double.PositiveInfinity),
                     new Bbs757EhsiRadialDashes(27, 18, 46, double.NaN),
                     new Bbs757EhsiRadialDashes(27, 18, double.PositiveInfinity, 30),
                     new Bbs757EhsiRadialDashes(27, 18, 0, 30),
                     new Bbs757EhsiRadialDashes(27, int.MaxValue, 1e-9, 30),
                     new Bbs757EhsiRadialDashes(27, 18, 46, 1e15),
                     new Bbs757EhsiRadialDashes(27, int.MaxValue, 46, 1e15),
                 })
        {
            var wild = good with { MapGuides = good.MapGuides! with { Dashes = dashes } };
            var rows = Task.Run(() => Starting(wild, "Map: fix"));
            Assert.Same(rows, await Task.WhenAny(rows, Task.Delay(TimeSpan.FromSeconds(10))));   // the sampling ends
            Assert.Empty(await rows);
        }
    }

    [Fact]
    public void A_radial_in_map_is_listed_only_where_its_dashes_reach_the_screen()
    {
        // Range 10, TCM off the screen. With the heading number 119.39 the radial 300 points down at 181°, away: not
        // listed. With the heading number 53 it points at 247°, toward the aircraft, and its dashes (to 839 px) cross the
        // screen: listed.
        var radialOnly = Tcm with { Entries = new[] { (300, -1) } };
        Assert.Empty(Starting(Read(new Guides { Fix1 = radialOnly }, range: 10), "Map: fix"));
        Assert.Equal(new[] { "Map: fix TCM, radial 300" },
            Starting(Read(new Guides { Fix1 = radialOnly }, range: 10, scene: s => s.Number = 53), "Map: fix"));
        // Three digits as on a course: radial 5 reads 005.
        Assert.Contains("Map: fix TCM, radial 005", Starting(Read(new Guides { Fix1 = Tcm with { Entries = new[] { (5, -1) } } }), "Map: fix"));
    }

    [Fact]
    public void In_plan_a_radial_goes_with_its_fix_or_its_circle()
    {
        // PLAN has no magnetic variation to turn the radial by: listed whenever the fix's point or one of its circles is.
        var plan = Read(new Guides { Fix1 = Tcm }, mode: 3);
        var rows = Starting(plan, "Map: fix");
        Assert.Contains("Map: fix TCM, radial 300", rows);
        Assert.Contains(rows, r => r.StartsWith("Map: fix TCM, 19.1 NM ", StringComparison.Ordinal) && r.EndsWith(" of the center", StringComparison.Ordinal));
        // Off the screen with only its circle on: the radial still listed, even pointing away (the MAP rule would not).
        var range10 = Starting(Read(new Guides { Fix1 = Tcm }, mode: 3, range: 10), "Map: fix");
        Assert.Equal(new[] { "Map: fix TCM, circle 15 NM", "Map: fix TCM, radial 300" }.OrderBy(s => s), range10.OrderBy(s => s));
        // Neither on: no radial, whichever way it points.
        var radialOnly = Tcm with { Entries = new[] { (300, -1) } };
        Assert.Empty(Starting(Read(new Guides { Fix1 = radialOnly }, mode: 3, range: 10, scene: s => s.Number = 53), "Map: fix"));
    }

    [Fact]
    public void Both_fix_pages_are_listed()
    {
        // FIX 2 holds a waypoint (not a navaid kind: its name the leg's name string, its position plan 26's).
        var havho = new Fix("HAVHO", 4, Bbs757EhsiTcasTests.At(5, -30), (0, 0), Array.Empty<int>(), Array.Empty<(int, int)>());
        var reading = Read(new Guides { Fix1 = Tcm, Fix2 = havho });
        Assert.False(reading.Corrupt);
        Assert.Contains("Map: fix HAVHO, 5.0 NM, 30° left", Rows(reading));
        Assert.Contains("Map: fix TCM, circle 15 NM", Rows(reading));
        Assert.Equal(new[] { "TCM", "HAVHO" }, reading.MapGuides!.Fixes.Select(f => f.Name));
        // A page switched off, or with no fix, draws nothing (its pointer not even read).
        var off = Read(new Guides { Fix1 = Tcm, Fix2 = havho, Fix1On = false, Tweak = b => I32(b, Fms + G.FmsFix1, Outside) });
        Assert.False(off.Corrupt);
        Assert.Equal(new[] { "HAVHO" }, off.MapGuides!.Fixes.Select(f => f.Name));
        Assert.Empty(Read(new Guides { Fix2 = havho, Fix2On = false }).MapGuides!.Fixes);
        Assert.Empty(Read(new Guides()).MapGuides!.Fixes);
        // No FMS: no guides through it, not corrupt.
        var noFms = Read(new Guides { Fix1 = Tcm, HasFms = false });
        Assert.False(noFms.Corrupt);
        Assert.Empty(noFms.MapGuides!.Fixes);
    }

    [Fact]
    public void Without_the_map_extras_a_navaid_fix_is_listed_with_no_place()
    {
        // The leg position rule is the map extras': without it TCM's own offsets read (0, 0), so no place is given.
        var layout = P with { MapExtras = null, MapExtrasMissing = "terrain static not found" };
        var havho = new Fix("HAVHO", 4, Bbs757EhsiTcasTests.At(5, -30), (0, 0), Array.Empty<int>(), Array.Empty<(int, int)>());
        var reading = Read(new Guides { Fix1 = Tcm, Fix2 = havho }, layout: layout);
        Assert.False(reading.Corrupt);
        var rows = Starting(reading, "Map: fix");
        Assert.Equal(new[] { "Map: fix HAVHO, 5.0 NM, 30° left", "Map: fix TCM, position not read" }, rows);
        Assert.Equal("Map: fix TCM, position not read", Starting(reading, "Map: ").Last());
        Assert.Null(reading.MapGuides!.Fixes[0].Position);
    }

    [Fact]
    public void A_torn_fix_object_or_entries_vector_is_corrupt()
    {
        static bool Torn(Guides g, int mode = 2) => Read(g, mode).Corrupt;
        Assert.False(Torn(new Guides { Fix1 = Tcm }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Fms + G.FmsFix1, Outside) }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Nd + G.NdToFms, Outside) }));
        // The entries: end before begin, not whole entries, a radial or distance out of range, storage outside the module
        // (a vector longer than the draw takes included).
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Fms + G.FmsFix1Entries + 4, Entries1 - 8) }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Fms + G.FmsFix1Entries + 4, Entries1 + 12) }));
        Assert.False(Torn(new Guides { Fix1 = Tcm with { Entries = new[] { (1, 1), (2, 2), (3, 3) } } }));
        uint pastModule = Entries1 + (uint)(((Size - Entries1) / G.FixEntryStride + 1) * G.FixEntryStride);
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Fms + G.FmsFix1Entries + 4, pastModule) }));
        Assert.True(Torn(new Guides { Fix1 = Tcm with { Entries = new[] { (361, -1) } } }));
        Assert.True(Torn(new Guides { Fix1 = Tcm with { Entries = new[] { (-1, -2) } } }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => { I32(b, Fms + G.FmsFix1Entries, Outside); I32(b, Fms + G.FmsFix1Entries + 4, Outside + 16); } }));
        // The object: a position out of range, a torn name, a torn capability set.
        Assert.True(Torn(new Guides { Fix1 = Tcm with { Second = (95, 0) } }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => b[FixObject1 + G.LegNavaidName + 11] = 11 }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => b[FixObject1 + G.LegNavaidName] = 7 }));
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, FixObject1 + P.MapExtras!.LegCapabilityCount, 5) }));
        // In PLAN too.
        Assert.True(Torn(new Guides { Fix1 = Tcm, Tweak = b => I32(b, Fms + G.FmsFix1, Outside) }, mode: 3));
    }

    [Fact]
    public void A_fix_entries_vector_longer_than_the_draw_takes_reads_its_first_entries()
    {
        // The draw takes the layout's FixEntries (3) and stops: a fourth entry is not on the screen, never corrupt, and its
        // values are not checked.
        var four = Read(new Guides { Fix1 = Tcm with { Entries = new[] { (1, 1), (2, 2), (3, 3), (400, -7) } } });
        Assert.False(four.Corrupt);
        Assert.Equal(new[] { new Bbs757EhsiFixEntry(1, 1), new Bbs757EhsiFixEntry(2, 2), new Bbs757EhsiFixEntry(3, 3) },
            four.MapGuides!.Fixes.Single().Entries);
        // The first three still checked.
        Assert.True(Read(new Guides { Fix1 = Tcm with { Entries = new[] { (1, 1), (361, 2), (3, 3), (4, 4) } } }).Corrupt);
    }

    // ---- the off-path descent circles ----

    [Fact]
    public void The_off_path_circles_are_listed_while_on_and_valid()
    {
        // The live point 111.6 NM from the gate, 52° right at range 160: both circles around it on the screen.
        var on = new Guides { OffpathOn = true };
        var reading = Read(on, range: 160);
        Assert.False(reading.Corrupt);
        Assert.Equal("Map: off-path descent circles, 45 NM and 38 NM around 111.6 NM, 52° right", Assert.Single(Starting(reading, "Map: off-path")));
        // Whole NM.
        Assert.Contains("Map: off-path descent circles, 46 NM and 38 NM around ", Starting(Read(new Guides { OffpathOn = true, Clean = 45.5, Speedbrake = 38.4 }, range: 160), "Map: off-path")[0]);
        // Each of the three conditions, and the draw's non-zero point.
        Assert.Empty(Starting(Read(new Guides(), range: 160), "Map: off-path"));
        Assert.Empty(Starting(Read(new Guides { OffpathOn = true, ValidA = false }, range: 160), "Map: off-path"));
        Assert.Empty(Starting(Read(new Guides { OffpathOn = true, ValidB = false }, range: 160), "Map: off-path"));
        Assert.Empty(Starting(Read(new Guides { OffpathOn = true, OffpathPoint = (0, 0) }, range: 160), "Map: off-path"));
        Assert.Null(Read(new Guides { OffpathOn = true, ValidB = false }, range: 160).MapGuides!.Offpath);
        // PLAN too, from the plan's center; never spoken.
        Assert.Equal("Map: off-path descent circles, 45 NM and 38 NM around 111.6 NM south of the center",
            Assert.Single(Starting(Read(on, mode: 3, range: 160), "Map: off-path")));
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Guides(), range: 160), reading, true));
    }

    [Fact]
    public void An_off_path_circle_partly_on_the_screen_is_listed()
    {
        // The center 100 NM ahead at range 80 is past the screen's top; the 45 NM circle's near side, 55 NM ahead, is on it.
        var ahead = new Guides { OffpathOn = true, OffpathPoint = Bbs757EhsiTcasTests.At(100, 0) };
        Assert.Equal(new[] { "Map: off-path descent circles, 45 NM and 38 NM around 100.0 NM, ahead" }, Starting(Read(ahead, range: 80), "Map: off-path"));
        // 300 NM ahead: both circles wholly off the screen.
        Assert.Empty(Starting(Read(new Guides { OffpathOn = true, OffpathPoint = Bbs757EhsiTcasTests.At(300, 0) }, range: 80), "Map: off-path"));
        // Torn: a radius or the point not a number or out of range.
        Assert.True(Read(new Guides { OffpathOn = true, Clean = double.NaN }).Corrupt);
        Assert.True(Read(new Guides { OffpathOn = true, Speedbrake = -1 }).Corrupt);
        Assert.True(Read(new Guides { OffpathOn = true, OffpathPoint = (95, 0) }).Corrupt);
        // Not drawn: never checked.
        Assert.False(Read(new Guides { Clean = double.NaN }).Corrupt);
    }

    // ---- the altitude range arc ----

    [Fact]
    public void The_range_arc_follows_bluebirds_formula()
    {
        // Descending from 11000 to 3000 ft at 2000 ft/min, 300 kt: 240 s, 20 NM.
        Assert.Equal(20.0, Bbs757EhsiReader.RangeArcDistance(3000, 11000, -2000, 300, false, G)!.Value, 9);
        // Climbing from 5000 to 10000 ft at 1500 ft/min, 250 kt: 200 s, 13.89 NM.
        Assert.Equal(200 * 250 / 3600.0, Bbs757EhsiReader.RangeArcDistance(10000, 5000, 1500, 250, false, G)!.Value, 9);
        // Each condition: more than 50 ft away, at least 0.1 ft/min toward it, not on the ground.
        Assert.Null(Bbs757EhsiReader.RangeArcDistance(5050, 5000, 1500, 250, false, G));
        Assert.NotNull(Bbs757EhsiReader.RangeArcDistance(5050.5, 5000, 1500, 250, false, G));
        Assert.Null(Bbs757EhsiReader.RangeArcDistance(10000, 5000, 0.09, 250, false, G));
        Assert.NotNull(Bbs757EhsiReader.RangeArcDistance(10000, 5000, 0.1, 250, false, G));
        Assert.Null(Bbs757EhsiReader.RangeArcDistance(10000, 5000, -1500, 250, false, G));
        Assert.Null(Bbs757EhsiReader.RangeArcDistance(3000, 11000, 2000, 250, false, G));
        Assert.Null(Bbs757EhsiReader.RangeArcDistance(10000, 5000, 1500, 250, true, G));
    }

    [Fact]
    public void The_range_arc_is_listed_ahead_where_its_point_is_on_the_screen()
    {
        var climb = new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1500 };
        var reading = Read(climb);
        Assert.False(reading.Corrupt);
        Assert.Equal(new[] { "Map: altitude range arc, 13.9 NM, ahead" }, Starting(reading, "Map: altitude"));
        // Along the true track: 30° right of the map's up.
        Assert.Equal(new[] { "Map: altitude range arc, 13.9 NM, 30° right" },
            Starting(Read(new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1500, Track = 164.43 }), "Map: altitude"));
        // Each condition failing removes the row.
        foreach (var failing in new[]
        {
            new Guides { Selected = 5040, Altitude = 5000, VerticalSpeed = 1500 },
            new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 0.05 },
            new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = -1500 },
            new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1500, OnGround = true },
            new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1500, HasAdc = false },
            new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1500, HasAirGround = false },
        })
        {
            var r = Read(failing);
            Assert.False(r.Corrupt);
            Assert.Empty(Starting(r, "Map: altitude"));
            Assert.Null(r.MapGuides!.Arc);
        }
        // Its point off the screen (range 10: 13.9 NM is past the top): not listed, though drawn by the formula.
        var range10 = Read(climb, range: 10);
        Assert.Empty(Starting(range10, "Map: altitude"));
        Assert.NotNull(range10.MapGuides!.Arc);
        // MAP only, while the IRS is valid; never spoken.
        Assert.Empty(Starting(Read(climb, mode: 3), "Map: altitude"));
        Assert.Null(Read(climb, mode: 3).MapGuides!.Arc);
        Assert.Empty(Starting(Read(climb, scene: s => s.Valid = false), "Map: altitude"));
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Guides()), reading, true));
        // Torn in MAP: a value not a number, the adc or air/ground outside the module; in PLAN never read.
        Assert.True(Read(new Guides { Selected = 10000, Altitude = double.NaN, VerticalSpeed = 1500 }).Corrupt);
        Assert.True(Read(new Guides { Selected = double.NaN }).Corrupt);
        Assert.True(Read(new Guides { Track = double.PositiveInfinity }).Corrupt);
        Assert.True(Read(new Guides { Tweak = b => I32(b, Nd + G.NdToAirGround, Outside) }).Corrupt);
        Assert.False(Read(new Guides { Selected = 10000, Altitude = double.NaN, VerticalSpeed = 1500 }, mode: 3).Corrupt);
        // In MAP with the IRS not valid the draw uses none of them: never read, so never torn.
        Assert.False(Read(new Guides { Track = double.PositiveInfinity }, scene: s => s.Valid = false).Corrupt);
        Assert.False(Read(new Guides { Selected = 10000, Altitude = double.NaN, VerticalSpeed = 1500 }, scene: s => s.Valid = false).Corrupt);
        Assert.False(Read(new Guides { Tweak = b => I32(b, Nd + G.NdToAirGround, Outside) }, scene: s => s.Valid = false).Corrupt);
        Assert.Null(Read(climb, scene: s => s.Valid = false).MapGuides!.Arc);
    }

    // Near-level flight: 1 ft/min toward an MCP altitude 5000 ft away passes BlueBird's 0.1 ft/min test, and the distance
    // is tens of thousands of NM; its point goes round the globe and can come back onto the screen. Listed only within the
    // drawn range plus the layout's margin.
    [Fact]
    public void A_range_arc_far_past_the_range_is_not_listed()
    {
        // Ground speed picked so the distance is the projection sphere's circumference plus 10 NM: the point lands 10 NM
        // ahead, on the screen.
        double circumference = 2 * Math.PI * 6371.0 / 1.852;
        double groundSpeed = (circumference + 10) * 3600 / (5000 * 60);
        var level = new Guides { Selected = 10000, Altitude = 5000, VerticalSpeed = 1 };
        var reading = Read(level, scene: s => s.GroundSpeed = groundSpeed);
        Assert.False(reading.Corrupt);
        Assert.True(reading.MapGuides!.Arc!.Value.Distance > 20000);
        Assert.Empty(Starting(reading, "Map: altitude"));
        // A plausible descent at the same range: listed (40 + the 10 NM margin).
        Assert.Equal(new[] { "Map: altitude range arc, 20.0 NM, ahead" },
            Starting(Read(new Guides { Selected = 3000, Altitude = 11000, VerticalSpeed = -2000 }, scene: s => s.GroundSpeed = 300), "Map: altitude"));
    }

    // ---- the VNAV path deviation ----

    [Fact]
    public void The_vnav_deviation_reads_feet_above_or_below_the_path_in_the_descent()
    {
        // The sign as flown (KDEN to CYVR, 2026-11-12): level at 5,000 ft the stored value ran from -1,108 through 0 to
        // +175 as the path came down through the aircraft, so positive is the aircraft above the path.
        Assert.Equal(new[] { "Right side: VNAV path deviation 120 ft above the path" }, Starting(Read(new Guides { Phase = 4, Deviation = 120.4 }), "Right side: VNAV"));
        Assert.Equal(new[] { "Right side: VNAV path deviation 121 ft below the path" }, Starting(Read(new Guides { Phase = 4, Deviation = -120.6 }), "Right side: VNAV"));
        Assert.Equal(new[] { "Right side: VNAV path deviation 0 ft" }, Starting(Read(new Guides { Phase = 4, Deviation = -0.3 }), "Right side: VNAV"));
        // The clamp: at or beyond 400 ft either way.
        Assert.Equal(new[] { "Right side: VNAV path deviation 400 ft above the path, at full scale" }, Starting(Read(new Guides { Phase = 4, Deviation = 400 }), "Right side: VNAV"));
        Assert.Equal(new[] { "Right side: VNAV path deviation 450 ft below the path, at full scale" }, Starting(Read(new Guides { Phase = 4, Deviation = -450 }), "Right side: VNAV"));
        Assert.Equal(new[] { "Right side: VNAV path deviation 400 ft below the path" }, Starting(Read(new Guides { Phase = 4, Deviation = -399.6 }), "Right side: VNAV"));
        // Out of the descent phase, without a path for the route, in PLAN, in a light reading: no row.
        Assert.Empty(Starting(Read(new Guides { Phase = 3, Deviation = 120 }), "Right side: VNAV"));
        Assert.Empty(Starting(Read(new Guides { Phase = 4, Deviation = 120 }, scene: s => s.HasRegistry = false), "Right side: VNAV"));
        Assert.Empty(Starting(Read(new Guides { Phase = 4, Deviation = 120 }, mode: 3), "Right side: VNAV"));
        Assert.Empty(Starting(Read(new Guides { Phase = 4, Deviation = 120 }, full: false), "Right side: VNAV"));
        // After the map's rows, before the bearing pointers and the heading bug; never spoken.
        var rows = Rows(Read(new Guides { Phase = 4, Deviation = 120 }));
        int at = Array.IndexOf(rows, "Right side: VNAV path deviation 120 ft above the path");
        Assert.StartsWith("Heading bug: ", rows.Skip(at).First(r => r.StartsWith("Heading bug: ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.StartsWith("Map", rows[at - 1], StringComparison.Ordinal);
        Assert.Empty(Bbs757EhsiText.Changes(Read(new Guides()), Read(new Guides { Phase = 4, Deviation = 120 }), true));
        // Not a number in the descent: torn; out of it, never read.
        Assert.True(Read(new Guides { Phase = 4, Deviation = double.NaN }).Corrupt);
        Assert.False(Read(new Guides { Phase = 3, Deviation = double.NaN }).Corrupt);
    }

    // ---- the part missing ----

    [Fact]
    public void Map_guides_missing_lists_its_row_in_map_and_plan_only()
    {
        var layout = P with { MapGuides = null, MapGuidesMissing = "fix: no fms" };
        var guides = new Guides { Fix1 = Tcm, OffpathOn = true, Phase = 4, Deviation = 120 };
        foreach (int mode in new[] { 0, 1, 2, 3 })
        {
            var reading = Read(guides, mode, layout: layout);
            Assert.False(reading.Corrupt);
            Assert.Null(reading.MapGuides);
            var rows = Rows(reading);
            Assert.Equal(mode >= 2, rows.Contains("FIX, descent and altitude guides: not read: fix: no fms"));
            Assert.DoesNotContain(rows, r => r.StartsWith("Map: fix", StringComparison.Ordinal) || r.StartsWith("Map: off-path", StringComparison.Ordinal)
                || r.StartsWith("Right side: VNAV", StringComparison.Ordinal));
        }
        // With the part present nothing says "not read".
        Assert.DoesNotContain(Rows(Read(guides)), r => r.Contains("not read:", StringComparison.Ordinal));
    }
}
