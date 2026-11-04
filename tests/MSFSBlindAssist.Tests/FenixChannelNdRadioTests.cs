// Characterization tests for FenixChannelNdRadio (the ND's radio layers: ROSE LS and ROSE VOR top right blocks, the course
// pointer and deviations, the bearing pointers, the bottom corners and their flags). Each test writes its own RemoteData
// bytes with FenixChannelProtoWriter. The samples are the fields as sent in docs/fenix-channel/evidence/03-nd.md,
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
                "Top right: ILS2 white, 110.15 magenta; CRS white, 186 magenta, ° cyan; ICIZ, magenta",
                "Center: magenta course pointer at 186 on the compass",
                "Center: magenta localizer bar 1.2 dots right",
                "Right side: magenta glideslope diamond 0.9 dots above"
            ],
            rows);

        // The other sides and the centre; in cruise the block is two lines; after touchdown 72.1 is left out and 72.3 empty.
        string[] below = Radio(Remote(Ls)
            .Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", ""))
            .Message(82, Ils(-0.757, -0.558, 186)));
        Assert.Equal("Top right: ILS2 white, 110.15 magenta; CRS white, 186 magenta, ° cyan", below[0]);
        Assert.Equal("Center: magenta localizer bar 0.8 dots left", below[2]);
        Assert.Equal("Right side: magenta glideslope diamond 0.6 dots below", below[3]);

        string[] landed = Radio(Remote(Ls).Message(72, Block(null, "CRS m186c°", "")).Message(82, Ils(double.NaN, double.NaN, double.NaN)));
        Assert.Equal(
            [
                "Top right: CRS white, 186 magenta, ° cyan",
                "Center: localizer scale, no bar",
                "Right side: glideslope scale, no diamond"
            ],
            landed);

        string[] centered = LsRows(0.04, -0.04);
        Assert.Equal("Center: magenta localizer bar centered", centered[2]);
        Assert.Equal("Right side: magenta glideslope diamond centered", centered[3]);
    }

    [Fact]
    public void The_localizer_and_glideslope_run_to_full_scale_at_their_own_figures()
    {
        // The bar stops at 2.5, the half diamond starts at 2.0 (sitting 2).
        Assert.Equal("Center: magenta localizer bar 2.4 dots right", LsRows(2.44, 0)[2]);
        Assert.Equal("Center: magenta localizer bar, full scale right", LsRows(2.5, 0)[2]);
        Assert.Equal("Center: magenta localizer bar, full scale right", LsRows(4.0, 0)[2]);
        Assert.Equal("Center: magenta localizer bar, full scale left", LsRows(-2.51, 0)[2]);
        Assert.Equal("Right side: magenta glideslope diamond 1.8 dots below", LsRows(0, -1.835)[3]);
        Assert.Equal("Right side: magenta glideslope half diamond, full scale above", LsRows(0, 2.003)[3]);
        Assert.Equal("Right side: magenta glideslope half diamond, full scale below", LsRows(0, -4.0)[3]);
        Assert.Equal("Center: magenta localizer bar 0.1 dots right", LsRows(0.05, 0)[2]);
    }

    [Fact]
    public void The_course_pointer_follows_82_6_and_never_the_text()
    {
        // After touchdown 72.2 still read CRS 186 with 82.6 NaN and no pointer drawn.
        string[] none = LsRows(double.NaN, double.NaN, double.NaN);
        Assert.DoesNotContain(none, row => row.Contains("course pointer"));
        Assert.Contains("Top right: ILS2 white, 110.15 magenta; CRS white, 186 magenta, ° cyan; ICIZ, magenta", none);

        // No 82.6 at all: no pointer either, and the scales say "no bar".
        string[] absent = LsRows(1.0, 1.0, null);
        Assert.DoesNotContain(absent, row => row.Contains("course pointer"));
        Assert.Contains("Center: magenta localizer bar 1.0 dots right", absent);

        // In cruise 82.6 held 186 with both deviations NaN: the pointer with no bar.
        string[] cruise = LsRows(double.NaN, double.NaN, 185.6);
        Assert.Contains("Center: magenta course pointer at 186 on the compass", cruise);
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

        Assert.Equal(["Top right: VOR1 113.40, white; CRS white, ---° cyan; ODF, white"], rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Center") || row.StartsWith("Right side"));

        // After a frequency change 70.3 is left out, then the frequency: only the lines sent are spoken.
        Assert.Equal(
            ["Top right: VOR1 113.40, white; CRS white, ---° cyan"],
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s40 ", "CRS c---°", null))));
    }

    [Fact]
    public void A_failed_vor_1_draws_the_failed_block_in_rose_vor_only()
    {
        // 102.1.1 = rVOR1 while 70 still held wVOR1 w113.s10 and CRS c---° (sitting 1 Part D).
        string[] rows = Radio(Remote(Vor, 1)
            .Message(70, Block("wVOR1 w113.s10 ", "CRS c---°", "lwODF "))
            .Message(102, Flags((1, "rVOR1"))));

        Assert.Equal(["Top right: VOR1 red, ---.-- cyan; CRS white, XX red", "Center: VOR red"], rows);

        // The failed block is unmeasured for a failed ILS and other modes: ROSE LS keeps its own block.
        string[] ls = Radio(Remote(Ls).Message(72, Block("wILS2 m110.s15 ", "CRS m186c°", "")).Message(102, Flags((1, "rVOR1"))));
        Assert.StartsWith("Top right: ILS2", ls[0]);
        Assert.DoesNotContain(ls, row => row.Contains("VOR red"));

        // Not the failed block while the flag is empty.
        Assert.Equal(
            ["Top right: VOR1 113.10, white"],
            Radio(Remote(Vor, 1).Message(70, Block("wVOR1 w113.s10 ", null, null)).Message(102, Flags((1, "")))));
    }

    // ---- The pointers

    [Fact]
    public void The_pointers_follow_the_selectors()
    {
        // NAV 1 on VOR, NAV 2 on ADF: both looks measured (the white single line, the two green lines).
        string[] both = Radio(Remote(Nav, 1, 2).Message(78, Pointers(204.6, 270.2)));
        Assert.Equal(
            [
                "Map: VOR 1 pointer, white single line with an open triangle head, at 205 on the compass",
                "Map: ADF 2 pointer, green two lines joined at the head, at 270 on the compass"
            ],
            both);

        // NaN is no pointer; a number with its selector at OFF is none either (the 78 pair stays set).
        Assert.Equal(
            ["Map: ADF 2 pointer, green two lines joined at the head, at 270 on the compass"],
            Radio(Remote(Nav, 1, 2).Message(78, Pointers(double.NaN, 270.2))));
        Assert.Empty(Radio(Remote(Nav, 0, 0).Message(78, Pointers(204.6, 270.2))));
        Assert.Empty(Radio(Remote(Nav).Message(78, Pointers(204.6, 270.2))));
        Assert.Equal(
            ["Map: VOR 1 pointer, white single line with an open triangle head, at 205 on the compass"],
            Radio(Remote(Nav, 1, 0).Message(78, Pointers(204.6, 270.2))));

        // NAV 1 on ADF and NAV 2 on VOR: the looks are not measured, so the row has none.
        Assert.Equal(
            [
                "Map: ADF 1 pointer, at 270 on the compass",
                "Map: VOR 2 pointer, at 005 on the compass"
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
    public void The_pointers_are_listed_in_rose_nav_and_rose_ls_only()
    {
        FenixChannelProtoWriter With(ulong mode)
            => Remote(mode, 1, 2).Message(78, Pointers(204.6, 270.2))
                .Message(70, Block("wVOR1 w113.s40 ", "CRS c---°", "")).Message(72, Block("wILS2 m110.s15 ", null, null));

        Assert.Equal(2, Radio(With(Nav)).Length);
        Assert.Empty(Radio(With(Arc)));
        Assert.Empty(Radio(With(Plan)));
        Assert.DoesNotContain(Radio(With(Vor)), row => row.StartsWith("Map:"));
        Assert.Equal(2, Radio(With(Ls)).Count(row => row.StartsWith("Map:")));
    }

    // ---- The corners

    [Fact]
    public void The_corners_read_as_drawn()
    {
        string[] rows = Corner(Remote(Nav, 1, 2).Message(76, Corners()));

        Assert.Equal(
            [
                "Bottom left: VOR1, white; CVG, white; 8.4 green, NM cyan; white single-line arrow",
                "Bottom right: ADF2, green; CP, green; green double-line arrow"
            ],
            rows);

        // Every mode while sent (ARC, ROSE NAV and ROSE LS measured; ROSE VOR and PLAN follow).
        foreach (ulong mode in new[] { Ls, Vor, Arc, Plan })
        {
            Assert.Equal(rows, Corner(Remote(mode, 1, 2).Message(76, Corners())));
        }

        // An empty entry (its selector at OFF) gives no row; an unmeasured arrow is the word alone.
        string[] off = Corner(Remote(Nav, 0, 0).Message(76, new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "")).Message(2, new FenixChannelProtoWriter().String(1, ""))));
        Assert.Empty(off);
        Assert.Empty(Corner(Remote(Nav, 0, 0)));

        string[] swapped = Corner(Remote(Nav, 2, 1).Message(76, Corners()));
        Assert.EndsWith("; arrow", swapped[0]);
        Assert.EndsWith("; arrow", swapped[1]);
    }

    [Fact]
    public void A_flag_reads_at_its_place()
    {
        // Sitting 1: the red VOR1 at the bottom left, no arrow, no other line, while 76 still held wVOR1.
        Assert.Equal(
            ["Bottom left: VOR1, red", "Bottom right: ADF2, green; CP, green; green double-line arrow"],
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
            "Bottom left: VOR1, white; CVG, white; 8.4 green, NM cyan; white single-line arrow",
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
        int pointer = Array.FindIndex(all, row => row.StartsWith("Map: VOR 1 pointer"));
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
        Assert.Contains("Center: magenta localizer bar 1.2 dots right", there);
        Assert.Contains("Right side: magenta glideslope diamond 0.9 dots below", there);
        Assert.Contains("Map: VOR 1 pointer, white single line with an open triangle head, at 205 on the compass", there);
        Assert.Contains("Bottom left: VOR1, white; CVG, white; 8.4 green, NM cyan; white single-line arrow", there);
        Assert.DoesNotContain(there, row => row.Contains("1,2") || row.Contains("0,9"));
    }

    [Fact]
    public void Every_radio_look_is_in_the_table_with_its_evidence()
    {
        foreach (string item in new[]
        {
            FenixChannelNdRadio.LookItem.CoursePointer, FenixChannelNdRadio.LookItem.LocalizerBar,
            FenixChannelNdRadio.LookItem.GlideslopeDiamond, FenixChannelNdRadio.LookItem.GlideslopeHalfDiamond,
            FenixChannelNdRadio.LookItem.VorPointerNav1, FenixChannelNdRadio.LookItem.AdfPointerNav2,
            FenixChannelNdRadio.LookItem.VorArrowNav1, FenixChannelNdRadio.LookItem.AdfArrowNav2,
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
