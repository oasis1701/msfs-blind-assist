// Characterization tests for FenixChannelNdWaypoint, Ctrl+W's sentence from the ND's next-waypoint lines. The ND's
// order, no colors, the parts joined by ", ", the figures as the ND draws them.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdWaypointTests
{
    private static FenixChannelSnapshot Snapshot(string? one, string? two, string? three, bool trusted = true,
        bool wrongShape = false, ulong? mode = null, Action<FenixChannelProtoWriter>? more = null)
    {
        var nav = new FenixChannelProtoWriter();
        if (one is not null) nav.String(1, one);
        if (two is not null) nav.String(2, two);
        if (three is not null) nav.String(3, three);

        var efis = new FenixChannelProtoWriter().Varint(3, 3);
        if (mode is not null) efis.Varint(1, mode.Value);
        var remote = new FenixChannelProtoWriter()
            .Message(1, efis).Message(1, new FenixChannelProtoWriter())
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456").String(2, "g213w / g37"));
        if (wrongShape) remote.Message(68, new FenixChannelProtoWriter().Double(2, 1));
        else if (one is not null || two is not null || three is not null) remote.Message(68, nav);
        more?.Invoke(remote);

        FenixChannelPush data = FenixChannelPush.From(remote.ToArray())!;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(trusted, trusted ? "trusted" : "off"), null, data, null, 0, 1);
    }

    private static string? Readout(FenixChannelSnapshot snapshot, List<string>? problems = null)
        => FenixChannelNdWaypoint.Readout(snapshot, (problems ?? []).Add);

    [Fact]
    public void The_cruise_waypoint_reads_in_the_nds_order()
    {
        Assert.Equal("VAKSU, 318 degrees, 84 nautical miles, 17:29",
            Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29")));
    }

    [Fact]
    public void The_gate_waypoint_reads_with_its_degree_sign()
    {
        Assert.Equal("INTCPT, 084 degrees, 2.8 nautical miles",
            Readout(Snapshot("INTCPT g084c°", "g2s.8 csNM", null)));
    }

    [Fact]
    public void Ppos_is_no_active_waypoint()
    {
        Assert.Equal("No active waypoint", FenixChannelNdWaypoint.NoActiveWaypoint);
        Assert.Equal("No active waypoint", Readout(Snapshot("PPOS     ", null, null)));
        Assert.Equal("No active waypoint", Readout(Snapshot("PPOS", "g84 csNM", "g17:29")));
    }

    [Fact]
    public void An_untrusted_nd_or_a_problem_gives_not_available()
    {
        Assert.Null(Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", trusted: false)));

        var problems = new List<string>();
        Assert.Null(Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", wrongShape: true), problems));
        Assert.Equal(["field 68.2 has the wrong shape"], problems);

        problems.Clear();
        Assert.Null(Readout(Snapshot(null, null, null), problems));
        Assert.Empty(problems);

        Assert.Null(Readout(Snapshot("", "g84 csNM", "g17:29")));
        Assert.Null(Readout(Snapshot("-----", "g84 csNM", "g17:29")));
        Assert.Null(Readout(Snapshot("VAKSU 400", "g84 csNM", "g17:29")));

        // A letter the parser does not know stops the whole ND, Ctrl+W included.
        problems.Clear();
        Assert.Null(Readout(Snapshot("VAKSU q318", "g84 csNM", "g17:29"), problems));
        Assert.Single(problems);
    }

    [Fact]
    public void An_unknown_letter_in_a_line_a_layer_reads_gives_not_available()
    {
        // ROSE LS reads its block (72), every mode but PLAN the corners (76): a letter the app does not know in either
        // stops the whole ND, Ctrl+W included, as the window's layers do.
        static void Ils(FenixChannelProtoWriter remote, string line1)
            => remote.Message(72, new FenixChannelProtoWriter().String(1, line1));
        static void Corner(FenixChannelProtoWriter remote, string line1)
            => remote.Message(76, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().String(1, line1)));
        const ulong Ls = 0, Arc = 3;

        var problems = new List<string>();
        Assert.Null(Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", mode: Ls, more: r => Ils(r, "wILS2 z110")), problems));
        Assert.Equal(["unknown code 'z' in the ILS block"], problems);

        problems.Clear();
        Assert.Null(Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", mode: Arc, more: r => Corner(r, "wVOR1z")), problems));
        Assert.Equal(["unknown code 'z' in the corner text"], problems);

        // The same lines without the letter read; and ARC does not read 72, so its letter stops nothing there.
        Assert.Equal("VAKSU, 318 degrees, 84 nautical miles, 17:29",
            Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", mode: Ls, more: r => Ils(r, "wILS2 m110"))));
        Assert.Equal("VAKSU, 318 degrees, 84 nautical miles, 17:29",
            Readout(Snapshot("VAKSU g318c", "g84 csNM", "g17:29", mode: Arc, more: r => Ils(r, "wILS2 z110"))));
    }

    [Fact]
    public void A_part_the_nd_does_not_show_is_left_out()
    {
        Assert.Equal("VAKSU, 318 degrees, 84 nautical miles", Readout(Snapshot("VAKSU g318c", "g84 csNM", "g--:--")));
        Assert.Equal("VAKSU, 318 degrees, 17:29", Readout(Snapshot("VAKSU g318c", "", "g17:29")));
        Assert.Equal("VAKSU, 318 degrees, 17:29", Readout(Snapshot("VAKSU g318c", null, "g17:29")));
        Assert.Equal("VAKSU", Readout(Snapshot("VAKSU", "g---csNM", null)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_tenth_of_a_mile_is_spoken_as_drawn_in_every_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("VAKSU, 318 degrees, 8.4 nautical miles, 17:29",
                Readout(Snapshot("VAKSU g318c", "g8.4 csNM", "g17:29")));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Is_shown_follows_line_one()
    {
        Assert.True(FenixChannelNdWaypoint.IsShown(new FenixNdWaypointLines("PPOS     ", "", "")));
        Assert.True(FenixChannelNdWaypoint.IsShown(new FenixNdWaypointLines("VAKSU g318c", "", "")));
        Assert.False(FenixChannelNdWaypoint.IsShown(new FenixNdWaypointLines("-----", "g84 csNM", "")));
        Assert.False(FenixChannelNdWaypoint.IsShown(new FenixNdWaypointLines("", "g84 csNM", "")));
    }
}
