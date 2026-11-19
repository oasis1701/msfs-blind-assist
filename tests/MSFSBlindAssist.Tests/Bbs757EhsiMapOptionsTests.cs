// The 757 EHSI's map options (plan 28): the NAV AID, ARPT and WPT lists read with the full set in MAP and PLAN, walked
// all or nothing, BlueBird's loop replayed over them, the drawn items listed where the screen draws them among the map's
// items; the tuned stations and the VOR course lines listed with every switch off. None of it is spoken. The values are
// plan 28's live ones at the KSEA gate (SEA the tuned VORTAC on 116.80, course 187), placed through the test layout.

using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiReaderTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiMapOptionsTests
{
    private static readonly Bbs757EhsiLayout P = Bbs757EhsiApproachTests.P;
    private static Bbs757EhsiMapOptions O => P.MapOptions!;

    // Where this test's memory puts the map options' objects (beside the approach's and the TCAS's).
    internal const uint Super = 0x24000, Fms = 0x25000, PanelLeft = 0x28000, PanelRight = 0x28800, Navaids = 0x29000,
        StationLeft = 0x2B000, StationRight = 0x2B100, NavaidPointers = 0x2C000, Airports = 0x2D000, LongNames = 0x2DF00,
        Ndbs = 0x2E000, NdbPointers = 0x2F000, Waypoints = 0xD0000, WaypointPointers = 0xE0000;
    private const int NavaidPitch = 0x50, RecordPitch = 48;

    /// <summary>A navaid record: ident, kind, class, place on this scene's map, frequency (MHz × 100).</summary>
    internal sealed record Nav(string Ident, int Kind, int Class, double Nm, double Off, int Frequency = 0);

    /// <summary>An airport, waypoint or NDB: its name and its place on this scene's map.</summary>
    internal sealed record Place(string Name, double Nm, double Off);

    /// <summary>A VOR panel as the course lines read it.</summary>
    internal sealed record Panel(bool Autotune, Nav? Station, double Course, int Whole = 116, int Fraction = 80);

    internal static readonly Nav Sea = new("SEA", 2, 2, 0.5, -173, 11680);
    internal static readonly Nav Tcm = new("TCM", 3, 0, 19.1, -160);
    internal static readonly Nav Pae = new("PAE", 1, 1, 28.6, 40, 11040);

    /// <summary>TCM placed where range 20's screen shows it (its live place, 19.1 NM behind, is past the screen's
    /// bottom).</summary>
    internal static readonly Nav TcmNear = Tcm with { Nm = 9, Off = -100 };

    /// <summary>The map options' memory as BlueBird keeps it, written over a <see cref="Scene"/> through its Tweak.</summary>
    internal sealed class Options
    {
        public bool NavAid, Airport, Waypoint, HasSuper = true, HasFms = true, RightSameStation;
        public Nav?[] NavaidList = Array.Empty<Nav?>();
        public Place[] AirportList = Array.Empty<Place>();
        public Place?[] WaypointList = Array.Empty<Place?>(), NdbList = Array.Empty<Place?>();
        public int TunedLeft = -1, TunedRight = -1;   // an index into NavaidList; -1 none
        public Panel? Left, Right;
        public Action<byte[]>? Tweak;

        public void Write(byte[] b)
        {
            Flag(b, Nd + O.NavAidSwitch, NavAid);
            Flag(b, Nd + O.AirportSwitch, Airport);
            Flag(b, Nd + O.WaypointSwitch, Waypoint);
            I32(b, Nd + O.TunedLeft, TunedLeft < 0 ? 0 : NavaidAt(TunedLeft));
            I32(b, Nd + O.TunedRight, TunedRight < 0 ? 0 : NavaidAt(TunedRight));
            I32(b, Nd + O.NdToSuper, HasSuper ? Super : 0);
            I32(b, Nd + O.NdToFms, HasFms ? Fms : 0);
            uint navdb = Super + (uint)O.SuperToNavdb;

            for (int i = 0; i < NavaidList.Length; i++)
                if (NavaidList[i] is { } n) PutNavaid(b, NavaidAt(i), n);
            Pointers(b, navdb + (uint)O.Navaids, NavaidPointers, NavaidList.Select((n, i) => n is null ? 0 : NavaidAt(i)).ToArray());

            Vector(b, Fms + (uint)O.Airports, Airports, AirportList.Length * O.AirportStride);
            for (int i = 0; i < AirportList.Length; i++)
            {
                uint at = Airports + (uint)(i * O.AirportStride);
                var a = AirportList[i];
                PutNarrow(b, at + (uint)O.AirportName, a.Name);
                var (lat, lon) = Bbs757EhsiTcasTests.At(a.Nm, a.Off);
                F64(b, at + O.AirportRecordLongitude, lon);
                F64(b, at + O.AirportRecordLatitude, lat);
                I32(b, at + 40, 432);   // BlueBird's unused field
            }

            for (int i = 0; i < WaypointList.Length; i++)
                if (WaypointList[i] is { } w) PutPoint(b, Waypoints + (uint)(i * RecordPitch), w, O.WaypointIdent, O.WaypointLatitude, O.WaypointLongitude);
            Pointers(b, navdb + (uint)O.Waypoints, WaypointPointers,
                WaypointList.Select((w, i) => w is null ? 0 : Waypoints + (uint)(i * RecordPitch)).ToArray());
            for (int i = 0; i < NdbList.Length; i++)
                if (NdbList[i] is { } n) PutPoint(b, Ndbs + (uint)(i * RecordPitch), n, O.NdbIdent, O.NdbLatitude, O.NdbLongitude);
            Pointers(b, navdb + (uint)O.Ndbs, NdbPointers, NdbList.Select((n, i) => n is null ? 0 : Ndbs + (uint)(i * RecordPitch)).ToArray());

            I32(b, Fms + O.FmsToVorLeft, Left is null ? 0 : PanelLeft);
            I32(b, Fms + O.FmsToVorRight, Right is null ? 0 : PanelRight);
            if (Left is { } l) PutPanel(b, PanelLeft, l, StationLeft);
            if (Right is { } r) PutPanel(b, PanelRight, r, RightSameStation ? StationLeft : StationRight);
            Tweak?.Invoke(b);
        }

        private static void PutPanel(byte[] b, uint at, Panel p, uint station)
        {
            Flag(b, at + O.PanelAutotune, p.Autotune);
            F64(b, at + O.PanelCourse, p.Course);
            I32(b, at + O.PanelManualWhole, p.Whole);
            I32(b, at + O.PanelManualFraction, p.Fraction);
            I32(b, at + O.PanelStation, p.Station is null ? 0 : station);
            if (p.Station is { } s) PutNavaid(b, station, s);
        }
    }

    internal static uint NavaidAt(int i) => Navaids + (uint)(i * NavaidPitch);

    internal static void PutNavaid(byte[] b, uint at, Nav n)
    {
        for (int i = 0; i < O.NavaidIdentLength; i++) I32(b, at + O.NavaidIdent + 4 * i, i < n.Ident.Length ? n.Ident[i] : 0);
        b[at + O.NavaidFrequency] = (byte)(n.Frequency & 0xFF);
        b[at + O.NavaidFrequency + 1] = (byte)(n.Frequency >> 8);
        b[at + 18] = 0x71;   // the DME elevation after the frequency: never part of the ident
        b[at + O.NavaidKind] = (byte)n.Kind;
        b[at + O.NavaidClass] = (byte)n.Class;
        var (lat, lon) = Bbs757EhsiTcasTests.At(n.Nm, n.Off);
        F64(b, at + O.NavaidLongitude, lon);
        F64(b, at + O.NavaidLatitude, lat);
    }

    private static void PutPoint(byte[] b, uint at, Place p, int identAt, int latitudeAt, int longitudeAt)
    {
        var (lat, lon) = Bbs757EhsiTcasTests.At(p.Nm, p.Off);
        F64(b, at + longitudeAt, lon);
        F64(b, at + latitudeAt, lat);
        for (int i = 0; i < p.Name.Length; i++) I32(b, at + identAt + 4 * i, p.Name[i]);
        I32(b, at + identAt + 4 * p.Name.Length, 0);
    }

    /// <summary>A narrow std::string in the libc++ alternate layout: short (up to 10 characters inline, byte 11 the
    /// size) or long (data pointer, size, capacity with the top bit).</summary>
    private static void PutNarrow(byte[] b, uint at, string text)
    {
        if (text.Length <= 10)
        {
            for (int i = 0; i < text.Length; i++) b[at + i] = (byte)text[i];
            b[at + 11] = (byte)text.Length;
            return;
        }
        for (int i = 0; i < text.Length; i++) b[LongNames + i] = (byte)text[i];
        I32(b, at, LongNames);
        I32(b, at + 4, text.Length);
        I32(b, at + 8, unchecked((int)(0x80000000u | (uint)(text.Length + 1))));
    }

    private static void Pointers(byte[] b, uint header, uint at, uint[] pointers)
    {
        Vector(b, header, at, pointers.Length * 4);
        for (int i = 0; i < pointers.Length; i++) I32(b, at + 4 * i, pointers[i]);
    }

    private static void Vector(byte[] b, uint header, uint at, int bytes)
    {
        I32(b, header, bytes == 0 ? 0 : at);
        I32(b, header + 4, bytes == 0 ? 0 : at + bytes);
        I32(b, header + 8, bytes == 0 ? 0 : at + bytes);
    }

    internal static Bbs757EhsiReading Read(Options options, int mode = 2, double range = 20, bool full = true,
        Action<Scene>? scene = null, Bbs757EhsiLayout? layout = null)
    {
        var s = new Scene { Mode = mode, Range = range, Tweak = options.Write };
        scene?.Invoke(s);
        return Bbs757EhsiReaderTests.Read(s, full, layout ?? P);
    }

    /// <summary>The map options' rows: a navaid's, an airport's, a waypoint's, an NDB's, a tuned station's or a course
    /// line's.</summary>
    internal static string[] OptionRows(Bbs757EhsiReading r) =>
        Bbs757EhsiText.Rows(r).Where(row => new[] { "Map: VOR", "Map: TACAN ", "Map: DME ", "Map: navaid", "Map: airport ", "Map: waypoint", "Map: NDB" }
            .Any(p => row.StartsWith(p, StringComparison.Ordinal))).ToArray();

    // ---- the rows ----

    // Review Focus: an option switched on with nothing within range lists nothing (and counts nothing), not an empty row.
    [Fact]
    public void An_option_with_nothing_in_range_lists_nothing()
    {
        var reading = Read(new Options
        {
            NavAid = true, Airport = true, Waypoint = true,
            NavaidList = new Nav?[] { Tcm with { Nm = 31 }, Pae with { Nm = 45 } },
            AirportList = new[] { new Place("KPAE", 31, 40) },
            WaypointList = new Place?[] { new Place("HAVHO", 30.5, 0) },
        });
        Assert.False(reading.Corrupt);
        Assert.Empty(OptionRows(reading));
        Assert.Equal(0, reading.MapOptions!.Count);
        Assert.Empty(reading.MapOptions.Drawn);
        Assert.DoesNotContain(Bbs757EhsiText.Rows(reading), r => r.StartsWith("Map options", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_drawn_item_is_listed_by_its_kind_and_place()
    {
        var reading = Read(new Options
        {
            NavAid = true, Airport = true, Waypoint = true,
            NavaidList = new Nav?[] { TcmNear, Pae with { Nm = 12 }, new Nav("SEA", 0, 0, 4, 90), new Nav("ZDME", 6, 0, 6, -90) },
            AirportList = new[] { new Place("KBFI", 5.2, -10), new Place("KSEA", 0.5, -173) },
            WaypointList = new Place?[] { new Place("HAVHO", 3, 30), new Place("", 2, 120) },
        });
        Assert.False(reading.Corrupt);
        // KSEA is the origin: skipped. The unnamed waypoint is counted and listed, never given a name.
        Assert.Equal(new[]
        {
            "Map: VOR/DME PAE, 12.0 NM, 40° right",
            "Map: TACAN TCM, 9.0 NM, 100° left",
            "Map: airport KBFI, 5.2 NM, 10° left",
            "Map: waypoint HAVHO, 3.0 NM, 30° right",
            "Map: VOR SEA, 4.0 NM, 90° right",
            "Map: DME ZDME, 6.0 NM, 90° left",
            "Map: waypoint, 2.0 NM, 120° right",
        }.OrderBy(r => r), OptionRows(reading).OrderBy(r => r));
        Assert.Equal(7, reading.MapOptions!.Count);
        // In screen order among the map's own items: PAE ahead right (higher on the screen) before KBFI just ahead.
        var rows = OptionRows(reading).ToList();
        Assert.True(rows.IndexOf("Map: VOR/DME PAE, 12.0 NM, 40° right") < rows.IndexOf("Map: airport KBFI, 5.2 NM, 10° left"));
        Assert.True(rows.IndexOf("Map: waypoint HAVHO, 3.0 NM, 30° right") < rows.IndexOf("Map: TACAN TCM, 9.0 NM, 100° left"));
        // Nothing here is spoken: the lists filling or moving say nothing.
        var empty = Read(new Options());
        Assert.Empty(Bbs757EhsiText.Changes(empty, reading, true));
        Assert.Empty(Bbs757EhsiText.Changes(reading, empty, true));
    }

    [Fact]
    public void A_navaid_of_another_kind_reads_navaid()
    {
        var layout = P with { MapOptions = O with { DrawnKinds = new[] { 0, 1, 2, 3, 6, 9 } } };
        var reading = Read(new Options { NavAid = true, NavaidList = new Nav?[] { new Nav("XYZ", 9, 0, 3, 0) } }, layout: layout);
        Assert.Equal(new[] { "Map: navaid XYZ, 3.0 NM, ahead" }, OptionRows(reading));
    }

    // The loop counts by range + 10 NM; the screen's clip cuts the picture, not the count: an item counted beyond the clip
    // is not listed.
    [Fact]
    public void An_item_counted_but_outside_the_clip_is_not_listed()
    {
        var reading = Read(new Options { Waypoint = true, WaypointList = new Place?[] { new Place("FAR", 25, 0), new Place("NEAR", 5, 0) } });
        Assert.Equal(2, reading.MapOptions!.Count);
        Assert.Equal(new[] { "FAR", "NEAR" }, reading.MapOptions.Drawn.Select(d => d.Name));
        Assert.Equal(new[] { "Map: waypoint NEAR, 5.0 NM, ahead" }, OptionRows(reading));
    }

    [Fact]
    public void The_tuned_station_is_listed_with_the_switches_off_and_not_counted()
    {
        var options = new Options { NavaidList = new Nav?[] { Sea, TcmNear }, TunedLeft = 0 };
        var reading = Read(options);
        Assert.Equal(new[] { "Map: VORTAC SEA, tuned, 0.5 NM, 173° left" }, OptionRows(reading));
        Assert.Equal(0, reading.MapOptions!.Count);
        // With NAV AID on, SEA is drawn once (tuned), TCM by the loop.
        options.NavAid = true;
        Assert.Equal(new[] { "Map: TACAN TCM, 9.0 NM, 100° left", "Map: VORTAC SEA, tuned, 0.5 NM, 173° left" }.OrderBy(r => r),
            OptionRows(Read(options)).OrderBy(r => r));
        Assert.Equal(1, Read(options).MapOptions!.Count);
        // A tuned station of any class at any range (here a low-class one at range 80, where the loop draws only high).
        var lowClass = new Options { NavaidList = new Nav?[] { Pae with { Nm = 3 } }, TunedRight = 0 };
        Assert.Equal(new[] { "Map: VOR/DME PAE, tuned, 3.0 NM, 40° right" }, OptionRows(Read(lowClass, range: 80)));
        // A tuned station of a kind the draw does not draw (an ILS DME) is not listed.
        Assert.Empty(OptionRows(Read(new Options { NavaidList = new Nav?[] { new Nav("ISNQ", 5, 0, 0.6, 0) }, TunedLeft = 0 })));
        // In PLAN too, by PLAN's place words; never in VOR or ILS (not read there) or a light reading.
        Assert.Single(OptionRows(Read(options, mode: 3)), r => r.StartsWith("Map: VORTAC SEA, tuned, 0.5 NM north-west of ", StringComparison.Ordinal));
        Assert.Null(Read(options, mode: 0).MapOptions);
        Assert.Null(Read(options, full: false).MapOptions);
        Assert.Empty(OptionRows(Read(options, full: false)));
    }

    // ---- the course lines ----

    private static Options CourseOptions() => new() { Left = new Panel(false, Sea, 187) };

    [Fact]
    public void A_course_line_is_listed_for_a_manually_tuned_receiver()
    {
        Assert.Equal(new[] { "Map: VOR L course 187 through SEA" }, OptionRows(Read(CourseOptions())));
        // In PLAN too, and with every switch off; the course as its label prints it.
        var options = CourseOptions();
        options.Left = options.Left! with { Course = 7.4 };
        Assert.Equal(new[] { "Map: VOR L course 007 through SEA" }, OptionRows(Read(options, mode: 3)));
        // VOR R's own line: PAE is off the screen (28.6 NM, 40° right); course 130 runs from it 10.6° right of down (130
        // minus the heading number 119.39), passing 14 NM right of the aircraft. Range 40 shows that, range 20 (12 NM
        // either side) does not.
        var right = new Options { Right = new Panel(false, Pae, 130, 110, 40) };
        Assert.Equal(new[] { "Map: VOR R course 130 through PAE" }, OptionRows(Read(right, range: 40)));
        Assert.Empty(OptionRows(Read(right)));
    }

    // The line runs both ways through its station: a course pointing away from the screen still reaches it behind the
    // station. In PLAN (no magnetic variation to turn it with) a line is listed whenever its conditions hold.
    [Fact]
    public void A_course_line_is_listed_only_where_it_crosses_the_screen()
    {
        var away = new Options { Right = new Panel(false, Pae, 310, 110, 40) };
        Assert.Equal(new[] { "Map: VOR R course 310 through PAE" }, OptionRows(Read(away, range: 40)));
        Assert.Empty(OptionRows(Read(away)));
        // Course 159.39: 40° off the screen's up, PAE's own direction, so the line runs through the aircraft: listed at
        // range 20 too.
        Assert.Equal(new[] { "Map: VOR R course 159 through PAE" },
            OptionRows(Read(new Options { Right = new Panel(false, Pae, 159.39, 110, 40) })));
        // PLAN: listed whenever drawn, with the station off the screen too (measured: the line was drawn through SEA with
        // SEA outside the clip); PAE is off the screen at range 20 and on it at range 40.
        Assert.Equal(new[] { "Map: VOR R course 310 through PAE" }, OptionRows(Read(away, mode: 3)));
        Assert.Equal(new[] { "Map: VOR R course 310 through PAE" }, OptionRows(Read(away, mode: 3, range: 40)));
    }

    // Re-review finding 2: a tiny positive range puts the station's pixel so far out that a step of a few pixels no longer
    // moves the sample; the sampling still ends, and a non-finite course or a range not above 0 lists nothing.
    [Theory]
    [InlineData(1e-20, 310)]
    [InlineData(1e-200, 310)]
    [InlineData(0, 310)]
    [InlineData(-40, 310)]
    [InlineData(40, double.NaN)]
    [InlineData(40, double.PositiveInfinity)]
    public async Task A_course_line_sampling_ends_and_lists_nothing_for_wild_inputs(double range, double course)
    {
        var listed = Read(new Options { Right = new Panel(false, Pae, 310, 110, 40) }, range: 40);
        Assert.Equal(new[] { "Map: VOR R course 310 through PAE" }, OptionRows(listed));
        var o = listed.MapOptions!;
        var wild = listed with
        {
            Range = range,
            MapOptions = o with { CourseLines = o.CourseLines.Select(l => l with { Course = course }).ToArray() },
        };
        var rows = Task.Run(() => OptionRows(wild));
        Assert.Same(rows, await Task.WhenAny(rows, Task.Delay(TimeSpan.FromSeconds(10))));   // the sampling ends
        Assert.Empty(await rows);
    }

    [Fact]
    public void Each_course_line_condition_failing_removes_the_line()
    {
        Options With(Func<Panel, Panel> change)
        {
            var o = CourseOptions();
            o.Left = change(o.Left!);
            return o;
        }
        Assert.Empty(OptionRows(Read(With(p => p with { Autotune = true }))));
        Assert.Empty(OptionRows(Read(With(p => p with { Station = Tcm with { Frequency = 11680 } }))));   // TACAN: not a course line kind
        Assert.Empty(OptionRows(Read(With(p => p with { Whole = 117, Fraction = 40 }))));                // not the manual frequency
        Assert.Empty(OptionRows(Read(With(p => p with { Course = double.NaN }))));
        Assert.Empty(OptionRows(Read(With(p => p with { Station = null }))));
        Assert.Empty(OptionRows(Read(new Options())));
        Assert.Empty(OptionRows(Read(new Options { Left = new Panel(false, Sea, 187), HasFms = false })));
        // Kind 5 (ILS/LOC with DME) is a course line kind.
        Assert.Equal(new[] { "Map: VOR L course 164 through ISNQ" },
            OptionRows(Read(With(p => p with { Station = new Nav("ISNQ", 5, 0, 0.6, 0, 11030), Course = 164, Whole = 110, Fraction = 30 }))));
    }

    [Fact]
    public void The_second_receiver_on_the_same_line_is_skipped()
    {
        var same = new Options { Left = new Panel(false, Sea, 187), Right = new Panel(false, Sea, 7), RightSameStation = true };
        Assert.Equal(new[] { "Map: VOR L course 187 through SEA" }, OptionRows(Read(same)));
        same.Right = new Panel(false, Sea, 187.3);
        Assert.Equal(new[] { "Map: VOR L course 187 through SEA" }, OptionRows(Read(same)));
        // Another course, or another station record, draws its own line.
        same.Right = new Panel(false, Sea, 90);
        Assert.Equal(new[] { "Map: VOR L course 187 through SEA", "Map: VOR R course 090 through SEA" }, OptionRows(Read(same)));
        var other = new Options { Left = new Panel(false, Sea, 187), Right = new Panel(false, Sea, 7) };
        Assert.Equal(new[] { "Map: VOR L course 187 through SEA", "Map: VOR R course 007 through SEA" }, OptionRows(Read(other)));
        // VOR L not drawn (autotune): VOR R's line stands.
        var leftAuto = new Options { Left = new Panel(true, Sea, 187), Right = new Panel(false, Sea, 7), RightSameStation = true };
        Assert.Equal(new[] { "Map: VOR R course 007 through SEA" }, OptionRows(Read(leftAuto)));
    }

    // ---- the switches ----

    [Fact]
    public void Each_window_switch_changes_the_list()
    {
        Options Lists() => new()
        {
            NavaidList = new Nav?[] { TcmNear },
            AirportList = new[] { new Place("KBFI", 5.2, -10) },
            WaypointList = new Place?[] { new Place("HAVHO", 3, 30) },
        };
        string[] All(Options o, bool tfc = false, bool terr = false) => Bbs757EhsiText.Rows(Read(new Options
        {
            NavAid = o.NavAid, Airport = o.Airport, Waypoint = o.Waypoint, NavaidList = o.NavaidList, AirportList = o.AirportList,
            WaypointList = o.WaypointList,
            Tweak = b =>
            {
                new Bbs757EhsiTcasTests.Tcas
                {
                    Tfc = tfc, Traffic = new[] { new Bbs757EhsiTcasTests.Entry(0, 0, 739, Bbs757EhsiTcasTests.At(8.73, -37.6).Lat, Bbs757EhsiTcasTests.At(8.73, -37.6).Lon) },
                }.Write(b);
                // TERR's live elevations, the picture byte as BlueBird sets it with the button on.
                new Bbs757EhsiMapExtrasTests.Extras { Terr = terr, Picture = terr }.Write(b);
            },
        })).ToArray();

        var off = All(Lists());
        void Adds(string[] on, params string[] rows)
        {
            foreach (string row in rows) Assert.Contains(row, on);
            Assert.All(off, row => Assert.Contains(row, on));
            Assert.Equal(off.Length + rows.Length, on.Length);
        }
        var navAid = Lists();
        navAid.NavAid = true;
        Adds(All(navAid), "Map: TACAN TCM, 9.0 NM, 100° left");
        var airport = Lists();
        airport.Airport = true;
        Adds(All(airport), "Map: airport KBFI, 5.2 NM, 10° left");
        var waypoint = Lists();
        waypoint.Waypoint = true;
        Adds(All(waypoint), "Map: waypoint HAVHO, 3.0 NM, 30° right");
        Adds(All(Lists(), tfc: true), "Left side: TFC", "Map: traffic, 8.7 NM, 38° left, +07");
        Adds(All(Lists(), terr: true), "Top right: TERR", "Top right: terrain 031 red, 004 amber", "Map: terrain shown, not read");
    }

    // ---- all or nothing ----

    [Fact]
    public void A_torn_list_is_corrupt()
    {
        static Options Full() => new()
        {
            NavAid = true, Airport = true, Waypoint = true,
            NavaidList = new Nav?[] { Tcm, null, Pae }, AirportList = new[] { new Place("KBFI", 5.2, -10) },
            WaypointList = new Place?[] { new Place("HAVHO", 3, 30), null }, NdbList = new Place?[] { new Place("FHR", 70, -90) },
        };
        void Corrupt(Action<byte[]> tear, double range = 20)
        {
            var o = Full();
            o.Tweak = tear;
            var reading = Read(o, range: range);
            Assert.True(reading.Corrupt);
            Assert.Null(reading.MapOptions);
            // A light reading does not walk the lists, and neither does VOR.
            Assert.False(Read(o, range: range, full: false).Corrupt);
            Assert.False(Read(o, mode: 0, range: range).Corrupt);
        }
        // The full set reads (a null entry is none, not torn).
        Assert.False(Read(Full()).Corrupt);
        Assert.False(Read(Full(), range: 80).Corrupt);

        uint navdb = Super + (uint)O.SuperToNavdb, navaids = navdb + (uint)O.Navaids, airports = Fms + (uint)O.Airports;
        Corrupt(b => I32(b, navaids + 4, NavaidPointers + 3 * 4 + 2));                    // not whole pointers
        Corrupt(b => I32(b, navaids + 4, NavaidPointers - 4));                            // end before begin
        Corrupt(b => I32(b, navaids, 0));                                                 // storage null
        Corrupt(b => I32(b, NavaidPointers, Outside));                                    // a record outside the module
        Corrupt(b => F64(b, NavaidAt(0) + O.NavaidLatitude, 95));                          // a position out of range
        Corrupt(b => I32(b, NavaidAt(0) + O.NavaidIdent, 0xD800));                         // an ident that is not text
        Corrupt(b => I32(b, airports + 4, Airports + 40));                                // not whole records
        Corrupt(b => b[Airports + 1] = 0x07);                                             // a name that is not text
        Corrupt(b => F64(b, Airports + (uint)O.AirportRecordLongitude, double.NaN));
        Corrupt(b =>
        {
            // A waypoint ident with no NUL within the guard.
            for (int i = 0; i < 40; i++) I32(b, Waypoints + (uint)O.WaypointIdent + 4 * (uint)i, 'A');
        });
        Corrupt(b => I32(b, navdb + (uint)O.Ndbs + 4, NdbPointers + 2), range: 80);
        Corrupt(b => I32(b, Nd + O.NdToSuper, Outside));
        Corrupt(b => I32(b, Nd + O.NdToFms, Outside));
        Corrupt(b => I32(b, Nd + O.TunedLeft, Outside));

        // The lists a switch does not ask for are not walked: a torn navaid list with NAV AID off reads.
        var off = Full();
        off.NavAid = false;
        off.Tweak = b => I32(b, navaids, 0);
        Assert.False(Read(off).Corrupt);
        // No navigation database or FMS: no lists, not corrupt.
        var none = Full();
        none.HasSuper = false;
        none.HasFms = false;
        Assert.Empty(Read(none).MapOptions!.Drawn);
        // A long airport name reads through its pointer; its label is the first characters, as drawn.
        var longName = Full();
        longName.AirportList = new[] { new Place("KBFI-BOEING-FIELD", 5.2, -10) };
        Assert.Contains("Map: airport KBFI, 5.2 NM, 10° left", OptionRows(Read(longName)));
    }

    [Fact]
    public void A_list_over_the_cap_is_corrupt()
    {
        var layout = P with { MapOptions = O with { MaxItems = 3 } };
        var three = new Options { Waypoint = true, WaypointList = Enumerable.Range(0, 3).Select(i => (Place?)new Place("W" + i, 1 + i, 0)).ToArray() };
        Assert.False(Read(three, layout: layout).Corrupt);
        var four = new Options { Waypoint = true, WaypointList = three.WaypointList.Append(new Place("W3", 4, 0)).ToArray() };
        Assert.True(Read(four, layout: layout).Corrupt);
        // Only the first RoundLimit entries are read: a torn record past them is never reached.
        var limited = P with { MapOptions = O with { RoundLimit = 2 } };
        var past = new Options { Waypoint = true, WaypointList = three.WaypointList, Tweak = b => F64(b, Waypoints + 2 * RecordPitch + (uint)O.WaypointLatitude, 95) };
        Assert.False(Read(past, layout: limited).Corrupt);
        Assert.True(Read(past).Corrupt);
    }

    [Fact]
    public void Map_options_missing_lists_its_row_in_map_and_plan_only()
    {
        var missing = P with { MapOptions = null, MapOptionsMissing = "navaid list not found" };
        const string Row = "Map options: not read: navaid list not found";
        var options = new Options { NavAid = true, NavaidList = new Nav?[] { Tcm }, Left = new Panel(false, Sea, 187) };
        Assert.Contains(Row, Bbs757EhsiText.Rows(Read(options, layout: missing)));
        Assert.Contains(Row, Bbs757EhsiText.Rows(Read(options, mode: 3, layout: missing)));
        Assert.DoesNotContain(Row, Bbs757EhsiText.Rows(Read(options, mode: 0, layout: missing)));
        Assert.DoesNotContain(Row, Bbs757EhsiText.Rows(Read(options, mode: 1, layout: missing)));
        Assert.DoesNotContain(Row, Bbs757EhsiText.Rows(Read(options, layout: missing, scene: s => s.Valid = false)));
        Assert.Empty(OptionRows(Read(options, layout: missing)));
        // Present: no such row, in any mode.
        foreach (int mode in new[] { 0, 1, 2, 3 })
            Assert.DoesNotContain(Bbs757EhsiText.Rows(Read(options, mode)), r => r.StartsWith("Map options: not read", StringComparison.Ordinal));
    }
}
