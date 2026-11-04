// Characterization tests for FenixChannelNd, the reader of the captain's ND header. Each test writes its own RemoteData
// bytes with FenixChannelProtoWriter, field numbers spelled out on purpose (a wrong number in the app's table must
// fail here, not be copied into the helper), and reads them in a snapshot whose verdict is trusted, on connection 1.

using System.Globalization;
using System.Text;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdTests
{
    // The cruise of spec 3.3: ARC, range 20, next waypoint VAKSU, the ground speed and wind texts.
    private static FenixChannelProtoWriter Efis(ulong? mode = 3, ulong? range = 20, ulong? option = null, bool? ls = null)
    {
        var efis = new FenixChannelProtoWriter();
        if (mode is not null) efis.Varint(1, mode.Value);
        if (option is not null) efis.Varint(2, option.Value);
        if (range is not null) efis.Varint(3, range.Value);
        if (ls is not null) efis.Bool(6, ls.Value);
        return efis;
    }

    private static FenixChannelProtoWriter Cruise(FenixChannelProtoWriter? captain = null)
        => new FenixChannelProtoWriter()
            .Message(1, captain ?? Efis())
            .Message(1, Efis(mode: 1, range: 160))
            .Message(68, new FenixChannelProtoWriter().String(1, "VAKSU g318c").String(2, "g84 csNM").String(3, "g17:29"))
            .Message(163, new FenixChannelProtoWriter()
                .String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(194, new FenixChannelProtoWriter().Double(1, -1.44).Double(2, 318.4).Double(3, 4.5))
            .Message(104, new FenixChannelProtoWriter().String(1, "GPS PRIMARY"));

    private static FenixChannelSnapshot Snapshot(FenixChannelProtoWriter remote)
    {
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
    }

    private static FenixChannelSection<FenixNdData> Read(FenixChannelProtoWriter remote)
        => FenixChannelNd.Read(Snapshot(remote));

    private static FenixNdData Value(FenixChannelProtoWriter remote)
    {
        FenixChannelSection<FenixNdData> section = Read(remote);
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    [Fact]
    public void The_cruise_lines_read_as_sent()
    {
        FenixNdData nd = Value(Cruise());

        Assert.Equal("ARC", nd.Mode);
        Assert.Equal("20", nd.Range);
        Assert.Null(nd.MapOption);
        Assert.False(nd.LsOn);
        Assert.Equal("wsGSlgd456wsdTASlgd448", nd.GroundSpeedLine);
        Assert.Equal("g213w / g37", nd.WindLine);
        Assert.Equal(new FenixNdWaypointLines("VAKSU g318c", "g84 csNM", "g17:29"), nd.Waypoint);
        Assert.Equal(-1.44, nd.MagneticHeading);
        Assert.Equal(318.4, nd.MagneticTrack);
        Assert.Equal(4.5, nd.MagneticVariation);
        Assert.Null(nd.SelectedHeading);
        Assert.Null(nd.Position);
        Assert.Null(nd.PlanCenter);
        Assert.Empty(nd.CenterMessages);
        Assert.Empty(nd.ListedMessages);
    }

    [Fact]
    public void Only_the_captains_side_is_read()
    {
        FenixChannelProtoWriter remote = Cruise()
            .String(63, "FO CENTER").String(65, "FO WINDSHEAR").String(67, "FO TCAS")
            .Message(69, new FenixChannelProtoWriter().String(1, "FOWPT g001c").String(2, "g1 csNM").String(3, "g00:01"))
            .Message(164, new FenixChannelProtoWriter().String(1, "wsGSlgd999").String(2, "g1w / g1"));

        FenixNdData nd = Value(remote);

        Assert.Equal("ARC", nd.Mode);
        Assert.Equal("20", nd.Range);
        Assert.Equal("VAKSU g318c", nd.Waypoint!.Line1);
        Assert.Equal("wsGSlgd456wsdTASlgd448", nd.GroundSpeedLine);
        Assert.Empty(nd.CenterMessages);
        Assert.Empty(nd.ListedMessages);
    }

    [Fact]
    public void An_absent_range_reads_forty()
    {
        Assert.Equal("40", Value(Cruise(Efis(range: null))).Range);
        Assert.Equal("40", FenixChannelNd.RangeName(null));
        Assert.Equal("320", FenixChannelNd.RangeName(320));
    }

    [Fact]
    public void An_absent_mode_reads_the_measured_position()
    {
        Assert.Equal("NAV", FenixChannelNd.ModeWhenAbsent);
        Assert.Equal("NAV", Value(Cruise(Efis(mode: null))).Mode);
        Assert.Equal("NAV", FenixChannelNd.ModeName(null));
    }

    [Fact]
    public void Each_mode_number_has_its_name()
    {
        string[] names = ["LS", "VOR", "NAV", "ARC", "PLAN"];
        for (ulong number = 0; number < 5; number++)
        {
            Assert.Equal(names[number], FenixChannelNd.ModeName(number));
            Assert.Equal(names[number], Value(Cruise(Efis(mode: number))).Mode);
        }

        Assert.Equal("7", FenixChannelNd.ModeName(7));
        Assert.Equal("7", Value(Cruise(Efis(mode: 7))).Mode);
    }

    [Fact]
    public void Each_map_option_has_its_name()
    {
        string[] names = ["CSTR", "WPT", "VOR.D", "NDB", "ARPT"];
        for (ulong number = 0; number < 5; number++)
        {
            Assert.Equal(names[number], Value(Cruise(Efis(option: number))).MapOption);
        }

        Assert.Null(Value(Cruise(Efis(option: null))).MapOption);
        Assert.Equal("9", Value(Cruise(Efis(option: 9))).MapOption);
    }

    [Fact]
    public void The_ls_button_reads_from_the_captains_efis()
    {
        Assert.True(Value(Cruise(Efis(ls: true))).LsOn);
        Assert.False(Value(Cruise(Efis(ls: null))).LsOn);
    }

    [Fact]
    public void A_plan_center_with_its_extra_members_reads()
    {
        FenixChannelProtoWriter remote = Cruise().Message(84, new FenixChannelProtoWriter()
            .Double(1, 33.64).Double(2, -84.43).Varint(3, 1).String(4, "x").String(5, "y"));

        FenixNdData nd = Value(remote);

        Assert.Equal(new FenixLatLon(33.64, -84.43), nd.PlanCenter);
    }

    [Fact]
    public void The_position_the_selected_heading_and_a_non_finite_angle_read()
    {
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis())
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd  0wsdTASlgd---"))
            .Message(113, new FenixChannelProtoWriter().Double(2, 250))
            .Message(192, new FenixChannelProtoWriter().Double(1, 33.6).Double(2, -84.4))
            .Message(194, new FenixChannelProtoWriter().Double(1, double.NaN).Double(2, double.PositiveInfinity));

        FenixNdData nd = Value(remote);

        Assert.Equal(250, nd.SelectedHeading);
        Assert.Equal(new FenixLatLon(33.6, -84.4), nd.Position);
        Assert.Null(nd.MagneticHeading);
        Assert.Null(nd.MagneticTrack);
        Assert.Null(nd.MagneticVariation);
        Assert.Null(nd.WindLine);
        Assert.Null(nd.Waypoint);
    }

    [Fact]
    public void A_wrong_shape_stops_the_nd()
    {
        FenixChannelProtoWriter Base() => new FenixChannelProtoWriter().Message(1, Efis()).Message(1, Efis());
        FenixChannelProtoWriter Wind() => new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1");

        static void Expect(string path, FenixChannelProtoWriter remote)
            => Assert.Equal($"field {path} has the wrong shape", Read(remote).Problem);

        Expect("68", Base().Varint(68, 1).Message(163, Wind()));
        Expect("68.1", Base().Message(68, new FenixChannelProtoWriter().Double(1, 1)).Message(163, Wind()));
        Expect("1.3", new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(3, "20")).Message(1, Efis()).Message(163, Wind()));
        Expect("1.2", new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Message(2, new FenixChannelProtoWriter().Varint(1, 1)))
            .Message(1, Efis()).Message(163, Wind()));
        Expect("194.2", Base().Message(163, Wind()).Message(194, new FenixChannelProtoWriter().Varint(2, 3)));
        Expect("103.1", Base().Message(163, Wind()).Message(103, new FenixChannelProtoWriter().Varint(1, 3)));
        Expect("163.2", Base().Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1")
            .Raw(0x12, 0x02, 0xC3, 0x28)));

        // The EFIS list holds the captain then the first officer: one and three are wrong shapes at "1".
        Expect("1", new FenixChannelProtoWriter().Message(1, Efis()).Message(163, Wind()));
        Expect("1", Base().Message(1, Efis()).Message(163, Wind()));
    }

    [Fact]
    public void The_first_wrong_shape_in_field_order_is_the_one_given()
    {
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis())
            .Message(194, new FenixChannelProtoWriter().Varint(1, 3))
            .Varint(62, 1)
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1"));

        Assert.Equal("field 62 has the wrong shape", Read(remote).Problem);
    }

    [Fact]
    public void Missing_content_stops_the_nd()
    {
        FenixChannelSnapshot snapshot = Snapshot(Cruise());
        Assert.Equal("no data", FenixChannelNd.Read(snapshot with { Push = null }).Problem);

        Assert.Equal("no EFIS data", Read(new FenixChannelProtoWriter()
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1"))).Problem);
        Assert.Equal("no ground speed and wind", Read(new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis())).Problem);
    }

    [Fact]
    public void An_unknown_code_in_any_nd_line_stops_the_nd()
    {
        static FenixChannelProtoWriter Build(string? speed = null, string? wind = null, string? one = null,
            string? two = null, string? three = null, string? center = null, string? shear = null, string? tcas = null,
            string? listed = null, ulong mode = 3, Action<FenixChannelProtoWriter>? more = null)
        {
            var remote = new FenixChannelProtoWriter().Message(1, Efis(mode: mode)).Message(1, Efis())
                .Message(68, new FenixChannelProtoWriter().String(1, one ?? "VAKSU g318c").String(2, two ?? "g84 csNM")
                    .String(3, three ?? "g17:29"))
                .Message(163, new FenixChannelProtoWriter().String(1, speed ?? "wsGSlgd456").String(2, wind ?? "g213w / g37"));
            if (center is not null) remote.String(62, center);
            if (shear is not null) remote.String(64, shear);
            if (tcas is not null) remote.String(66, tcas);
            if (listed is not null) remote.Message(103, new FenixChannelProtoWriter().String(1, listed));
            more?.Invoke(remote);
            return remote;
        }

        static string? Problem(FenixChannelProtoWriter remote) => FenixChannelNd.ReadChecked(Snapshot(remote)).Problem;

        // The header's lines, which Read checks itself.
        Assert.Equal("unknown code 'q' in the ground speed line", Read(Build(speed: "wsGSlqgd456")).Problem);
        Assert.Equal("unknown code 'q' in the wind line", Read(Build(wind: "q213")).Problem);
        Assert.Equal("unknown code 'q' in the next waypoint lines", Read(Build(one: "VAKSU q318")).Problem);
        Assert.Equal("unknown code 'q' in the next waypoint lines", Read(Build(two: "q84")).Problem);
        Assert.Equal("unknown code 'q' in the next waypoint lines", Read(Build(three: "q17:29")).Problem);
        Assert.Equal("unknown code 'q' in the center message", Read(Build(center: "q")).Problem);
        Assert.Equal("unknown code 'q' in the windshear message", Read(Build(shear: "q")).Problem);
        Assert.Equal("unknown code 'q' in the TCAS message", Read(Build(tcas: "q")).Problem);
        Assert.Equal("unknown code 'q' in the ND messages", Read(Build(listed: "q")).Problem);
        Assert.Equal("unknown code 'q' in the wind line", Problem(Build(wind: "q213")));
        Assert.True(Read(Build()).IsOk);
        Assert.True(FenixChannelNd.ReadChecked(Snapshot(Build())).IsOk);

        // The lines the layers read at the mode, which ReadChecked adds: ROSE LS's block (72), ROSE VOR's (70), the
        // corners (76) and their flags (102.1).
        static void Lines(FenixChannelProtoWriter remote, int field, string line1)
            => remote.Message(field, new FenixChannelProtoWriter().String(1, line1));
        static void Corner(FenixChannelProtoWriter remote, string line1)
            => remote.Message(76, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().String(1, line1)));
        static void Flag(FenixChannelProtoWriter remote, int member, string text)
            => remote.Message(102, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().String(member, text)));

        Assert.Equal("unknown code 'q' in the ILS block", Problem(Build(mode: 0, more: r => Lines(r, 72, "wILS2 q110"))));
        Assert.Equal("unknown code 'q' in the VOR block", Problem(Build(mode: 1, more: r => Lines(r, 70, "wVOR1 q113"))));
        Assert.Equal("unknown code 'q' in the corner flag", Problem(Build(mode: 2, more: r => Flag(r, 3, "qADF1"))));
        foreach (ulong mode in new ulong[] { 0, 1, 2, 3 })
        {
            Assert.Equal("unknown code 'q' in the corner text", Problem(Build(mode: mode, more: r => Corner(r, "wVOR1q"))));
        }

        // A line the mode does not read stops nothing: ARC reads neither block, ROSE NAV neither, ROSE VOR not 70 while
        // VOR 1's flag replaces it, and PLAN no corner or flag. The header's own reader never reads them.
        Assert.Null(Problem(Build(mode: 3, more: r => { Lines(r, 72, "wILS2 q110"); Lines(r, 70, "wVOR1 q113"); })));
        Assert.Null(Problem(Build(mode: 2, more: r => { Lines(r, 72, "wILS2 q110"); Lines(r, 70, "wVOR1 q113"); })));
        Assert.Null(Problem(Build(mode: 1, more: r => { Lines(r, 70, "wVOR1 q113"); Flag(r, 1, "rVOR1"); })));
        Assert.Null(Problem(Build(mode: 4, more: r => { Corner(r, "wVOR1q"); Flag(r, 3, "qADF1"); Lines(r, 72, "q"); })));
        Assert.True(Read(Build(mode: 0, more: r => Lines(r, 72, "wILS2 q110"))).IsOk);
    }

    [Fact]
    public void The_approach_message_is_102_2_as_sent()
    {
        // Sample as sent in flight: gILS18L, with the corner flags (102.1) beside it.
        FenixChannelProtoWriter flags = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, 1)).String(2, "gILS18L");

        Assert.Equal("gILS18L", Value(Cruise().Message(102, flags)).ApproachMessage);
        Assert.Null(Value(Cruise()).ApproachMessage);
        Assert.Null(Value(Cruise().Message(102, new FenixChannelProtoWriter())).ApproachMessage);
        Assert.Null(Value(Cruise().Message(102, new FenixChannelProtoWriter().String(2, ""))).ApproachMessage);
        Assert.Null(Value(Cruise().Message(102, new FenixChannelProtoWriter().String(2, "   "))).ApproachMessage);
    }

    [Fact]
    public void The_approach_message_has_its_shape_and_its_letters()
    {
        Assert.Equal("field 102.2 has the wrong shape",
            Read(Cruise().Message(102, new FenixChannelProtoWriter().Varint(2, 1))).Problem);
        Assert.Equal("field 102 has the wrong shape", Read(Cruise().Varint(102, 1)).Problem);
        Assert.Equal("field 102.1 has the wrong shape",
            Read(Cruise().Message(102, new FenixChannelProtoWriter().Varint(1, 1))).Problem);
        Assert.Equal("unknown code 'q' in the approach message",
            Read(Cruise().Message(102, new FenixChannelProtoWriter().String(2, "qILS18L"))).Problem);
    }

    [Fact]
    public void Blank_and_null_messages_are_skipped()
    {
        FenixChannelProtoWriter remote = Cruise().String(62, "   ").String(64, "")
            .Message(103, new FenixChannelProtoWriter())
            .Message(103, new FenixChannelProtoWriter().String(1, ""))
            .Message(103, new FenixChannelProtoWriter().String(1, "  "))
            .Message(103, new FenixChannelProtoWriter().String(1, "TA ONLY"));

        Assert.Equal(["TA ONLY"], Value(remote).ListedMessages);
        Assert.Empty(Value(remote).CenterMessages);
    }

    [Fact]
    public void Center_windshear_and_tcas_messages_are_kept_apart_from_the_listed_ones()
    {
        FenixChannelProtoWriter remote = Cruise()
            .Message(103, new FenixChannelProtoWriter().String(1, "GPS PRIMARY"))
            .String(66, "TCAS FAIL").String(62, "CENTER").String(64, "WINDSHEAR")
            .Message(103, new FenixChannelProtoWriter().String(1, "TA ONLY"));

        FenixNdData nd = Value(remote);

        Assert.Equal(
            [new FenixNdCenterMessage("CENTER", 62), new FenixNdCenterMessage("WINDSHEAR", 64),
                new FenixNdCenterMessage("TCAS FAIL", 66)],
            nd.CenterMessages);
        Assert.Equal(["GPS PRIMARY", "TA ONLY"], nd.ListedMessages);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_reading_does_not_depend_on_the_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            FenixNdData nd = Value(Cruise(Efis(range: 160)));

            Assert.Equal("160", nd.Range);
            Assert.Equal("160", FenixChannelNd.RangeName(160));
            Assert.Equal(318.4, nd.MagneticTrack);
            Assert.Equal("g84 csNM", nd.Waypoint!.Line2);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
