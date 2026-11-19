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
        ulong mode = Arc, bool trusted = true, int connection = 1, bool broken = false, string? windshear = null,
        string? tcas = null, Action<FenixChannelProtoWriter>? more = null)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, mode)).Message(1, new FenixChannelProtoWriter())
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456").String(2, "g213w / g37"));
        if (center is not null) remote.String(62, center);
        if (windshear is not null) remote.String(64, windshear);
        if (tcas is not null) remote.String(66, tcas);
        if (waypoint is not null)
        {
            var nav = new FenixChannelProtoWriter().String(1, waypoint);
            if (distance is not null) nav.String(2, distance);
            remote.Message(68, nav);
        }

        foreach (string entry in listed ?? []) remote.Message(103, new FenixChannelProtoWriter().String(1, entry));
        if (broken) remote.Message(68, new FenixChannelProtoWriter().Double(2, 1));
        more?.Invoke(remote);

        FenixChannelPush data = FenixChannelPush.From(remote.ToArray())!;
        return new FenixChannelSnapshot(
            new FenixDisplayVerdict(trusted, trusted ? "trusted" : "off"), null, data, null, 0, connection);
    }

    // The announcer with its clock: every sample is 100 ms after the one before, the host's tick.
    private sealed class Rig
    {
        private readonly FenixChannelNdMessages _announcer = new();
        private long _ms;

        // One sample.
        public string? Once(FenixChannelSnapshot s, bool muted) => _announcer.Sample(s with { AtMs = _ms += 100 }, muted);

        // The same reading sampled until it has held the settle: what the last sample says, every one before it silent.
        public string? Sample(FenixChannelSnapshot s, bool muted)
        {
            long start = _ms + 100;
            string? said = Once(s, muted);
            while (_ms - start < FenixChannelNdMessages.SettleMs)
            {
                Assert.Null(said);
                said = Once(s, muted);
            }

            return said;
        }
    }

    // A rig that has taken its baseline on a quiet ND.
    private static Rig Baselined(FenixChannelSnapshot? first = null)
    {
        var announcer = new Rig();
        Assert.Null(announcer.Sample(first ?? Snap(), false));
        return announcer;
    }

    [Fact]
    public void The_first_reading_is_silent()
    {
        var announcer = new Rig();
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
    }

    [Fact]
    public void A_message_missing_from_one_sample_says_nothing()
    {
        // A message left out of one push, or a list the ND redraws for a frame: back before the settle, it never went.
        Rig announcer = Baselined(Snap(listed: ["TA ONLY"]));

        Assert.Null(announcer.Once(Snap(), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
    }

    [Fact]
    public void A_change_is_spoken_once_it_has_held_300_ms()
    {
        Rig announcer = Baselined();

        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));   // the hold starts
        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));   // 100 ms
        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));   // 200 ms
        Assert.Equal("ND message: TA ONLY", announcer.Once(Snap(listed: ["TA ONLY"]), false));   // 300 ms
        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));
    }

    [Fact]
    public void A_reading_the_nd_passes_through_is_never_spoken()
    {
        // TA ONLY shows for 200 ms, then WINDSHEAR replaces it and holds: only WINDSHEAR was on the ND long enough.
        Rig announcer = Baselined();

        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));
        Assert.Null(announcer.Once(Snap(listed: ["TA ONLY"]), false));
        Assert.Equal("ND message: WINDSHEAR", announcer.Sample(Snap(center: "WINDSHEAR"), false));
    }

    [Fact]
    public void A_new_message_is_spoken_once()
    {
        Rig announcer = Baselined();

        Assert.Equal("ND message: MAP PARTLY DISPLAYED, amber", announcer.Sample(Snap(listed: ["aMAP PARTLY DISPLAYED"]), false));
        Assert.Null(announcer.Sample(Snap(listed: ["aMAP PARTLY DISPLAYED"]), false));
    }

    [Fact]
    public void A_message_that_goes_is_spoken_once()
    {
        Rig announcer = Baselined(Snap(listed: ["TA ONLY"]));

        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(), false));
        Assert.Null(announcer.Sample(Snap(), false));
    }

    [Fact]
    public void Several_at_once_share_one_line()
    {
        Rig announcer = Baselined();
        Assert.Equal("ND message: WINDSHEAR, TA ONLY",
            announcer.Sample(Snap(listed: ["TA ONLY"], center: "WINDSHEAR"), false));
        Assert.Equal("ND message gone: WINDSHEAR, TA ONLY", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void New_gone_and_waypoint_share_one_line_in_that_order()
    {
        Rig announcer = Baselined(Snap(listed: ["TA ONLY"]));

        Assert.Equal("ND message: WINDSHEAR. ND message gone: TA ONLY. ND: 1500, 2.7 NM",
            announcer.Sample(Snap(center: "WINDSHEAR", waypoint: "1500", distance: "g2s.7 csNM"), false));
    }

    [Fact]
    public void An_active_waypoint_change_is_spoken()
    {
        Rig announcer = Baselined();

        Assert.Equal("ND: 1500, 2.7 NM", announcer.Sample(Snap(waypoint: "1500", distance: "g2s.7 csNM"), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "1500", distance: "g2s.6 csNM"), false));
        Assert.Equal("ND: ABC, 24 NM", announcer.Sample(Snap(waypoint: "ABC g090c", distance: "g24 csNM"), false));
        Assert.Equal("ND: DEF", announcer.Sample(Snap(waypoint: "DEF", distance: null), false));
    }

    [Fact]
    public void Ppos_is_no_active_waypoint()
    {
        Rig announcer = Baselined(Snap(waypoint: "1500", distance: "g2s.7 csNM"));

        Assert.Equal("ND: no active waypoint", announcer.Sample(Snap(waypoint: "PPOS     ", distance: null), false));
        Assert.Null(announcer.Sample(Snap(waypoint: "PPOS", distance: null), false));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        Rig announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), true));
        Assert.Null(announcer.Sample(Snap(waypoint: "1500"), true));
    }

    [Fact]
    public void Unmuting_replays_nothing()
    {
        Rig announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), true));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], waypoint: "1500"), false));
        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(waypoint: "1500"), false));
    }

    [Fact]
    public void An_untrusted_nd_is_silent()
    {
        Rig announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], trusted: false), false));
        // The first good reading after the gap is the new baseline: what came meanwhile is not news.
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
        Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void A_reading_with_a_problem_is_a_gap()
    {
        Rig announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], broken: true), false));
        Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"]), false));
    }

    [Fact]
    public void An_unknown_letter_in_a_line_a_layer_reads_is_a_gap()
    {
        // ROSE LS's block (72) and, in ARC, a corner (76.1): the window says "ND: not available", so the announcer is
        // silent and takes the next good reading as its baseline.
        Action<FenixChannelProtoWriter> ils = r => r.Message(72, new FenixChannelProtoWriter().String(1, "wILS2 z110"));
        Action<FenixChannelProtoWriter> corner = r => r.Message(76, new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "wVOR1z")));

        foreach ((ulong mode, Action<FenixChannelProtoWriter> letter) in new[] { (Ls, ils), (Arc, corner) })
        {
            Rig announcer = Baselined(Snap(mode: mode));

            Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], mode: mode, more: letter), false));
            Assert.Null(announcer.Sample(Snap(listed: ["TA ONLY"], mode: mode), false));
            Assert.Equal("ND message gone: TA ONLY", announcer.Sample(Snap(mode: mode), false));
        }
    }

    [Fact]
    public void A_new_connection_is_silent()
    {
        Rig announcer = Baselined();

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
        Rig announcer = Baselined();

        Assert.Null(announcer.Sample(Snap(center: text), false));
        Assert.Null(announcer.Sample(Snap(), false));
    }

    // Each center message is spoken by the rule of its own field: 62's usual color is white, 64 and 66 have no measured
    // set, so every color of theirs is spoken.
    [Theory]
    [InlineData(null, "gWINDSHEAR", null, "ND message: WINDSHEAR, green")]
    [InlineData(null, null, "gTCAS FAIL", "ND message: TCAS FAIL, green")]
    [InlineData("gCENTER", null, null, "ND message: CENTER, green")]
    [InlineData("wCENTER", null, null, "ND message: CENTER")]
    [InlineData("wCENTER", "gWINDSHEAR", null, "ND message: CENTER, WINDSHEAR, green")]
    [InlineData("gCENTER", "wWINDSHEAR", null, "ND message: CENTER, green, WINDSHEAR, white")]
    public void Each_center_message_follows_its_own_fields_colors(string? center, string? windshear, string? tcas, string said)
    {
        Rig announcer = Baselined();

        Assert.Equal(said, announcer.Sample(Snap(center: center, windshear: windshear, tcas: tcas), false));
    }

    [Fact]
    public void A_windshear_message_keeps_its_color_in_the_window_too()
    {
        string[] rows = [.. FenixChannelNdRows.Compose(Snap(windshear: "gWINDSHEAR"), _ => { }).Rows!];

        Assert.Contains("Message: WINDSHEAR, green", rows);
    }

    [Fact]
    public void A_center_message_is_white_by_rule_only_as_field_62()
    {
        string[] rows = [.. FenixChannelNdRows.Compose(Snap(center: "wCENTER", tcas: "wTCAS"), _ => { }).Rows!];

        Assert.Contains("Message: CENTER", rows);
        Assert.Contains("Message: TCAS, white", rows);
    }

    [Fact]
    public void Another_center_text_is_announced()
    {
        Rig announcer = Baselined();

        Assert.Equal("ND message: SOMETHING NEW", announcer.Sample(Snap(center: "SOMETHING NEW"), false));
        Assert.Equal("ND message gone: SOMETHING NEW", announcer.Sample(Snap(), false));
    }

    [Fact]
    public void A_listed_message_the_glass_does_not_draw_is_not_announced()
    {
        // The lower box holds the first entry that is not TA ONLY; a second one is not drawn.
        Rig announcer = Baselined(Snap(listed: ["GPS PRIMARY"]));

        Assert.Null(announcer.Sample(Snap(listed: ["GPS PRIMARY", "OTHER"]), false));
        Assert.Null(announcer.Sample(Snap(listed: ["GPS PRIMARY"]), false));
    }

    [Fact]
    public void Rose_ls_draws_no_waypoint_and_returning_to_arc_is_silent()
    {
        Rig announcer = Baselined();

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
            Rig announcer = Baselined();
            Assert.Equal("ND: 1500, 2.7 NM", announcer.Sample(Snap(waypoint: "1500", distance: "g2s.7 csNM"), false));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
