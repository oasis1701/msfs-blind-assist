namespace MSFSBlindAssist.Services;

/// <summary>
/// One symbol the ND draws and the color the captures showed it in. <paramref name="Color"/> is null for a symbol the
/// ND draws in no fixed color. <paramref name="Evidence"/> names the entry of evidence03-nd.md that
/// measured it.
/// </summary>
internal sealed record FenixNdLookEntry(string Item, string Symbol, string? Color, string Evidence)
{
    /// <summary>
    /// The color is spoken: it tells items of one kind apart (the magenta VOR/DME against the cyan tuned one, the TO
    /// waypoint against a route waypoint) or it is red. For every other entry the color never changes, so it is not
    /// spoken (the owner's decision, sitting 3); the evidence stays on the entry either way.
    /// </summary>
    public bool ColorTellsApart { get; init; }
}

/// <summary>
/// The table of looks the captures measured: every symbol and fixed color a row of the ND window speaks that does not
/// come from the channel's own color letters (spec 2.6, row 52). A row never speaks a color the table does not hold,
/// so a color is never guessed, and speaks one only where <see cref="FenixNdLookEntry.ColorTellsApart"/>. Each task
/// that adds a layer adds its entries here, each citing its evidence.
/// </summary>
internal static class FenixChannelNdLook
{
    private const string MapPointsEvidence = "03-nd.md, Task 1 Part C, the looks of the map points";
    private const string RouteLooks = "03-nd.md, Task 1 Part B, the route's looks";
    private const string RouteInFlight = "03-nd.md, sitting 2, the route in flight";
    private const string MissedApproach = "03-nd.md, Sitting 3, the missed approach";
    private const string Constraints = "03-nd.md, sitting 2, the markers (2.12), the constraints";
    private const string Markers = "03-nd.md, sitting 2, the markers (2.12)";
    private const string Rings = "03-nd.md, Task 1 Part B, rings, the bug and FIX INFO";
    private const string Terrain = "03-nd.md, Task 1 Part B, terrain and weather";
    private const string HeaderInFlight = "03-nd.md, sitting 2, the header in flight";
    private const string MapDataInFlight = "03-nd.md, Sitting 2, GetMapData in flight";
    private const string TunedCaptures =
        MapDataInFlight + " (disco-arc160-210338, cap/211030: RMG; fixinfo-211836: VXV; efis/215457-restored: CVG)";
    private const string RadioFlight = "03-nd.md, Sitting 2: the radio content and traffic";
    private const string TrafficFlight = "03-nd.md, Sitting 2: the radio content and traffic, Traffic: other and proximate";
    private const string RadioGate = "03-nd.md, Sitting 1, Part D: the radio content at the gate";
    private const string RadioGateSitting3 = "03-nd.md, Sitting 3, the gate";
    private const string TunedKind3= "; measured on kind 3 (DBN, RMG, ODF, VXV, LOZ, CVG)";
    private const string TunedUnmeasured = "; kinds 2 and 4 follow the same rule, unmeasured";

