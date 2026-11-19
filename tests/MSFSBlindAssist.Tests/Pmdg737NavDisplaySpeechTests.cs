// What the navigation display reader says by itself: the ND's messages appearing and going (one line each
// way, the prefix once, in list order) and the active waypoint changing, from two readings; and its status
// lines. Nothing while the displays are dark; messages only while the Captain's ND is on a display.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;
using S = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplaySpeech;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplaySpeechTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;

    /// <summary>The live reading with TCAS in STBY (TCAS OFF), as on the ground.</summary>
    private static M Ground()
    {
        var m = new M();
        m.SetCondition("TCAS OFF");
        return m;
    }

    private static NavDisplaySample With(Action<M> change)
    {
        var m = Ground();
        change(m);
        return m.Read();
    }

    [Fact]
    public void Message_appearing()
    {
        Assert.Equal(new[] { "ND message: TERRAIN" }, S.Changes(Ground().Read(), With(m => m.SetCondition("TERRAIN")), L));
    }

    [Fact]
    public void Message_going()
    {
        Assert.Equal(new[] { "ND message gone: TERRAIN" }, S.Changes(With(m => m.SetCondition("TERRAIN")), Ground().Read(), L));
    }

    [Fact]
    public void Appearing_and_going_in_one_window_are_two_lines()
    {
        // TCAS from STBY to TA/RA with the TFC button on in MAP: TCAS OFF goes, TFC appears.
        var before = Ground().Read();
        var m = new M();
        m.SetCondition("TFC");
        Assert.Equal(new[] { "ND message: TFC", "ND message gone: TCAS OFF" }, S.Changes(before, m.Read(), L));
    }

    [Fact]
    public void Two_messages_one_line_prefix_once()
    {
        var after = With(m =>
        {
            m.SetCondition("TERRAIN");
            m.SetCondition("WINDSHEAR");
        });
        Assert.Equal(new[] { "ND message: TERRAIN, WINDSHEAR" }, S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void Waypoint_sequencing_says_name_and_distance()
    {
        var after = With(m => m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2]));
        Assert.Equal(new[] { "ND: PT753, 23.0 NM" }, S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void A_vector_leg_says_its_dashes()
    {
        var after = With(m =>
        {
            m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2]);
            m.I(m.RouteNodes[2] + (uint)L.Route.NodeType, L.Route.NoDataLegType);
        });
        Assert.Equal(new[] { "ND: PT753, --- NM" }, S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void Route_deleted_says_no_active_waypoint()
    {
        var after = With(m => m.SetKeys(0, 0, 0));
        Assert.Equal(new[] { "ND: no active waypoint" }, S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void Same_waypoint_new_distance_is_silent()
    {
        var after = With(m => m.D(m.RouteNodes[1] + (uint)L.Route.NodeDistance, 6.5));
        Assert.Empty(S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void Messages_then_the_waypoint()
    {
        var after = With(m =>
        {
            m.SetCondition("TERRAIN");
            m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2]);
        });
        Assert.Equal(new[] { "ND message: TERRAIN", "ND: PT753, 23.0 NM" }, S.Changes(Ground().Read(), after, L));
    }

    [Fact]
    public void Nothing_while_the_displays_are_dark()
    {
        var u = L.Units;
        var dark = With(m =>
        {
            for (int k = 0; k < u.UnitCount; k++) m.I(M.Efis + (uint)(k * u.UnitStride + u.PageOffset), u.DarkPages[0]);
        });
        var terrain = With(m => m.SetCondition("TERRAIN"));
        Assert.Empty(S.Changes(dark, terrain, L));
        Assert.Empty(S.Changes(terrain, dark, L));
    }

    [Fact]
    public void Messages_only_while_the_nd_is_on_a_display()
    {
        // The ND moved off its display: its messages are not compared (they did not go: no one draws them);
        // the active waypoint is the FMC's and still is.
        var notShown = With(m =>
        {
            m.I(M.Efis + (uint)L.Units.PageOffset, 2);
            m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2]);
        });
        Assert.Equal(new[] { "ND: PT753, 23.0 NM" }, S.Changes(With(m => m.SetCondition("TERRAIN")), notShown, L));
    }

    [Fact]
    public void Sentences_are_invariant_under_sv_SE()
    {
        var before = Ground().Read();
        var after = With(m => m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2]));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(new[] { "ND: PT753, 23.0 NM" }, S.Changes(before, after, L));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Status_lines()
    {
        Assert.Equal("Navigation display starting", S.Starting);
        Assert.Equal("ND announcements resumed", S.Resumed);
        Assert.Equal("ND announcements unavailable: PMDG file not recognised", S.Unavailable(FmaReasons.FileNotRecognised));
        Assert.Equal("ND announcements stopped: values do not match the SDK", S.Stopped(FmaReasons.Mismatch));
        Assert.Equal("Navigation display unavailable: aircraft memory not found", S.ListUnavailable(FmaReasons.MemoryNotFound));
        Assert.Equal("Navigation display stopped: values do not match the SDK, trying again", S.ListStopped(FmaReasons.Mismatch));
        Assert.Equal("ND waypoint unavailable: aircraft memory not found", S.WaypointUnavailable(FmaReasons.MemoryNotFound));
        Assert.Equal("No active waypoint", S.NoActiveWaypoint);
    }
}
