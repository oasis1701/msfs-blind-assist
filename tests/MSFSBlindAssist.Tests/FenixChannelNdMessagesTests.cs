// Characterization tests for FenixChannelNdMessages, the ND messages announcer: new and gone ND messages and the
// active waypoint, silent on a baseline, muted and untrusted. Rulings 72 to 76 of plan 03, task 13.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdMessagesTests
{
    private const ulong Ls = 0, Nav = 2, Arc = 3, Plan = 4;

    private static FenixChannelSnapshot Snap(
        string[]? listed = null, string? center = null, string? waypoint = "VAKSU g318c", string? distance = "g84 csNM",
        ulong mode = Arc, bool trusted = true, int connection = 1, bool broken = false)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, mode)).Message(1, new FenixChannelProtoWriter())
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456").String(2, "g213w / g37"));
        if (center is not null) remote.String(62, center);
        if (waypoint is not null)
        {
            var nav = new FenixChannelProtoWriter().String(1, waypoint);
            if (distance is not null) nav.String(2, distance);
            remote.Message(68, nav);
        }

        foreach (string entry in listed ?? []) remote.Message(103, new FenixChannelProtoWriter().String(1, entry));
        if (broken) remote.Message(68, new FenixChannelProtoWriter().Double(2, 1));

        FenixChannelPush data = FenixChannelPush.From(remote.ToArray())!;
        return new FenixChannelSnapshot(
            new FenixDisplayVerdict(trusted, trusted ? "trusted" : "off"), null, data, null, 0, connection);
    }

    // A rig that has taken its baseline on a quiet ND.
    private static FenixChannelNdMessages Baselined(FenixChannelSnapshot? first = null)
    {
        var announcer = new FenixChannelNdMessages();
        Assert.Null(announcer.Sample(first ?? Snap(), false));
        return announcer;
    }

    [Fact]
    public void The_first_reading_is_silent()
    {
        var announcer = new FenixChannelNdMessages();
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
    }

    [Fact]
    public void A_new_message_is_spoken_once()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Equal("ND message: MAP PARTLY DISPLAYED, amber", announcer.Sample(Snap(listed: ["aMAP PARTLY DISPLAYED"]), false));
        Assert.Null(announcer.Sample(Snap(listed: ["aMAP PARTLY DISPLAYED"]), false));
    }

    [Fact]
    public void A_message_that_goes_is_spoken_once()
    {
        FenixChannelNdMessages announcer = Baselined(Snap(listed: ["TA ONLY"]));

        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(), false));
        Assert.Null(announcer.Sample(Snap(), false));
    }

    [Fact]
    public void Several_at_once_share_one_line()
    {
        FenixChannelNdMessages announcer = Baselined();
        Assert.Equal("ND message: WINDSHEAR, TA ONLY",
            announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
        Assert.Equal("ND message gone: WINDSHEAR, TA ONLY", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void New_gone_and_waypoint_share_one_line_in_that_order()
    {
        FenixChannelNdMessages announcer = Baselined(Snap(listed: ["TA ONLY"]));

        Assert.Equal("ND message: WINDSHEAR. ND message gone: TA ONLY. ND: 1500, 2.7 NM",
            announcer.Sample(Snap(center: "WINDSHEAR", waypoint: "1500", distance: "g2s.7 csNM"), false));
    }

    [Fact]
    public void An_active_waypoint_change_is_spoken()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Equal("ND: 1500, 2.7 NM", announcer.Sample(Snap(waypoint: "1500", distance: "g2s.7 csNM"), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "1500", distance: "g2s.6 csNM"), false));
        Assert.Equal("ND: ABC, 24 NM", announcer.Sample(Snap(waypoint: "ABC g090c", distance: "g24 csNM"), false));
        Assert.Equal("ND: DEF", announcer.Sample(Snap(waypoint: "DEF", distance: null), false));
    }

    [Fact]
    public void Ppos_is_no_active_waypoint()
    {
        FenixChannelNdMessages announcer = Baselined(Snap(waypoint: "1500", distance: "g2s.7 csNM"));

        Assert.Equal("ND: no active waypoint", announcer.Sample(Snap(waypoint: "PPOS     ", distance: null), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "PPOS", distance: null), false));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), true));
        Assert.Null(announcer.Sample(Snap(waypoint: "1500"), true));
    }

    [Fact]
    public void Unmuting_replays_nothing()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), true));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), false));
        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(waypoint: "1500"), false));
    }

    [Fact]
    public void An_untrusted_nd_is_silent()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], trusted: false), false));
        // The first good reading after the gap is the new baseline: what came meanwhile is not news.
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void A_reading_with_a_problem_is_a_gap()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], broken: true), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
    }

    [Fact]
    public void A_new_connection_is_silent()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500", connection: 2), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500", connection: 2), false));
        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(waypoint: "1500", connection: 2), false));
    }

    [Theory]
    [InlineData("RANGE CHANGE")]
    [InlineData("MODE CHANGE")]
    [InlineData("wMODE CHANGE ")]
    public void Range_and_mode_change_are_never_announced(string text)
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(center: text), false));
        Assert.Null(announcer.Sample(Snap(), false));
    }

    [Fact]
    public void Another_center_text_is_announced()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Equal("ND message: SOMETHING NEW", announcer.Sample(Snap(center: "SOMETHING NEW"), false));
        Assert.Equal("ND message gone: SOMETHING NEW", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void A_listed_message_the_glass_does_not_draw_is_not_announced()
    {
        // The lower box holds the first entry that is not TA ONLY; a second one is not drawn.
        FenixChannelNdMessages announcer = Baselined(Snap(listed: ["GPS PRIMARY"]));

        Assert.Null(announcer.Sample(Snap(listed: ["GPS PRIMARY", "OTHER"]), false));
        Assert.Null(announcer.Sample(Snap(listed: ["GPS PRIMARY"]), false));
    }

    [Fact]
    public void Rose_ls_draws_no_waypoint_and_returning_to_arc_is_silent()
    {
        FenixChannelNdMessages announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(waypoint: "1500", mode: Ls), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "2000", mode: Ls), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "2000", mode: Arc), false));
        Assert.Equal("ND: 2500, 3 NM", announcer.Sample(Snap(waypoint: "2500", distance: "g3 csNM"), false));
    }

    [Fact]
    public void Plan_and_rose_nav_announce_the_waypoint()
    {
        Assert.Equal("ND: 1500, 3 NM",
            Baselined(Snap(mode: Plan)).Sample(Snap(waypoint: "1500", distance: "g3 csNM", mode: Plan), false));
        Assert.Equal("ND: 1500, 3 NM",
            Baselined(Snap(mode: Nav)).Sample(Snap(waypoint: "1500", distance: "g3 csNM", mode: Nav), false));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_distance_is_as_drawn_whatever_the_culture(string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            FenixChannelNdMessages announcer = Baselined();
            Assert.Equal("ND: 1500, 2.7 NM", announcer.Sample(Snap(waypoint: "1500", distance: "g2s.7 csNM"), false));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