    // A marker's color is the channel's (its member 5): its entry holds the symbol only. An entry's color is spoken only
    // with ColorTellsApart: where the color tells items of one kind apart, or is red.
    private static readonly FenixNdLookEntry[] Entries =
    [
        new("lubber line", "lubber line", "yellow", "03-nd.md, Task 1 Part A, the header as drawn"),
        new("track diamond", "track diamond", "green", "03-nd.md, Task 1 Part A, the header as drawn"),
        new("wind arrow", "wind arrow", "green",
            "03-nd.md, Sitting 2, The header in flight (cap/205636, hdg-push-210207, cap/213017)"),
        new(FenixChannelNdMapData.LookItem.VorDme, "circle with a cross", "magenta", MapPointsEvidence)
            { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.Dme, "circle", "magenta", MapPointsEvidence) { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.Ndb, "triangle", "magenta", MapPointsEvidence) { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.Vor, "cross", "magenta",
            MapDataInFlight + " (efis/211133-arc320-vord: MYS, FFT, XUB, ODR)") { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.TunedVorDme, "circle with a cross", "cyan", TunedCaptures + TunedKind3)
            { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.TunedVor, "cross", "cyan", TunedCaptures + TunedKind3 + TunedUnmeasured)
            { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.TunedDme, "circle", "cyan", TunedCaptures + TunedKind3 + TunedUnmeasured)
            { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.Airport, "star", "magenta", MapPointsEvidence) { ColorTellsApart = true },
        new(FenixChannelNdMapData.LookItem.Fix, "diamond", "magenta", MapPointsEvidence) { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.ToWaypoint, "diamond", "white", RouteInFlight + "; " + RouteLooks)
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.Waypoint, "diamond", "green", RouteInFlight + "; " + RouteLooks)
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.Vor, "VOR symbol", "green", RouteLooks + " (VHP, on the alternate)")
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.ToVor, "VOR symbol", null,
            RouteLooks + " (VHP, on the alternate); a TO waypoint's color on it is not measured"),
        new(FenixChannelNdRoute.LookItem.Runway, "runway symbol", "white", RouteInFlight + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.RouteLine, "solid line", "green", RouteInFlight + " (58.3 sent, NAV)")
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.DashedRouteLine, "dashed line", "green", RouteInFlight + " (58.3 left out, HDG)")
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.MissedApproachLine, "solid line", "cyan", MissedApproach + " (appr\\120121, appr-arc40-441)")
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.MissedApproachHold, "arc", "white", MissedApproach + " (the hold at TROYS, ARC 40)"),
        new(FenixChannelNdRoute.LookItem.TemporaryLine, "dashed line", "yellow", RouteLooks + " (the temporary plan)")
            { ColorTellsApart = true },
        new(FenixChannelNdRoute.LookItem.ConstraintRing, "ring", null, Constraints + " (its color is member 5)"),
        new(FenixChannelNdRoute.LookItem.ConstraintLabel, "label", "magenta", Constraints + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.Tod, "arrow level then down", null, Markers),
        new(FenixChannelNdRoute.LookItem.Toc, "arrow climbing then level", null, Markers),
        new(FenixChannelNdRoute.LookItem.Eod, "arrow descending then level", null, Markers),
        new(FenixChannelNdRoute.LookItem.Decel, "circle with a D", null, Markers),
        new(FenixChannelNdRoute.LookItem.Cross, "zigzag ending in an arrow", null, Markers),
        new(FenixChannelNdRoute.LookItem.SpeedLimit, "filled dot", null, Markers + "; " + RouteLooks + " (PLAN 320)"),
        new(FenixChannelNdRoute.LookItem.SpeedLimitRose, "filled diamond", null, RouteLooks + " (ROSE 40)"),
        new(FenixChannelNdRoute.LookItem.Etp, "circle", null, Markers + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.HeadingBug, "hollow triangle", "cyan", RouteInFlight + " (58.2)"),
        new(FenixChannelNdRoute.LookItem.TrackLine, "thin solid line", "green", RouteInFlight + " (58.1)"),
        new(FenixChannelNdRoute.LookItem.CrossTrack, "figure", "green", RouteInFlight + " (52)"),
        new(FenixChannelNdRoute.LookItem.RangeRing, "dashed ring", "white", Rings),
        new(FenixChannelNdRoute.LookItem.CompassRing, "solid ring", "white", Rings),
        new(FenixChannelNdRoute.LookItem.FixInfoFix, "symbol", "green", RouteInFlight + " (FIX INFO: the fix's own symbol)"),
        new(FenixChannelNdRoute.LookItem.FixInfoLines, "dashed", "cyan", RouteInFlight + " (FIX INFO)"),
        new(FenixChannelNdRoute.LookItem.Terr, "TERR", "cyan", Terrain + " (53)"),
        new(FenixChannelNdRoute.LookItem.WxThH, "WX+T+H", "green", Terrain + " (191.4)"),
        new(FenixChannelNdRoute.LookItem.PwsScan, "PWS SCAN", "green", HeaderInFlight + " (191.8)"),
        new(FenixChannelNdRoute.LookItem.WxrOff, "WXR OFF", "white",
            "03-nd.md, Task 1 Part A, the header as drawn; " + HeaderInFlight),
        new(FenixChannelNdRadio.LookItem.CoursePointer, "course pointer", "magenta",
            RadioFlight + ", ROSE LS: the course pointer (efis/214719-ls20-nav, appr/215104, cap/211159-ls160)"),
        new(FenixChannelNdRadio.LookItem.LocalizerBar, "localizer bar", "magenta",
            RadioFlight + ", ROSE LS: the localizer deviation (82.1), the table of bar offsets"),
        new(FenixChannelNdRadio.LookItem.GlideslopeDiamond, "glideslope diamond", "magenta",
            RadioFlight + ", ROSE LS: the glideslope deviation (82.2), the whole diamond"),
        new(FenixChannelNdRadio.LookItem.GlideslopeHalfDiamond, "glideslope half diamond", "magenta",
            RadioFlight + ", ROSE LS: the glideslope deviation (82.2), the half diamond from 2.02 dots"),
        // The shape follows the side, the color the receiver (VOR white, ADF green); one entry covers both receivers, so
        // its Color is null and the receivers' colors are in the evidence.
        new(FenixChannelNdRadio.LookItem.PointerNav1, "single-line pointer", null,
            "VOR white, ADF green: " + RadioFlight + ", the bearing pointers (efis/211245-rose160-vord-nav, appr/215104: VOR on NAV 1); "
            + RadioGateSitting3 + " (efis-rose20-adf1-vor2, efis-vor20-adf1-vor2: ADF on NAV 1, a green single line)"),
        new(FenixChannelNdRadio.LookItem.PointerNav2, "double-line pointer", null,
            "VOR white, ADF green: " + RadioFlight + ", the bearing pointers (efis/211245-rose160-vord-nav and the appr captures: ADF on NAV 2); "
            + RadioGateSitting3 + " (efis-rose20-adf1-vor2, crop-adf1-vor2: VOR on NAV 2, a white double line)"),
        new(FenixChannelNdRadio.LookItem.ArrowNav1, "single-line arrow", null,
            "VOR white, ADF green: " + RadioFlight + ", the corners (214719: VOR on NAV 1); " + RadioGateSitting3
            + " (crop-adf1-vor2: ADF1, green)"),
        new(FenixChannelNdRadio.LookItem.ArrowNav2, "double-line arrow", null,
            "VOR white, ADF green: " + RadioFlight + ", the corners (214719: ADF on NAV 2); " + RadioGateSitting3
            + " (crop-adf1-vor2: VOR2, white hollow)"),
        new(FenixChannelNdRadio.LookItem.VorCoursePointer, "course pointer", "cyan",
            RadioGateSitting3 + " (efis-vor20-crs185: 80.1 = 185, the cross bar at the head, the tail toward 005, no reception)"),
        new(FenixChannelNdRadio.LookItem.VorDeviationBar, "deviation bar", "cyan",
            "03-nd.md, Sitting 3 (efis-vor20-ape-1, efis-vor20-flm-300, efis-vor20-flm-120)"),
        new(FenixChannelNdTraffic.LookItem.OtherTraffic, "diamond", "white",
            TrafficFlight + " (hollow: appr/214920, sel/203039; AAL626, DAL1959)"),
        new(FenixChannelNdTraffic.LookItem.ProximateTraffic, "diamond", "white",
            TrafficFlight + " (filled: appr/214824, sel/203039, sel/203026; AAL626, DAL1959, DAL2997)"),
        new(FenixChannelNdTraffic.LookItem.TrafficAdvisory, "filled circle", "amber", "03-nd.md, Sitting 3 (ta-120553)")
            { ColorTellsApart = true },
        new(FenixChannelNdTraffic.LookItem.TcasRing, "ticks", "white",
            TrafficFlight + ", the ring (efis/215457-restored, efis/212245); "
            + "03-nd.md, Sitting 3 (efis-arc10-ring, efis-rose10-ring: 2.5 NM at range 10; ta-120553)"),
        new(FenixChannelNdRadio.LookItem.FailedVorIdent, "VOR1", "red", RadioGate + ", VOR 1 failed: the top right")
            { ColorTellsApart = true },
        new(FenixChannelNdRadio.LookItem.FailedVorFrequency, "---.--", "cyan", RadioGate + ", VOR 1 failed: the top right"),
        new(FenixChannelNdRadio.LookItem.FailedCourseLabel, "CRS", "white", RadioGate + ", VOR 1 failed: the top right"),
        new(FenixChannelNdRadio.LookItem.FailedCourseFigure, "XX", "red", RadioGate + ", VOR 1 failed: the top right")
            { ColorTellsApart = true },
        new(FenixChannelNdRadio.LookItem.FailedVorFlag, "VOR", "red", RadioGate + ", VOR 1 failed: the center")
            { ColorTellsApart = true }
    ];

    /// <summary>Every entry, in the order they were measured.</summary>
    public static IReadOnlyList<FenixNdLookEntry> All => Entries;

    /// <summary>The look of <paramref name="item"/>, or null when no capture measured it.</summary>
    public static FenixNdLookEntry? For(string item)
        => Entries.FirstOrDefault(entry => string.Equals(entry.Item, item, StringComparison.Ordinal));
}
