// Characterization tests for FenixChannelNdRows (the ND window's composer), FenixChannelNdLook (the table of measured
// looks) and FenixChannelNdForm (the window). Each test writes its own RemoteData bytes with FenixChannelProtoWriter and
// reads them in a snapshot whose verdict is trusted, on connection 1. The rows follow evidence03-nd.md.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdRowsTests
{
    private static FenixChannelProtoWriter Efis(ulong mode = 3)
        => new FenixChannelProtoWriter().Varint(1, mode).Varint(3, 20);

    // The cruise of spec 3.3: ARC, range 20, next waypoint VAKSU, heading 318.4 and track 322.6.
    private static FenixChannelProtoWriter Cruise(
        ulong mode = 3, double? heading = 318.4, double track = 322.6, string wind = "g213w / g37",
        string? approach = null, string line3 = "g17:29", params string[] listed)
    {
        FenixChannelProtoWriter angles = new FenixChannelProtoWriter();
        if (heading is { } mag) angles.Double(1, mag);
        angles.Double(2, track).Double(3, 4.5);
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis(mode))
            .Message(1, Efis(1))
            .Message(68, new FenixChannelProtoWriter().String(1, "VAKSU g318c").String(2, "g84 csNM").String(3, line3))
            .Message(163, new FenixChannelProtoWriter()
                .String(1, "wsGSlgd456wsdTASlgd448").String(2, wind))
            .Message(194, angles);
        if (approach is not null) remote.Message(102, new FenixChannelProtoWriter().String(2, approach));
        foreach (string entry in listed) remote.Message(103, new FenixChannelProtoWriter().String(1, entry));
        return remote;
    }

    private static FenixChannelSnapshot Snapshot(FenixChannelProtoWriter remote)
    {
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
    }

    private static string[] Rows(FenixChannelProtoWriter remote)
    {
        FenixChannelRows rows = FenixChannelNdRows.Compose(Snapshot(remote), _ => { });
        Assert.Null(rows.Problem);
        return [.. rows.Rows!];
    }

    private static readonly string[] CruiseRows =
    [
        "Top left: GS 456 TAS 448",
        "Top left: wind 213 / 37",
        "Top left: wind arrow pointing 75° right",
        "Top center: heading 318 under the lubber line, track diamond 4° right",
        "Top right: VAKSU 318; 84 NM; 17:29"
    ];

    [Fact]
    public void The_cruise_rows_read_in_screen_order()
    {
        Assert.Equal(CruiseRows, Rows(Cruise()));

        string[] withMessage = Rows(Cruise(listed: ["GPS PRIMARY"]));
        Assert.Equal([.. CruiseRows, "Message: GPS PRIMARY"], withMessage);
        Assert.StartsWith("Top left: GS", withMessage[0]);
    }

    [Fact]
    public void The_ground_speed_line_never_speaks_its_d()
    {
        string first = Rows(Cruise())[0];

        Assert.DoesNotContain("d", first.Replace("Top left: ", ""));
        Assert.Equal("Top left: GS 456 TAS 448", first);
    }

    [Fact]
    public void The_track_reads_on_the_compass()
    {
        Assert.Equal("318", FenixChannelNdRows.CompassText(318.4));
        Assert.Equal("359", FenixChannelNdRows.CompassText(-1.44));
        Assert.Equal("360", FenixChannelNdRows.CompassText(359.6));
        Assert.Equal("005", FenixChannelNdRows.CompassText(5.2));
        Assert.Equal("360", FenixChannelNdRows.CompassText(0.2));
        Assert.Equal("not available", FenixChannelNdRows.CompassText(null));

        const string Lubber = "under the lubber line";
        Assert.Equal(
            $"Top center: heading 359 {Lubber}, track diamond 4° right",
            Rows(Cruise(heading: -1.44, track: 2.7))[3]);
        Assert.Equal(
            $"Top center: heading 005 {Lubber}, track diamond 7° left",
            Rows(Cruise(heading: 5.2, track: 358.0))[3]);
        Assert.Equal(
            $"Top center: heading 318 {Lubber}, track diamond on the heading",
            Rows(Cruise(heading: 318.4, track: 318.7))[3]);
    }

    [Fact]
    public void A_missing_heading_or_track_leaves_its_part_out()
    {
        FenixChannelProtoWriter NoTrack() => new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis(1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 318.4));
        FenixChannelProtoWriter NoHeading() => new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis(1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"));

        Assert.Contains("Top center: heading 318 under the lubber line", Rows(NoTrack()));
        Assert.Contains("Top center: heading not available", Rows(NoHeading()));
    }

    [Fact]
    public void The_modes_draw_their_own_header_rows()
    {
        // PLAN has no heading row; LS and VOR have the heading row but no next-waypoint box (Task 10 adds their block).
        Assert.DoesNotContain(Rows(Cruise(mode: 4)), row => row.StartsWith("Top center"));
        Assert.Contains(Rows(Cruise(mode: 4)), row => row.StartsWith("Top right: VAKSU"));
        foreach (ulong mode in new ulong[] { 0, 1 })
        {
            string[] rows = Rows(Cruise(mode: mode));
            Assert.Contains(rows, row => row.StartsWith("Top center"));
            Assert.DoesNotContain(rows, row => row.StartsWith("Top right"));
        }

        Assert.Contains(Rows(Cruise(mode: 2)), row => row.StartsWith("Top right: VAKSU"));
    }

    [Fact]
    public void The_waypoint_row_shows_only_a_waypoint_or_ppos()
    {
        FenixChannelProtoWriter With(string line1) => new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis(1))
            .Message(68, new FenixChannelProtoWriter().String(1, line1))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 10).Double(2, 10));

        Assert.Contains("Top right: PPOS", Rows(With("PPOS     ")));
        Assert.DoesNotContain(Rows(With("-----")), row => row.StartsWith("Top right"));
        Assert.DoesNotContain(Rows(With("")), row => row.StartsWith("Top right"));
        Assert.Contains("Top right: VAKSU 318", Rows(With("VAKSU g318")));
    }

    [Fact]
    public void A_problem_gives_not_available_and_is_reported()
    {
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis()).Message(1, Efis(1)).Varint(68, 1)
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd1").String(2, "g1w / g1"));
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelNdRows.Compose(Snapshot(remote), reported.Add);

        Assert.Equal(FenixChannelRows.NotAvailable("field 68 has the wrong shape"), rows);
        Assert.Equal(["field 68 has the wrong shape"], reported);
    }

    private static FenixChannelRows ComposeWith(
        Func<FenixChannelSnapshot, FenixNdData, FenixNdLayer> layer, List<string> reported)
        => FenixChannelNdRows.Compose(Snapshot(Cruise(listed: ["GPS PRIMARY"])), reported.Add, [layer]);

    [Fact]
    public void A_layer_problem_leaves_the_other_layers()
    {
        var reported = new List<string>();
        Func<FenixChannelSnapshot, FenixNdData, FenixNdLayer> failing =
            (_, _) => FenixNdLayer.Failed("Route", "field 2.4.1 has the wrong shape");

        FenixChannelRows rows = FenixChannelNdRows.Compose(
            Snapshot(Cruise(listed: ["GPS PRIMARY"])), reported.Add,
            [failing, (_, _) => FenixNdLayer.Of(["Map: a row"])]);

        Assert.Equal(
            [.. CruiseRows, "Route: not available, field 2.4.1 has the wrong shape", "Map: a row", "Message: GPS PRIMARY"],
            rows.Rows);
        Assert.Equal(["Route: field 2.4.1 has the wrong shape"], reported);
    }

    [Fact]
    public void An_unknown_code_stops_the_whole_nd()
    {
        var reported = new List<string>();

        FenixChannelRows rows = ComposeWith((_, _) => FenixNdLayer.StopsNd("unknown code 'q' in the route"), reported);

        Assert.Equal(FenixChannelRows.NotAvailable("unknown code 'q' in the route"), rows);
        Assert.Equal(["unknown code 'q' in the route"], reported);
    }

    [Fact]
    public void No_message_lists_no_message_row()
    {
        Assert.DoesNotContain(Rows(Cruise()), row => row.StartsWith("Message"));
    }

    [Fact]
    public void No_row_of_the_apps_own()
    {
        string[] rows = Rows(Cruise());

        Assert.DoesNotContain(rows, row => row.StartsWith("Captain ND"));
        Assert.DoesNotContain(rows, row => row.Contains("Messages: none"));
    }

    // ---- The approach label, top center (102.2), and the wind arrow, top left (sitting 2, the header in flight).

    [Fact]
    public void The_approach_label_reads_above_the_heading_in_every_mode()
    {
        foreach (ulong mode in new ulong[] { 3, 2, 0, 1, 4 })
        {
            string[] rows = Rows(Cruise(mode: mode, approach: "gILS18L"));

            Assert.Contains("Top center: ILS18L", rows);
            int label = Array.IndexOf(rows, "Top center: ILS18L");
            int heading = Array.FindIndex(rows, row => row.StartsWith("Top center: heading"));
            if (mode == 4) Assert.Equal(-1, heading);
            else Assert.True(label < heading, $"mode {mode}");
        }

        // Absent, empty or blank: no row.
        Assert.DoesNotContain(Rows(Cruise()), row => row.Contains("ILS18L"));
        Assert.Equal(CruiseRows, Rows(Cruise(approach: "")));
        Assert.Equal(CruiseRows, Rows(Cruise(approach: "   ")));
        Assert.Equal(
            ["Top left: GS 456 TAS 448", "Top left: wind 213 / 37",
             "Top left: wind arrow pointing 75° right", "Top center: ILS18L"],
            Rows(Cruise(approach: "gILS18L")).Take(4));
    }

    [Fact]
    public void An_unknown_letter_in_the_approach_label_stops_the_nd()
    {
        FenixChannelRows rows = FenixChannelNdRows.Compose(Snapshot(Cruise(approach: "qILS18L")), _ => { });

        Assert.NotNull(rows.Problem);
    }

    // The arrow points along the wind line's figure plus 180 minus the magnetic heading, folded to -180..180.
    [Theory]
    [InlineData(228, 18.0, "Top left: wind arrow pointing 30° right")]     // fixinfo-211836
    [InlineData(100, 280.0, "Top left: wind arrow pointing ahead")]
    [InlineData(10, 10.0, "Top left: wind arrow pointing back")]
    [InlineData(350, 10.0, "Top left: wind arrow pointing 160° right")]    // 350 + 180 - 10 = 520
    [InlineData(200, 40.0, "Top left: wind arrow pointing 20° left")]       // 340 folds to -20
    [InlineData(100, 120.0, "Top left: wind arrow pointing 160° right")]
    public void The_wind_arrow_reads_relative_to_the_nose(int figure, double heading, string expected)
    {
        string[] rows = Rows(Cruise(heading: heading, wind: $"g{figure}w / g39"));

        Assert.Equal(expected, rows.Single(row => row.Contains("wind arrow")));
    }

    [Fact]
    public void The_wind_arrow_rounds_half_away_from_zero_and_folds_the_back_both_ways()
    {
        // 100 + 180 - 249.5 = 30.5 -> 31; 100 + 180 - 310.5 = -30.5 -> 31 left; 0 + 180 - 0 and -180 both "back".
        Assert.Contains("Top left: wind arrow pointing 31° right", Rows(Cruise(heading: 249.5, wind: "g100w / g39")));
        Assert.Contains("Top left: wind arrow pointing 31° left", Rows(Cruise(heading: 310.5, wind: "g100w / g39")));
        Assert.Contains("Top left: wind arrow pointing back", Rows(Cruise(heading: 0, wind: "g0w / g39")));
        Assert.Contains("Top left: wind arrow pointing back", Rows(Cruise(heading: 360, wind: "g0w / g39")));
        Assert.Contains("Top left: wind arrow pointing ahead", Rows(Cruise(heading: 280, wind: "g100w / g39")));
        Assert.Contains("Top left: wind arrow pointing ahead", Rows(Cruise(heading: 280.4, wind: "g100w / g39")));
    }

    [Fact]
    public void No_wind_arrow_without_a_figure_a_heading_or_in_plan()
    {
        // Sitting 2's dashes, as sent (the separator spaces are U+2800).
        Assert.DoesNotContain(Rows(Cruise(wind: "g---w⠀/⠀g---")), row => row.Contains("wind arrow"));
        Assert.DoesNotContain(Rows(Cruise(heading: null)), row => row.Contains("wind arrow"));
        Assert.DoesNotContain(Rows(Cruise(mode: 4)), row => row.Contains("wind arrow"));
        Assert.DoesNotContain(Rows(Cruise(wind: "")), row => row.Contains("wind arrow"));

        // Heading-up modes list it: ARC, ROSE NAV, ROSE LS and ROSE VOR.
        foreach (ulong mode in new ulong[] { 3, 2, 0, 1 })
        {
            Assert.Contains(Rows(Cruise(mode: mode, wind: "g153w⠀/⠀g32")), row => row.Contains("wind arrow"));
        }
    }

    [Fact]
    public void The_time_line_is_the_third_waypoint_line()
    {
        // The flight's lines: 68.1 "wRONII g082c°"-style, 68.2 "g8s.1 csNM", 68.3 "g01:58" (sitting 2, the header).
        FenixChannelProtoWriter remote = new FenixChannelProtoWriter()
            .Message(1, Efis(3)).Message(1, Efis(1))
            .Message(68, new FenixChannelProtoWriter().String(1, "wRONII g082c°").String(2, "g8s.1 csNM").String(3, "g01:58"))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 318.4).Double(2, 322.6).Double(3, 4.5));

        Assert.Contains(
            Rows(remote), row => row == "Top right: RONII 082°; 8.1 NM; 01:58");
    }

    [Fact]
    public void The_glass_boxes_list_the_messages_the_way_it_draws_them()
    {
        // Evidence Part A, "The ND's sides": TA ONLY has the upper box, the lower box the first other entry.
        Assert.Equal(["Message: TA ONLY", "Message: GPS PRIMARY"], Messages("GPS PRIMARY", "TA ONLY"));
        Assert.Equal(
            ["Message: TA ONLY", "Message: GPS PRIMARY"], Messages("GPS PRIMARY", "MAP PARTLY DISPLAYED", "TA ONLY"));
        Assert.Equal(
            ["Message: TA ONLY", "Message: MAP PARTLY DISPLAYED, amber"], Messages("aMAP PARTLY DISPLAYED", "TA ONLY"));
        Assert.Equal(["Message: GPS PRIMARY"], Messages("GPS PRIMARY"));
    }

    [Fact]
    public void The_center_texts_come_before_the_two_boxes()
    {
        FenixChannelProtoWriter remote = Cruise(listed: ["GPS PRIMARY", "TA ONLY"])
            .String(62, "RANGE CHANGE").String(64, "WINDSHEAR").String(66, "TCAS FAIL");

        Assert.Equal(
            ["Message: RANGE CHANGE", "Message: WINDSHEAR", "Message: TCAS FAIL", "Message: TA ONLY", "Message: GPS PRIMARY"],
            Rows(remote).Where(row => row.StartsWith("Message")));
    }

    private static string[] Messages(params string[] listed)
        => [.. Rows(Cruise(listed: listed)).Where(row => row.StartsWith("Message"))];

    [Fact]
    public void An_item_with_no_measured_look_speaks_no_color()
    {
        Assert.Null(FenixChannelNdLook.For("an unmeasured item"));
        Assert.Equal("an unmeasured item", FenixChannelNdRows.Words("an unmeasured item"));
        Assert.Equal("lubber line", FenixChannelNdRows.Words("lubber line"));
        Assert.Equal("track diamond", FenixChannelNdRows.Words("track diamond"));
        Assert.Equal("wind arrow", FenixChannelNdRows.Words("wind arrow"));
        Assert.Contains("cap/205636, hdg-push-210207, cap/213017", FenixChannelNdLook.For("wind arrow")!.Evidence);
    }

    [Fact]
    public void Every_look_cites_its_evidence()
    {
        Assert.NotEmpty(FenixChannelNdLook.All);
        foreach (FenixNdLookEntry entry in FenixChannelNdLook.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Evidence), entry.Item);
            Assert.Contains("03-nd.md", entry.Evidence);
            Assert.Same(entry, FenixChannelNdLook.For(entry.Item));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_are_the_same_in_every_culture(string culture)
    {
        string[] invariant = Rows(Cruise(approach: "gILS18L", listed: ["GPS PRIMARY", "TA ONLY"]));

        string[] there = TestCulture.Run(culture, () => Rows(Cruise(approach: "gILS18L", listed: ["GPS PRIMARY", "TA ONLY"])));

        Assert.Equal(invariant, there);
        Assert.Contains("Top left: wind arrow pointing 75° right", there);
        Assert.Contains("Top center: ILS18L", there);
        Assert.Contains("Top left: wind arrow pointing 31° left", TestCulture.Run(culture,
            () => Rows(Cruise(heading: 310.5, wind: "g100w / g39"))));
        Assert.Equal("318", TestCulture.Run(culture, () => FenixChannelNdRows.CompassText(318.4)));
        Assert.Equal("359", TestCulture.Run(culture, () => FenixChannelNdRows.CompassText(-1.44)));
    }

    [Fact]
    public void The_nd_window_is_the_nd_display()
    {
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelNdForm(monitor);

        Assert.Equal("Fenix ND", form.Text);
        Assert.Equal("ND display", form.Controls.OfType<DisplayListBox>().Single().AccessibleName);
        Assert.True(typeof(FenixChannelNdForm).IsSubclassOf(typeof(FenixChannelDisplayForm)));
    }
}
