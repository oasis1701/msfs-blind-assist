// Characterization tests for FenixChannelNdSpeech (the ND speaks only the colors that tell something) and for the look
// table's ColorTellsApart. The samples are the lines the sittings 1 and 2 measured (evidence03-nd.md).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdSpeechTests
{
    public static TheoryData<string, string> Usual => new()
    {
        { "163.1", "GS 0 TAS ---" },
        { "163.2", "153 / 32" },
        { "68.1", "INTCPT 176°" },
        { "68.2", "2.3 NM" },
        { "68.3", "01:58" },
        { "103", "GPS PRIMARY" },
        { "103a", "MAP PARTLY DISPLAYED, amber" },
        { "102.1.1", "VOR1, red" },
        { "102.2", "ILS18L" },
        { "72.1", "ILS2 110.15" },
        { "72.2", "CRS 186°" },
        { "72.3", "ICIZ" },
        { "76.1", "VOR1; CVG; 8.4 NM" },
    };

    private static string? Speak(string key) => key switch
    {
        "163.1" => FenixChannelNdSpeech.Line("wsGSlgd  0wsdTASlgd---", FenixChannelNdSpeech.GroundWind),
        "163.2" => FenixChannelNdSpeech.Line("g153w⠀/⠀g32", FenixChannelNdSpeech.GroundWind),
        "68.1" => FenixChannelNdSpeech.Line("INTCPT g176c°", FenixChannelNdSpeech.Waypoint),
        "68.2" => FenixChannelNdSpeech.Line("g2s.3 csNM", FenixChannelNdSpeech.Waypoint),
        "68.3" => FenixChannelNdSpeech.Line("g01:58", FenixChannelNdSpeech.Waypoint),
        "103" => FenixChannelNdSpeech.Line("GPS PRIMARY", FenixChannelNdSpeech.ListedMessage),
        "103a" => FenixChannelNdSpeech.Line("aMAP PARTLY DISPLAYED", FenixChannelNdSpeech.ListedMessage),
        "102.1.1" => FenixChannelNdSpeech.Line("x1rVOR1", FenixChannelNdSpeech.CornerFlag),
        "102.2" => FenixChannelNdSpeech.Line("gILS18L", FenixChannelNdSpeech.ApproachLabel),
        "72.1" => FenixChannelNdSpeech.Line("wILS2 m110.s15 ", FenixChannelNdSpeech.LsBlock),
        "72.2" => FenixChannelNdSpeech.Line("CRS m186c°", FenixChannelNdSpeech.LsBlock),
        "72.3" => FenixChannelNdSpeech.Line(" mICIZ ", FenixChannelNdSpeech.LsBlock),
        "76.1" => string.Join("; ", new[]
        {
            FenixChannelNdSpeech.Line("wVOR1", FenixChannelNdSpeech.Corner),
            FenixChannelNdSpeech.Line("wCVG   ", FenixChannelNdSpeech.Corner),
            FenixChannelNdSpeech.Line("g8s.4⠀⠀scNM", FenixChannelNdSpeech.Corner)
        }),
        _ => throw new ArgumentException(key)
    };

    [Theory]
    [MemberData(nameof(Usual))]
    public void A_usual_color_is_left_out(string key, string expected) => Assert.Equal(expected, Speak(key));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_samples_read_the_same_in_every_culture(string culture)
    {
        foreach (object[] row in Usual.Select(data => (object[])data))
        {
            string key = (string)row[0];
            Assert.Equal((string)row[1], TestCulture.Run(culture, () => Speak(key)));
        }
    }

    // The lines the ND's fields sent in the three sittings (evidence03-nd.md and the samples above):
    // 163, 68, 72, 70, 76, 102.1, 102.2, 103 and 62.
    public static TheoryData<string> MeasuredLines => new()
    {
        "wsGSlgd  0wsdTASlgd---", "wsGSlgd456wsdTASlgd448", "g153w⠀/⠀g32", "g213w / g37", "g---w⠀/⠀g---",
        "INTCPT g176c°", "INTCPT g084c°", "VAKSU g318c", "PPOS     ", "g2s.3 csNM", "g2s.8 csNM", "g84 csNM", "g99 csNM",
        "g24 csNM", "g19s.7 csNM", "g01:58", "g17:29",
        "wILS2 m110.s15 ", "wILS2 m109.s90 ", "CRS m186c°", " mICIZ ", "mIATL",
        "wVOR1 w113.s40 ", "wVOR1 w113.s10 ", "wVOR1 w108.s00 ", "CRS c---°", "CRS c185°", "lwODF ", "lwCVG ", "lw117.30 ",
        "wVOR1", "wVOR2", "wCVG   ", "g8s.4⠀⠀scNM", "g10s.0⠀⠀scNM", "g 50⠀⠀scNM", "gADF1", "gADF2", "g     CP", "gCP    ",
        "rVOR1", "rVOR2", "rADF1", "x1rVOR1", "x1rVOR2", "x1rADF1", "x1rADF2",
        "gILS18L", "GPS PRIMARY", "aMAP PARTLY DISPLAYED", "TA ONLY", "aTCAS", "aGPS PRIMARY LOST", "wMODE CHANGE ",
        "RANGE CHANGE"
    };

    // With no usual colors the ND's speech reads a measured line exactly as plan 01's does, so the ND's copy of the
    // parse and the merge cannot drift from FenixChannelSpeech's.
    [Theory]
    [MemberData(nameof(MeasuredLines))]
    public void With_no_usual_colors_a_measured_line_reads_as_plan_01_reads_it(string coded)
    {
        string? planOne = FenixChannelSpeech.Line(coded);

        Assert.NotNull(planOne);
        Assert.Equal(planOne, FenixChannelNdSpeech.Line(coded, null));
    }

    [Fact]
    public void The_one_difference_is_no_space_before_a_degree_sign()
    {
        // A degree sign after a space in the same color, which no measured line has: the ND drops the space.
        Assert.Equal("CRS 186 °, white", FenixChannelSpeech.Line("wCRS 186 °"));
        Assert.Equal("CRS 186°, white", FenixChannelNdSpeech.Line("wCRS 186 °", null));
    }

    [Fact]
    public void An_amber_figure_is_spoken_whatever_the_line()
    {
        string? spoken = FenixChannelNdSpeech.Line("wsGSlad  0wsdTASlgd---", FenixChannelNdSpeech.GroundWind);

        Assert.Contains("amber", spoken);
        Assert.DoesNotContain("white", spoken);
        Assert.DoesNotContain("green", spoken);
    }

    [Fact]
    public void A_color_not_usual_for_the_line_is_spoken()
    {
        Assert.Equal("2.3 magenta, NM", FenixChannelNdSpeech.Line("m2s.3 csNM", FenixChannelNdSpeech.Waypoint));
        Assert.Equal("MESSAGE, green", FenixChannelNdSpeech.Line("gMESSAGE", FenixChannelNdSpeech.CenterMessage));
        Assert.Equal("ILS18L, white", FenixChannelNdSpeech.Line("wILS18L", FenixChannelNdSpeech.ApproachLabel));
    }

    [Fact]
    public void A_field_with_no_usual_set_speaks_every_color()
    {
        Assert.Equal("WINDSHEAR, white", FenixChannelNdSpeech.Line("wWINDSHEAR", null));
        Assert.Equal("A white, B green", FenixChannelNdSpeech.Line("wAgB", null));
    }

    [Fact]
    public void Yellow_has_no_name_and_red_is_always_spoken()
    {
        Assert.Equal("CAUTION", FenixChannelNdSpeech.Line("yCAUTION", FenixChannelNdSpeech.CenterMessage));
        Assert.Equal("FAIL, red", FenixChannelNdSpeech.Line("rFAIL", FenixChannelNdSpeech.CenterMessage));
        Assert.Equal("FAIL, red", FenixChannelNdSpeech.Line("rFAIL", FenixChannelNdSpeech.CornerFlag));
    }

    [Fact]
    public void A_blank_line_and_an_unknown_letter_are_not_spoken()
    {
        Assert.Null(FenixChannelNdSpeech.Line(null, FenixChannelNdSpeech.CenterMessage));
        Assert.Null(FenixChannelNdSpeech.Line("w   ", FenixChannelNdSpeech.CenterMessage));
        Assert.Null(FenixChannelNdSpeech.Line("wHELLOq", FenixChannelNdSpeech.CenterMessage));
    }

    [Fact]
    public void Only_the_looks_that_tell_items_apart_or_are_red_speak_their_color()
    {
        string[] speaking =
        [
            .. FenixChannelNdLook.All.Where(entry => entry.ColorTellsApart).Select(entry => entry.Item)
        ];

        // Map points (magenta against the cyan tuned navaids), TO against route waypoint, the route VOR, the route
        // lines against the temporary plan, and the red parts of the failed VOR block.
        string[] expected =
        [
            FenixChannelNdMapData.LookItem.VorDme, FenixChannelNdMapData.LookItem.Dme, FenixChannelNdMapData.LookItem.Ndb,
            FenixChannelNdMapData.LookItem.Vor, FenixChannelNdMapData.LookItem.TunedVorDme,
            FenixChannelNdMapData.LookItem.TunedVor, FenixChannelNdMapData.LookItem.TunedDme,
            FenixChannelNdMapData.LookItem.Airport, FenixChannelNdMapData.LookItem.Fix,
            FenixChannelNdRoute.LookItem.ToWaypoint, FenixChannelNdRoute.LookItem.Waypoint, FenixChannelNdRoute.LookItem.Vor,
            FenixChannelNdRoute.LookItem.RouteLine, FenixChannelNdRoute.LookItem.DashedRouteLine,
            FenixChannelNdRoute.LookItem.TemporaryLine, FenixChannelNdRoute.LookItem.MissedApproachLine,
            FenixChannelNdTraffic.LookItem.TrafficAdvisory,
            FenixChannelNdRadio.LookItem.FailedVorIdent, FenixChannelNdRadio.LookItem.FailedCourseFigure,
            FenixChannelNdRadio.LookItem.FailedVorFlag
        ];
        Assert.Equal(expected.OrderBy(x => x), speaking.OrderBy(x => x));

        // Red is always spoken, so no red entry may be left silent.
        Assert.All(FenixChannelNdLook.All.Where(entry => entry.Color == "red"), entry => Assert.True(entry.ColorTellsApart));
    }

    [Fact]
    public void A_look_that_never_changes_speaks_its_symbol_alone()
    {
        Assert.Equal("lubber line", FenixChannelNdRows.Words("lubber line"));
        Assert.Equal("wind arrow", FenixChannelNdRows.Words("wind arrow"));
        Assert.Equal("WXR OFF", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.WxrOff));
        Assert.Equal("TERR", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.Terr));
        Assert.Equal("localizer bar", FenixChannelNdRows.Words(FenixChannelNdRadio.LookItem.LocalizerBar));
        Assert.Equal("hollow triangle", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.HeadingBug));
        Assert.Equal("ticks", FenixChannelNdRows.Words(FenixChannelNdTraffic.LookItem.TcasRing));
        Assert.Equal("diamond", FenixChannelNdRows.Words(FenixChannelNdTraffic.LookItem.OtherTraffic));
    }

    [Fact]
    public void A_look_that_tells_items_apart_keeps_its_color()
    {
        Assert.Equal("magenta cross", FenixChannelNdRows.Words(FenixChannelNdMapData.LookItem.Vor));
        Assert.Equal("cyan cross", FenixChannelNdRows.Words(FenixChannelNdMapData.LookItem.TunedVor));
        Assert.Equal("white diamond", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.ToWaypoint));
        Assert.Equal("green diamond", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.Waypoint));
        Assert.Equal("yellow dashed line", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.TemporaryLine));
        Assert.Equal("green dashed line", FenixChannelNdRows.Words(FenixChannelNdRoute.LookItem.DashedRouteLine));
    }
}
