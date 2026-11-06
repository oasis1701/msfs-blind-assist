// Characterization tests for FenixChannelNdRadio (the ND's radio layers: ROSE LS and ROSE VOR top right blocks, the course
// pointer and deviations, the bearing pointers, the bottom corners and their flags). Each test writes its own RemoteData
// bytes with FenixChannelProtoWriter. The samples are the fields as sent in evidence03-nd.md,
// sitting 1 Part D and sitting 2 "the radio content and traffic".

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdRadioTests
{
    private const ulong Ls = 0, Vor = 1, Nav = 2, Arc = 3, Plan = 4;

    // The captain's EFIS: the mode, range 20 and the selectors (NAV 1 then NAV 2), one varint each as the Fenix sends them.
    private static FenixChannelProtoWriter Efis(ulong mode, params ulong[] selectors)
    {
        FenixChannelProtoWriter efis = new FenixChannelProtoWriter().Varint(1, mode).Varint(3, 20);
        foreach (ulong selector in selectors) efis.Varint(17, selector);
        return efis;
    }

    private static FenixChannelProtoWriter Remote(ulong mode, params ulong[] selectors)
        => new FenixChannelProtoWriter()
            .Message(1, Efis(mode, selectors))
            .Message(1, Efis(1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 194.1).Double(2, 195).Double(3, 4.5));

    private static FenixChannelProtoWriter Block(string? line1, string? line2, string? line3)
    {
        var block = new FenixChannelProtoWriter();
        if (line1 is not null) block.String(1, line1);
        if (line2 is not null) block.String(2, line2);
        if (line3 is not null) block.String(3, line3);
        return block;
    }

    private static FenixChannelProtoWriter Ils(double? loc, double? gs, double? course)
    {
        var ils = new FenixChannelProtoWriter();
        if (loc is { } l) ils.Double(1, l);
        if (gs is { } g) ils.Double(2, g);
        if (course is { } c) ils.Double(6, c).Double(7, c);
        return ils;
    }

    private static FenixChannelProtoWriter Pointers(double nav1, double nav2)
        => new FenixChannelProtoWriter().Double(1, nav1).Double(2, nav2);

    private static FenixChannelProtoWriter Flags(params (int Member, string Text)[] flags)
    {
        var inner = new FenixChannelProtoWriter();
        foreach ((int member, string text) in flags) inner.String(member, text);
        return new FenixChannelProtoWriter().Message(1, inner);
    }

    // As sent at 21:47:19.703: NAV 1 on VOR, NAV 2 on ADF, 8.4 NM to CVG.
    private static FenixChannelProtoWriter Corners()
        => new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "wVOR1").String(2, "wCVG   ").String(3, "g8s.4⠀⠀scNM"))
            .Message(2, new FenixChannelProtoWriter().String(1, "gADF2").String(2, "g     CP"));

    private static (FenixChannelPush Push, FenixNdData Header) Read(FenixChannelProtoWriter remote)
    {
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        var snapshot = new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
        FenixChannelSection<FenixNdData> header = FenixChannelNd.Read(snapshot);
        Assert.True(header.IsOk, header.Problem);
        return (data, header.Value!);
    }

    private static string[] Radio(FenixChannelProtoWriter remote)
    {
        (FenixChannelPush push, FenixNdData header) = Read(remote);
        FenixNdLayer layer = FenixChannelNdRadio.Rows(push, header);
        Assert.Null(layer.NdProblem);
        Assert.Null(layer.LayerProblem);
        return [.. layer.Rows];
    }

    private static string[] Corner(FenixChannelProtoWriter remote)
    {
        (FenixChannelPush push, FenixNdData header) = Read(remote);
        FenixNdLayer layer = FenixChannelNdRadio.CornerRows(push, header);
        Assert.Null(layer.NdProblem);
        Assert.Null(layer.LayerProblem);
        return [.. layer.Rows];
    }

    private static string[] LsRows(double? loc, double? gs, double? course = 186)
        => Radio(Remote(Ls, 1, 2).Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", " mICIZ ")).Message(82, Ils(loc, gs, course)));

    // ---- ROSE LS

    [Fact]
    public void Rose_ls_reads_its_block_and_deviation()
    {
        string[] rows = Radio(Remote(Ls)
            .Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", " mICIZ "))
            .Message(82, Ils(1.24, 0.86, 186)));

        Assert.Equal(
            [
                "Top right: ILS2 110.15; CRS 186°; ICIZ",
                "Center: course pointer at 186 on the compass",
                "Center: localizer bar 1.2 dots right",
                "Right side: glideslope diamond 0.9 dots above"
            ],
            rows);

        // The other sides and the centre; in cruise the block is two lines; after touchdown 72.1 is left out and 72.3 empty.
        string[] below = Radio(Remote(Ls)
            .Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", ""))
            .Message(82, Ils(-0.757, -0.558, 186)));
        Assert.Equal("Top right: ILS2 110.15; CRS 186°", below[0]);
        Assert.Equal("Center: localizer bar 0.8 dots left", below[2]);
        Assert.Equal("Right side: glideslope diamond 0.6 dots below", below[3]);

        string[] landed = Radio(Remote(Ls).Message(72, Block(null, "CRS m186c°", "")).Message(82, Ils(double.NaN, double.NaN, double.NaN)));
        Assert.Equal(
            [
                "Top right: CRS 186°",
                "Center: localizer scale, no bar",
                "Right side: glideslope scale, no diamond"
            ],
            landed);

        string[] centered = LsRows(0.04, -0.04);
        Assert.Equal("Center: localizer bar centered", centered[2]);
        Assert.Equal("Right side: glideslope diamond centered", centered[3]);
    }

    [Fact]
    public void The_localizer_and_glideslope_run_to_full_scale_at_their_own_figures()
    {
        // The bar stops at 2.5, the half diamond starts at 2.0 (sitting 2).
        Assert.Equal("Center: localizer bar 2.4 dots right", LsRows(2.44, 0)[2]);
        Assert.Equal("Center: localizer bar, full scale right", LsRows(2.5, 0)[2]);
        Assert.Equal("Center: localizer bar, full scale right", LsRows(4.0, 0)[2]);
        Assert.Equal("Center: localizer bar, full scale left", LsRows(-2.51, 0)[2]);
        Assert.Equal("Right side: glideslope diamond 1.8 dots below", LsRows(0, -1.835)[3]);
        Assert.Equal("Right side: glideslope half diamond, full scale above", LsRows(0, 2.003)[3]);
        Assert.Equal("Right side: glideslope half diamond, full scale below", LsRows(0, -4.0)[3]);
        Assert.Equal("Center: localizer bar 0.1 dots right", LsRows(0.05, 0)[2]);
    }

    [Fact]
    public void The_course_pointer_follows_82_6_and_never_the_text()
    {
        // After touchdown 72.2 still read CRS 186 with 82.6 NaN and no pointer drawn.
        string[] none = LsRows(double.NaN, double.NaN, double.NaN);
        Assert.DoesNotContain(none, row => row.Contains("course pointer"));
        Assert.Contains("Top right: ILS2 110.15; CRS 186°; ICIZ", none);

        // No 82.6 at all: no pointer either, and the scales say "no bar".
        string[] absent = LsRows(1.0, 1.0, null);
        Assert.DoesNotContain(absent, row => row.Contains("course pointer"));
        Assert.Contains("Center: localizer bar 1.0 dots right", absent);

        // In cruise 82.6 held 186 with both deviations NaN: the pointer with no bar.
        string[] cruise = LsRows(double.NaN, double.NaN, 185.6);
        Assert.Contains("Center: course pointer at 186 on the compass", cruise);
        Assert.Contains("Center: localizer scale, no bar", cruise);
    }

    [Fact]
    public void A_missing_82_reads_as_no_ils()
    {
        string[] rows = Radio(Remote(Ls).Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", "")));

        Assert.Equal("Center: localizer scale, no bar", rows[1]);
        Assert.Equal("Right side: glideslope scale, no diamond", rows[2]);
        Assert.Equal(3, rows.Length);
    }

    // ---- ROSE VOR

    [Fact]
    public void Rose_vor_reads_its_block_and_nothing_at_the_center()
    {
        // 70 as sent in sitting 1 and 2: no course was set, and no deviation, TO or FROM is drawn.
        string[] rows = Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s40 ", "CRS c---°", "lwODF ")));

        Assert.Equal(["Top right: VOR1 113.40; CRS ---°; ODF"], rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Center") || row.StartsWith("Right side"));

        // After a frequency change 70.3 is left out, then the frequency: only the lines sent are spoken.
        Assert.Equal(
            ["Top right: VOR1 113.40; CRS ---°"],
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s40 ", "CRS c---°", null))));
    }

    [Fact]
    public void A_failed_vor_1_draws_the_failed_block_in_rose_vor_only()
    {
        // 102.1.1 = rVOR1 while 70 still held wVOR1 w113.s10 and CRS c---° (sitting 1 Part D).
        string[] rows = Radio(Remote(Vor, 1)
            .Message(70, Block("wVOR1 w113.s10 ", "CRS c---°", "lwODF "))
            .Message(102, Flags((1, "rVOR1"))));

        Assert.Equal(["Top right: VOR1 red, ---.--; CRS, XX red", "Center: VOR red"], rows);

        // The failed block is unmeasured for a failed ILS and other modes: ROSE LS keeps its own block.
        string[] ls = Radio(Remote(Ls).Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", "")).Message(102, Flags((1, "rVOR1"))));
        Assert.StartsWith("Top right: ILS2", ls[0]);
        Assert.DoesNotContain(ls, row => row.Contains("VOR red"));

        // Not the failed block while the flag is empty.
        Assert.Equal(
            ["Top right: VOR1 113.10"],
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s10 ", null, null)).Message(102, Flags((1, "")))));
    }

    // ---- The pointers

    [Fact]
    public void The_pointers_follow_the_selectors()
    {
        // NAV 1 on VOR, NAV 2 on ADF: the shape follows the side (sitting 2, sitting 3).
        string[] both = Radio(Remote(Nav, 1, 2).Message(78, Pointers(204.6, 270.2)));
        Assert.Equal(
            [
                "Map: VOR 1 single-line pointer, at 205 on the compass",
                "Map: ADF 2 double-line pointer, at 270 on the compass"
            ],
            both);

        // NaN is no pointer; a number with its selector at OFF is none either (the 78 pair stays set).
        Assert.Equal(
            ["Map: ADF 2 double-line pointer, at 270 on the compass"],
            Radio(Remote(Nav, 1, 2).Message(78, Pointers(double.NaN, 270.2))));
        Assert.Empty(Radio(Remote(Nav, 0, 0).Message(78, Pointers(204.6, 270.2))));
        Assert.Empty(Radio(Remote(Nav).Message(78, Pointers(204.6, 270.2))));
        Assert.Equal(
            ["Map: VOR 1 single-line pointer, at 205 on the compass"],
            Radio(Remote(Nav, 1, 0).Message(78, Pointers(204.6, 270.2))));

        // NAV 1 on ADF and NAV 2 on VOR (sitting 3, the gate): the side still sets the shape.
        Assert.Equal(
            [
                "Map: ADF 1 single-line pointer, at 270 on the compass",
                "Map: VOR 2 double-line pointer, at 005 on the compass"
            ],
            Radio(Remote(Nav, 2, 1).Message(78, Pointers(270.2, 4.6))));

        // A packed selector list reads the same.
        FenixChannelProtoWriter packed = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, Nav).PackedVarints(17, 1, 2))
            .Message(1, Efis(1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 194.1))
            .Message(78, Pointers(204.6, 270.2));
        Assert.Equal(2, Radio(packed).Length);
    }

    [Fact]
    public void A_flagged_receiver_hides_its_pointer()
    {
        // (selectors, flag member, the pointer row's start): NAV 1 VOR 102.1.1, ADF 102.1.3; NAV 2 VOR 102.1.2, ADF 102.1.4.
        (ulong N1, ulong N2, int Member, string Row)[] cases =
        [
            (1, 1, 1, "Map: VOR 1 "), (2, 1, 3, "Map: ADF 1 "), (1, 1, 2, "Map: VOR 2 "), (1, 2, 4, "Map: ADF 2 ")
        ];
        foreach ((ulong n1, ulong n2, int member, string row) in cases)
        {
            string[] rows = Radio(Remote(Nav, n1, n2).Message(78, Pointers(272, 100)).Message(102, Flags((member, "x1rX"))));
            Assert.DoesNotContain(rows, r => r.StartsWith(row));
            Assert.Single(rows);
            Assert.Equal(2, Radio(Remote(Nav, n1, n2).Message(78, Pointers(272, 100))).Length);
        }

        // A flag of the receiver NOT selected does not hide it; an empty flag is none.
        Assert.Equal(2, Radio(Remote(Nav, 1, 2).Message(78, Pointers(272, 100)).Message(102, Flags((3, "x1rADF1"), (2, "")))).Length);
        // ROSE LS and ROSE VOR hide it too.
        Assert.DoesNotContain(
            Radio(Remote(Ls, 2, 1).Message(78, Pointers(272, 100)).Message(72, Block("wILS2", null, null))
                .Message(102, Flags((3, "x1rADF1")))),
            r => r.StartsWith("Map: ADF 1 "));
        Assert.DoesNotContain(
            Radio(Remote(Vor, 2, 1).Message(78, Pointers(272, 100)).Message(70, Block("wVOR1", null, null))
                .Message(102, Flags((3, "x1rADF1")))),
            r => r.StartsWith("Map: ADF 1 "));
    }

    // ---- ROSE VOR's course pointer (80.1)

    private static FenixChannelProtoWriter VorCourse(double course)
        => new FenixChannelProtoWriter().Double(1, course).Double(2, double.NaN).Double(3, double.NaN).Double(4, double.NaN);

    [Fact]
    public void Rose_vor_lists_its_course_pointer_from_80_1()
    {
        // Sitting 3: 80.1 = 185 on the ground, 70.2 read CRS c185°; 80.2 to 80.4 NaN; no bar, no TO/FROM.
        string[] rows = Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s40 ", "CRS c185°", "lwODF ")).Message(80, VorCourse(185)));
        Assert.Equal(
            [
                "Top right: VOR1 113.40; CRS 185°; ODF", "Center: course pointer at 185 on the compass",
                "Center: deviation scale, no bar"
            ],
            rows);

        // Never from the text: 70.2 says 185 while 80.1 is NaN or 80 is absent.
        Assert.DoesNotContain(
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1", "CRS c185°", null)).Message(80, VorCourse(double.NaN))),
            r => r.Contains("course pointer"));
        Assert.DoesNotContain(
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1", "CRS c185°", null))), r => r.Contains("course pointer"));

        // ROSE LS keeps 82.6 and never reads 80.1; ROSE NAV draws none.
        Assert.DoesNotContain(
            Radio(Remote(Ls).Message(72, Block("wILS2", null, null)).Message(80, VorCourse(185))), r => r.Contains("course pointer"));
        Assert.DoesNotContain(Radio(Remote(Nav, 1).Message(80, VorCourse(185))), r => r.Contains("course pointer"));
    }

    // ---- ROSE VOR's deviation bar (80.2) and TO/FROM (80.5), sitting 3

    private static string[] VorBar(double course, double? deviation, double? toFrom)
    {
        var block = new FenixChannelProtoWriter().Double(1, course);
        if (deviation is { } d) block.Double(2, d);
        if (toFrom is { } t) block.Double(5, t);
        return Radio(Remote(Vor, 1).Message(70, Block("wVOR1", null, null)).Message(80, block));
    }

    [Theory]
    [InlineData(185, 2.5, 1, "Center: deviation bar, full scale left, FROM")]
    [InlineData(235, 2.5, 1, "Center: deviation bar, full scale left, FROM")]
    [InlineData(300, -1.857, 1, "Center: deviation bar 1.9 dots right, FROM")]
    [InlineData(120, -2.5, 2, "Center: deviation bar, full scale right, TO")]
    public void Rose_vor_reads_the_deviation_bar_and_to_from_as_measured(double course, double dots, double toFrom, string expected)
    {
        string[] rows = VorBar(course, dots, toFrom);
        Assert.Equal(expected, rows[^1]);
        Assert.StartsWith("Center: course pointer at ", rows[^2]);
    }

    [Fact]
    public void Rose_vor_deviation_edges_centered_and_missing_parts()
    {
        Assert.Equal("Center: deviation bar centered, TO", VorBar(120, 0, 2)[^1]);
        Assert.Equal("Center: deviation bar centered", VorBar(120, 0.04, null)[^1]);
        Assert.Equal("Center: deviation bar 0.1 dots left", VorBar(120, 0.05, null)[^1]);
        Assert.Equal("Center: deviation bar 0.1 dots right", VorBar(120, -0.05, 0)[^1]);
        Assert.Equal("Center: deviation bar 2.4 dots left, TO", VorBar(120, 2.44, 2)[^1]);
        Assert.Equal("Center: deviation bar, full scale left", VorBar(120, 2.5, 3)[^1]);
        Assert.Equal("Center: deviation bar, full scale right", VorBar(120, -9, double.NaN)[^1]);

        // Nothing received: no bar, no arrow, whatever 80.5 says; no course: no deviation row.
        Assert.Equal("Center: deviation scale, no bar", VorBar(185, double.NaN, null)[^1]);
        Assert.Equal("Center: deviation scale, no bar", VorBar(185, null, 1)[^1]);
        Assert.DoesNotContain(VorBar(double.NaN, -1, 1), r => r.Contains("deviation"));

        // ROSE LS and NAV draw no VOR bar.
        Assert.DoesNotContain(
            Radio(Remote(Nav, 1).Message(80, new FenixChannelProtoWriter().Double(1, 185).Double(2, 1).Double(5, 1))),
            r => r.Contains("deviation"));
    }

    [Fact]
    public void A_wrong_shape_in_80_2_or_80_5_stops_the_radio_layer()
    {
        foreach ((int member, string path) in new[] { (2, "80.2"), (5, "80.5") })
        {
            FenixChannelProtoWriter bad = Remote(Vor, 1).Message(70, Block("wVOR1", null, null))
                .Message(80, new FenixChannelProtoWriter().Double(1, 185).String(member, "x"));
            (FenixChannelPush push, FenixNdData header) = Read(bad);
            FenixNdLayer layer = FenixChannelNdRadio.Rows(push, header);
            Assert.Equal($"Radio: field {path} has the wrong shape", layer.LayerProblem);
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rose_vor_deviation_is_the_same_in_every_culture(string culture)
    {
        string[] there = TestCulture.Run<string[]>(culture, () => VorBar(300, -1.857, 1));
        Assert.Equal("Center: deviation bar 1.9 dots right, FROM", there[^1]);
    }

    [Fact]
    public void The_pointers_are_listed_in_rose_nav_rose_ls_and_rose_vor_only()
    {
        FenixChannelProtoWriter With(ulong mode)
            => Remote(mode, 1, 2).Message(78, Pointers(204.6, 270.2))
                .Message(70, Block("wVOR1 w113.s40 ", "CRS c---°", "")).Message(72, Block("wILS2 m110.s15 ", null, null));

        Assert.Equal(2, Radio(With(Nav)).Length);
        Assert.Empty(Radio(With(Arc)));
        Assert.Empty(Radio(With(Plan)));
        Assert.Equal(2, Radio(With(Vor)).Count(row => row.StartsWith("Map:")));
        Assert.Equal(2, Radio(With(Ls)).Count(row => row.StartsWith("Map:")));
    }

    // ---- The corners

    [Fact]
    public void The_corners_read_as_drawn()
    {
        string[] rows = Corner(Remote(Nav, 1, 2).Message(76, Corners()));

        Assert.Equal(
            [
                "Bottom left: VOR1; CVG; 8.4 NM; single-line arrow",
                "Bottom right: ADF2; CP; double-line arrow"
            ],
            rows);

        // Every mode but PLAN (sitting 3: PLAN draws no corners, flags included).
        foreach (ulong mode in new[] { Ls, Vor, Arc })
        {
            Assert.Equal(rows, Corner(Remote(mode, 1, 2).Message(76, Corners())));
        }

        Assert.Empty(Corner(Remote(Plan, 1, 2).Message(76, Corners())));
        Assert.Empty(Corner(Remote(Plan, 1, 2).Message(76, Corners()).Message(102, Flags((1, "rVOR1"), (4, "rADF2")))));

        // An empty entry (its selector at OFF) gives no row.
        string[] off = Corner(Remote(Nav, 0, 0).Message(76, new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "")).Message(2, new FenixChannelProtoWriter().String(1, ""))));
        Assert.Empty(off);
        Assert.Empty(Corner(Remote(Nav, 0, 0)));

        // The shape follows the side, whichever receiver (sitting 3: NAV 1 on ADF, NAV 2 on VOR).
        string[] swapped = Corner(Remote(Nav, 2, 1).Message(76, Corners()));
        Assert.EndsWith("; single-line arrow", swapped[0]);
        Assert.EndsWith("; double-line arrow", swapped[1]);
    }

    [Fact]
    public void A_flag_reads_at_its_place()
    {
        // Sitting 1: the red VOR1 at the bottom left, no arrow, no other line, while 76 still held wVOR1.
        Assert.Equal(
            ["Bottom left: VOR1, red", "Bottom right: ADF2; CP; double-line arrow"],
            Corner(Remote(Nav, 1, 2).Message(76, Corners()).Message(102, Flags((1, "rVOR1")))));

        // Members 2 to 4 by their side; both members of one side in member order, in the side's one row.
        string[] rows = Corner(Remote(Nav, 1, 2).Message(76, Corners())
            .Message(102, Flags((2, "rVOR2"), (3, "rADF1"), (4, "rADF2"))));
        Assert.Equal(["Bottom left: ADF1, red", "Bottom right: VOR2, red; ADF2, red"], rows);

        string[] both = Corner(Remote(Nav, 1, 2).Message(76, Corners()).Message(102, Flags((1, "rVOR1"), (3, "rADF1"))));
        Assert.Equal("Bottom left: VOR1, red; ADF1, red", both[0]);

        // A flag with no corner text still reads, and an empty flag is no flag.
        Assert.Equal(["Bottom left: VOR1, red"], Corner(Remote(Nav, 1).Message(102, Flags((1, "rVOR1")))));
        Assert.Equal(
            "Bottom left: VOR1; CVG; 8.4 NM; single-line arrow",
            Corner(Remote(Nav, 1, 2).Message(76, Corners()).Message(102, Flags((1, ""))))[0]);
    }

    // ---- Problems

    [Fact]
    public void A_wrong_shape_stops_the_radio_layer_only()
    {
        // 72.2 as a number: the radio layer gives one row in its place.
        FenixChannelProtoWriter bad = Remote(Ls, 1, 2).Message(72, new FenixChannelProtoWriter().String(1, "wILS2").Double(2, 1.0));
        (FenixChannelPush push, FenixNdData header) = Read(bad);
        FenixNdLayer layer = FenixChannelNdRadio.Rows(push, header);
        Assert.Equal(["Radio: not available, field 72.2 has the wrong shape"], layer.Rows);
        Assert.Equal("Radio: field 72.2 has the wrong shape", layer.LayerProblem);
        Assert.Null(layer.NdProblem);

        // ... and the corner layer, reading other fields, is whole.
        Assert.Empty(FenixChannelNdRadio.CornerRows(push, header).Rows);

        // 70 as a whole, 78.1 as a string, 82.6 as a string, a selector as a double.
        Assert.Equal("field 70 has the wrong shape", Problem(Remote(Vor).Varint(70, 1)));
        Assert.Equal("field 78.1 has the wrong shape", Problem(Remote(Nav, 1).Message(78, new FenixChannelProtoWriter().String(1, "x"))));
        Assert.Equal("field 82.6 has the wrong shape", Problem(Remote(Ls).Message(72, Block("wILS2", null, null))
            .Message(82, new FenixChannelProtoWriter().String(6, "x"))));
        Assert.Equal("field 1.17 has the wrong shape", Problem(new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, Nav).Double(17, 1))
            .Message(1, Efis(1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 194.1))));

        // The corners: 76.2.3 as a number, 102.1.2 as a number.
        (FenixChannelPush cornerPush, FenixNdData cornerHeader) = Read(Remote(Nav, 1, 2).Message(76, new FenixChannelProtoWriter()
            .Message(2, new FenixChannelProtoWriter().Double(3, 1))));
        Assert.Equal(["Radio corners: not available, field 76.2.3 has the wrong shape"],
            FenixChannelNdRadio.CornerRows(cornerPush, cornerHeader).Rows);

        // The radio layer of ROSE NAV is untouched by a wrong 72 or 70: it reads only the fields its mode draws.
        Assert.Empty(Radio(Remote(Nav, 1).Varint(72, 1).Varint(70, 1)));

        static string Problem(FenixChannelProtoWriter remote)
        {
            (FenixChannelPush p, FenixNdData h) = Read(remote);
            FenixNdLayer result = FenixChannelNdRadio.Rows(p, h);
            Assert.Single(result.Rows);
            Assert.StartsWith("Radio: not available, ", result.Rows[0]);
            return result.Rows[0]["Radio: not available, ".Length..];
        }
    }

    [Fact]
    public void An_unknown_letter_stops_the_whole_nd()
    {
        (FenixChannelPush push, FenixNdData header) = Read(Remote(Ls).Message(72, Block("wILS2 z110", null, null)));
        FenixNdLayer layer = FenixChannelNdRadio.Rows(push, header);
        Assert.Equal("unknown code 'z' in the ILS block", layer.NdProblem);
        Assert.Empty(layer.Rows);

        (FenixChannelPush cornerPush, FenixNdData cornerHeader) = Read(Remote(Nav, 1).Message(76, new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "wVOR1z"))));
        Assert.Equal("unknown code 'z' in the corner text", FenixChannelNdRadio.CornerRows(cornerPush, cornerHeader).NdProblem);
    }

    // The ND's shared check (FenixChannelNd.ReadChecked, which Ctrl+W and the announcer read) must stop exactly where the
    // radio layers stop the window: the first layer's problem, then the corners', as Compose runs them.
    public static TheoryData<string> CheckCases => new()
    {
        "72 unknown", "72 unknown, 82 wrong", "72.1 unknown, 72.2 wrong", "70 unknown", "70 unknown, VOR 1 flagged",
        "76.1 unknown", "76.1 unknown, 1.17 wrong", "76.2 wrong, 102.1.3 unknown", "102.1.3 unknown", "clean"
    };

    private static FenixChannelProtoWriter CheckCase(string name, ulong mode)
    {
        FenixChannelProtoWriter remote = name.Contains("1.17")
            ? new FenixChannelProtoWriter()
                .Message(1, new FenixChannelProtoWriter().Varint(1, mode).Double(17, 1)).Message(1, Efis(1))
                .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"))
                .Message(194, new FenixChannelProtoWriter().Double(1, 194.1))
            : Remote(mode, 1, 2);
        if (name.StartsWith("72 unknown")) remote.Message(72, Block("wILS2 z110", null, null));
        if (name.StartsWith("72.1")) remote.Message(72, new FenixChannelProtoWriter().String(1, "wILS2 z110").Double(2, 1));
        if (name.Contains("82 wrong")) remote.Message(82, new FenixChannelProtoWriter().String(6, "x"));
        if (name.StartsWith("70")) remote.Message(70, Block("wVOR1 z113", null, null));
        if (name.Contains("VOR 1 flagged")) remote.Message(102, Flags((1, "rVOR1")));
        if (name.StartsWith("76.1")) remote.Message(76, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().String(1, "wVOR1z")));
        if (name.StartsWith("76.2")) remote.Message(76, new FenixChannelProtoWriter().Message(2, new FenixChannelProtoWriter().Double(3, 1)));
        if (name.Contains("102.1.3")) remote.Message(102, Flags((3, "zADF1")));
        return remote;
    }

    [Theory]
    [MemberData(nameof(CheckCases))]
    public void The_shared_check_stops_where_the_layers_stop(string name)
    {
        foreach (ulong mode in new[] { Ls, Vor, Nav, Arc, Plan })
        {
            (FenixChannelPush push, FenixNdData header) = Read(CheckCase(name, mode));
            string? layers = FenixChannelNdRadio.Rows(push, header).NdProblem ?? FenixChannelNdRadio.CornerRows(push, header).NdProblem;
            string? shared = FenixChannelNdRadio.UnknownCode(push, header) is { } unknown
                ? FenixChannelSection<FenixNdData>.UnknownCode(unknown.Code, unknown.Place).Problem
                : null;

            Assert.True(layers == shared, $"{name}, mode {mode}: the layers '{layers}', the check '{shared}'");
        }
    }

    [Fact]
    public void An_unknown_letter_in_the_text_stops_before_a_later_field_is_read()
    {
        // The layer reads its text first: 72's letter stops the ND though 82 is in another shape; a text field in another
        // shape (72.2) costs the layer its rows and stops nothing.
        (FenixChannelPush push, FenixNdData header) = Read(CheckCase("72 unknown, 82 wrong", Ls));
        Assert.Equal("unknown code 'z' in the ILS block", FenixChannelNdRadio.Rows(push, header).NdProblem);

        (FenixChannelPush textPush, FenixNdData textHeader) = Read(CheckCase("72.1 unknown, 72.2 wrong", Ls));
        FenixNdLayer layer = FenixChannelNdRadio.Rows(textPush, textHeader);
        Assert.Null(layer.NdProblem);
        Assert.Equal("Radio: field 72.2 has the wrong shape", layer.LayerProblem);
        Assert.Null(FenixChannelNdRadio.UnknownCode(textPush, textHeader));
    }

    [Fact]
    public void Compose_lists_the_radio_where_the_glass_draws_it()
    {
        FenixChannelProtoWriter remote = Remote(Nav, 1, 2)
            .Message(76, Corners()).Message(78, Pointers(204.6, 270.2))
            .Message(68, new FenixChannelProtoWriter().String(1, "VAKSU g318c").String(2, "g84 csNM"));
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        var snapshot = new FenixChannelSnapshot(
            new FenixDisplayVerdict(true, "trusted"), null, push.TryGet(1, out FenixChannelField f) ? f.AsMessage()! : push, null, 0, 1);

        FenixChannelRows rows = FenixChannelNdRows.Compose(snapshot, _ => { }, (null, 0, null), 0);

        Assert.Null(rows.Problem);
        string[] all = [.. rows.Rows!];
        int topRight = Array.FindIndex(all, row => row.StartsWith("Top right"));
        int pointer = Array.FindIndex(all, row => row.StartsWith("Map: VOR 1 "));
        int left = Array.FindIndex(all, row => row.StartsWith("Bottom left"));
        int right = Array.FindIndex(all, row => row.StartsWith("Bottom right"));
        Assert.True(topRight >= 0 && topRight < pointer, "the pointers follow the top right");
        Assert.True(pointer < left && left + 1 == right, "the corners come after the map, left then right");
    }

    // ---- Cultures

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Radio_rows_are_the_same_in_every_culture(string culture)
    {
        FenixChannelProtoWriter Build() => Remote(Ls, 1, 2)
            .Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", " mICIZ "))
            .Message(82, Ils(1.24, -0.86, 186.4))
            .Message(76, Corners()).Message(78, Pointers(204.6, 270.2));

        string[] invariant = [.. Radio(Build()), .. Corner(Build())];
        string[] there = TestCulture.Run<string[]>(culture, () => [.. Radio(Build()), .. Corner(Build())]);

        Assert.Equal(invariant, there);
        Assert.Contains("Center: localizer bar 1.2 dots right", there);
        Assert.Contains("Right side: glideslope diamond 0.9 dots below", there);
        Assert.Contains("Map: VOR 1 single-line pointer, at 205 on the compass", there);
        Assert.Contains("Bottom left: VOR1; CVG; 8.4 NM; single-line arrow", there);
        Assert.DoesNotContain(there, row => row.Contains("1,2") || row.Contains("0,9"));
    }

    [Fact]
    public void Every_look_entry_holds_no_color_or_one_color_word()
    {
        string[] words = ["amber", "cyan", "green", "magenta", "white", "red", "yellow"];
        foreach (FenixNdLookEntry entry in FenixChannelNdLook.All)
        {
            Assert.True(entry.Color is null || words.Contains(entry.Color), $"{entry.Item}: '{entry.Color}'");
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rose_vor_rows_are_the_same_in_every_culture(string culture)
    {
        FenixChannelProtoWriter Build() => Remote(Vor, 1, 2)
            .Message(70, Block("wVOR1 w113.s40 ", "CRS c185°", "lwODF "))
            .Message(80, VorCourse(185.4)).Message(76, Corners()).Message(78, Pointers(204.6, 270.2));

        string[] invariant = [.. Radio(Build()), .. Corner(Build())];
        string[] there = TestCulture.Run<string[]>(culture, () => [.. Radio(Build()), .. Corner(Build())]);

        Assert.Equal(invariant, there);
        Assert.Contains("Center: course pointer at 185 on the compass", there);
        Assert.Contains("Map: VOR 1 single-line pointer, at 205 on the compass", there);
        Assert.Contains("Map: ADF 2 double-line pointer, at 270 on the compass", there);
    }

    [Fact]
    public void Every_radio_look_is_in_the_table_with_its_evidence()
    {
        foreach (string item in new[]
        {
            FenixChannelNdRadio.LookItem.CoursePointer, FenixChannelNdRadio.LookItem.LocalizerBar,
            FenixChannelNdRadio.LookItem.GlideslopeDiamond, FenixChannelNdRadio.LookItem.GlideslopeHalfDiamond,
            FenixChannelNdRadio.LookItem.PointerNav1, FenixChannelNdRadio.LookItem.PointerNav2,
            FenixChannelNdRadio.LookItem.ArrowNav1, FenixChannelNdRadio.LookItem.ArrowNav2,
            FenixChannelNdRadio.LookItem.VorCoursePointer,
            FenixChannelNdRadio.LookItem.FailedVorIdent, FenixChannelNdRadio.LookItem.FailedVorFrequency,
            FenixChannelNdRadio.LookItem.FailedCourseLabel, FenixChannelNdRadio.LookItem.FailedCourseFigure,
            FenixChannelNdRadio.LookItem.FailedVorFlag
        })
        {
            FenixNdLookEntry? entry = FenixChannelNdLook.For(item);
            Assert.NotNull(entry);
            Assert.Contains("03-nd.md", entry.Evidence);
        }
    }
}
