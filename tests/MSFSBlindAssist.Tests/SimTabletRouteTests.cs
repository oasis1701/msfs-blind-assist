// Characterization tests for SimTabletRoute (Services/SimTabletRoute.cs): the SimBrief route as the
// sim tablet's "Enter route string" box takes it, the reading of the route the tablet shows, and
// the sentence the import speaks. The OFP and the tablet state are the ones measured live on
// 2026-10-19 (C172 G1000, KFFC to KSAV).

using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class SimTabletRouteTests
{
    private static SimBriefOFP Ofp(string origin = "KFFC", string route = "DCT SOONE DCT NOKIE DCT", string dest = "KSAV") =>
        new() { OriginIcao = origin, Route = route, DestIcao = dest, OriginRunway = "31", DestRunway = "01" };

    [Fact]
    public void TheRouteIsOriginRouteDestination_TheShapeTheTabletLoaded()
    {
        Assert.Equal("KFFC DCT SOONE DCT NOKIE DCT KSAV", SimTabletRoute.Build(Ofp()));
    }

    [Fact]
    public void ExtraSpacesAreCollapsed_AndIcaosUppercased()
    {
        Assert.Equal("KFFC DCT SOONE KSAV", SimTabletRoute.Build(Ofp(" kffc ", "  DCT   SOONE  ", "ksav")));
    }

    [Fact]
    public void AnEmptyRouteStillGivesBothAirports()
    {
        Assert.Equal("KFFC KSAV", SimTabletRoute.Build(Ofp(route: "")));
    }

    [Theory]
    [InlineData("", "KSAV")]
    [InlineData("KFFC", "")]
    public void NoOriginOrNoDestination_IsNoRoute(string origin, string dest)
    {
        Assert.Null(SimTabletRoute.Build(Ofp(origin, "DCT", dest)));
    }

    [Theory]
    [InlineData("01", "1")]
    [InlineData("1", "1")]
    [InlineData("runway 01", "1")]
    [InlineData("RW01", "1")]
    [InlineData("runway 16L", "16L")]
    [InlineData("16l", "16L")]
    [InlineData("", "")]
    [InlineData("runway", "")]
    public void ARunwayKeyDropsTheLeadingZeroAndKeepsTheSide(string input, string key)
    {
        Assert.Equal(key, SimTabletRoute.RunwayKey(input));
    }

    // The reader's route.state() on the live Route page after the import and the runway pick.
    private const string LiveState =
        "{\"ok\":true,\"atlas\":true,\"routeShown\":true,\"airports\":[" +
        "{\"text\":\"KFFC, Atlanta Rgnl Falcon ...\",\"icao\":\"KFFC\",\"runway\":\"runway 31\",\"runwayKey\":\"31\",\"runways\":[\"runway 13\",\"runway 31\"]}," +
        "{\"text\":\"KSAV, Savannah/Hilton Head...\",\"icao\":\"KSAV\",\"runway\":\"runway 01\",\"runwayKey\":\"1\",\"runways\":[\"runway 01\",\"runway 10\",\"runway 19\",\"runway 28\"]}," +
        "{\"text\":\"\",\"icao\":\"\",\"runway\":\"\",\"runwayKey\":\"\",\"runways\":[]}]," +
        "\"procedures\":[\"None\",\"None\",\"ILS 1 via Direct\"],\"waypoints\":[\"SOONE\",\"NOKIE\"],\"routeBoxOpen\":false}";

    [Fact]
    public void TheStateParses_AndTheAirportsMatchThePlan()
    {
        var st = SimTabletRoute.ParseState(LiveState);
        Assert.NotNull(st);
        Assert.True(SimTabletRoute.HasAirports(st!, "KFFC", "KSAV"));
        Assert.True(SimTabletRoute.HasAirports(st!, "kffc", "ksav"));
        Assert.False(SimTabletRoute.HasAirports(st!, "KFFC", "KATL"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"ok\":false,\"error\":\"x\"}")]
    public void AnythingButAStateIsNull(string json)
    {
        Assert.Null(SimTabletRoute.ParseState(json));
    }

    [Fact]
    public void TheSummarySaysBothEnds_TheWaypoints_AndTheApproachTheTabletChose()
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        Assert.Equal(
            "SimBrief plan loaded: KFFC runway 31 to KSAV runway 01, 2 waypoints. Approach: ILS 1 via Direct. " +
            "Use Send to avionics when you are ready.",
            SimTabletRoute.Summary(st));
    }

    [Fact]
    public void TheImportsNotesComeBeforeTheSendHint()
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        string s = SimTabletRoute.Summary(st, new[] { "SimBrief planned runway 05 at KSAV, which the tablet does not list." });
        Assert.EndsWith("Approach: ILS 1 via Direct. SimBrief planned runway 05 at KSAV, which the tablet does not list. Use Send to avionics when you are ready.", s);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void TheSummaryIsTheSameUnderAnyCulture(string culture)
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        Assert.Equal(SimTabletRoute.Summary(st), TestCulture.Run(culture, () => SimTabletRoute.Summary(st)));
        Assert.Equal("1", TestCulture.Run(culture, () => SimTabletRoute.RunwayKey("01")));
    }

    [Fact]
    public void OneWaypointIsSingular_AndAnAirportWithNoRunwayIsJustItsIcao()
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        st.waypoints = new List<string> { "SOONE" };
        st.airports[1].runway = "";
        string s = SimTabletRoute.Summary(st);
        Assert.StartsWith("SimBrief plan loaded: KFFC runway 31 to KSAV, 1 waypoint.", s);
    }
}
