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

    [Fact]
    public void TheSidGoesRightAfterTheOrigin_AndTheStarRightBeforeTheDestination_WhereTheParserLooks()
    {
        var ofp = Ofp("KATL", "PRMMM Q64 ONDRE", "KSAV");
        ofp.SidIdent = "PRMMM3";
        ofp.StarIdent = "ONDRE1";
        Assert.Equal("KATL PRMMM3 PRMMM Q64 ONDRE ONDRE1 KSAV", SimTabletRoute.Build(ofp));
    }

    [Fact]
    public void ASidOrStarTheRouteAlreadyCarries_IsNotWrittenTwice()
    {
        var ofp = Ofp("KATL", "PRMMM3 PRMMM Q64 ONDRE ONDRE1", "KSAV");
        ofp.SidIdent = "prmmm3";
        ofp.StarIdent = "ONDRE1";
        Assert.Equal("KATL PRMMM3 PRMMM Q64 ONDRE ONDRE1 KSAV", SimTabletRoute.Build(ofp));
    }

    [Fact]
    public void APlanWithOnlyProcedures_StillGetsThem()
    {
        var ofp = Ofp("KATL", "", "KSAV");
        ofp.SidIdent = "PRMMM3";
        ofp.StarIdent = "ONDRE1";
        Assert.Equal("KATL PRMMM3 ONDRE1 KSAV", SimTabletRoute.Build(ofp));
    }

    // The tablet's parser takes the level from a speed and level group's SPEED digits (measured:
    // N0112F030 set the cruise box to FL112), so such a group is never written.
    [Theory]
    [InlineData("N0112F030 DCT SOONE DCT NOKIE DCT")]
    [InlineData("DCT SOONE N0450F370 DCT NOKIE DCT")]
    [InlineData("M078F350 DCT SOONE DCT NOKIE DCT")]
    public void ASpeedAndLevelGroupIsNeverWritten(string route)
    {
        string built = SimTabletRoute.Build(Ofp(route: route))!;
        Assert.Equal("KFFC DCT SOONE DCT NOKIE DCT KSAV", built);
    }

    // The cruise altitude rides a speed and level group with the level in BOTH fields: the tablet's
    // parser takes the level from the speed digits (measured: N0030F030 set FL030, from FL112).
    [Theory]
    [InlineData("3000", "N0030F030")]
    [InlineData("37000", "N0370F370")]
    [InlineData("1500", "N0015F015")]
    [InlineData("4550", "N0046F046")]
    [InlineData("", null)]
    [InlineData("50", null)]
    [InlineData("100000", null)]
    [InlineData("high", null)]
    public void TheCruiseGroupCarriesTheFlightLevelInBothFields(string feet, string? group)
    {
        Assert.Equal(group, SimTabletRoute.CruiseGroup(feet));
        Assert.Equal(group, TestCulture.Run("de-DE", () => SimTabletRoute.CruiseGroup(feet)));
    }

    [Fact]
    public void TheCruiseGroupGoesRightAfterTheOrigin_BeforeTheSid()
    {
        var ofp = Ofp("KATL", "PRMMM Q64 ONDRE", "KSAV");
        ofp.SidIdent = "PRMMM3";
        ofp.StarIdent = "ONDRE1";
        ofp.InitialAltitude = "35000";
        Assert.Equal("KATL N0350F350 PRMMM3 PRMMM Q64 ONDRE ONDRE1 KSAV", SimTabletRoute.Build(ofp));
    }

    [Fact]
    public void SimBriefsOwnGroupIsReplacedByTheOneTheTabletReadsRight()
    {
        var ofp = Ofp(route: "N0112F030 DCT SOONE DCT NOKIE DCT");
        ofp.InitialAltitude = "3000";
        Assert.Equal("KFFC N0030F030 DCT SOONE DCT NOKIE DCT KSAV", SimTabletRoute.Build(ofp));
    }

    [Theory]
    [InlineData("30", "FL", "Cruise altitude: flight level 030, 3000 feet.")]
    [InlineData("370", "FL", "Cruise altitude: flight level 370, 37000 feet.")]
    [InlineData("4500", "FT", "Cruise altitude: 4500 feet.")]
    [InlineData("0", "FL", null)]
    [InlineData("", "FL", null)]
    public void TheCruisePhraseSaysTheTabletsReading(string value, string unit, string? phrase)
    {
        Assert.Equal(phrase, SimTabletRoute.CruisePhrase(value, unit));
        Assert.Equal(phrase, TestCulture.Run("sv-SE", () => SimTabletRoute.CruisePhrase(value, unit)));
    }

    [Fact]
    public void TheSummarySaysTheCruiseAltitudeAndAnyTabletWarning()
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        st.cruise = "30";
        st.cruiseUnit = "FL";
        st.cruiseWarnings = new List<string> { "The selected cruise altitude is below the departure or destination altitude." };
        Assert.Equal(
            "SimBrief plan loaded: KFFC runway 31 to KSAV runway 01, 2 waypoints. Cruise altitude: flight level 030, 3000 feet. " +
            "The tablet warns: The selected cruise altitude is below the departure or destination altitude. " +
            "Approach: ILS 1 via Direct. Use Send to avionics when you are ready.",
            SimTabletRoute.Summary(st, null, 3000));
    }

    [Fact]
    public void WithNoCruiseReadingTheSummarySaysTheOneSent()
    {
        var st = SimTabletRoute.ParseState(LiveState)!;
        Assert.Contains("2 waypoints. Cruise altitude sent: 3000 feet. Approach:", SimTabletRoute.Summary(st, null, 3000));
        Assert.DoesNotContain("Cruise", SimTabletRoute.Summary(st));
    }

    [Fact]
    public void TheSidAndStarAreReadFromGeneral_WhereSimBriefWritesThem()
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml("<OFP><general><route>PRMMM Q64 ONDRE</route><sid_ident>PRMMM3</sid_ident><star_ident>ONDRE1</star_ident></general>" +
                    "<origin><icao_code>KATL</icao_code><plan_rwy>26L</plan_rwy></origin>" +
                    "<destination><icao_code>KSAV</icao_code><plan_rwy>10</plan_rwy></destination></OFP>");
        var ofp = new SimBriefService().ParseFullOFP(doc);
        Assert.Equal("PRMMM3", ofp.SidIdent);
        Assert.Equal("ONDRE1", ofp.StarIdent);
        Assert.Equal("KATL PRMMM3 PRMMM Q64 ONDRE ONDRE1 KSAV", SimTabletRoute.Build(ofp));
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
