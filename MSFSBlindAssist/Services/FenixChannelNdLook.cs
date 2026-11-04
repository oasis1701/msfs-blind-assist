namespace MSFSBlindAssist.Services;

/// <summary>
/// One symbol the ND draws and the color the captures showed it in. <paramref name="Color"/> is null for a symbol the
/// ND draws in no fixed color. <paramref name="Evidence"/> names the entry of docs/fenix-channel/evidence/03-nd.md that
/// measured it.
/// </summary>
internal sealed record FenixNdLookEntry(string Item, string Symbol, string? Color, string Evidence);

/// <summary>
/// The table of looks the captures measured: every symbol and fixed color a row of the ND window speaks that does not
/// come from the channel's own color letters (spec 2.6, row 52). A row never speaks a color the table does not hold,
/// so a color is never guessed. Each task that adds a layer adds its entries here, each citing its evidence.
/// </summary>
internal static class FenixChannelNdLook
{
    private const string MapPointsEvidence = "03-nd.md, Task 1 Part C, the looks of the map points";
    private const string RouteLooks = "03-nd.md, Task 1 Part B, the route's looks";
    private const string RouteInFlight = "03-nd.md, sitting 2, the route in flight";
    private const string Constraints = "03-nd.md, sitting 2, the markers (2.12), the constraints";
    private const string Markers = "03-nd.md, sitting 2, the markers (2.12)";
    private const string Rings = "03-nd.md, Task 1 Part B, rings, the bug and FIX INFO";
    private const string Terrain = "03-nd.md, Task 1 Part B, terrain and weather";
    private const string HeaderInFlight = "03-nd.md, sitting 2, the header in flight";
    private const string MapDataInFlight = "03-nd.md, Sitting 2, GetMapData in flight";
    private const string TunedCaptures =
        MapDataInFlight + " (disco-arc160-210338, cap/211030: RMG; fixinfo-211836: VXV; efis/215457-restored: CVG)";
    private const string TunedKind3 = "; measured on kind 3 (DBN, RMG, ODF, VXV, LOZ, CVG)";
    private const string TunedUnmeasured = "; kinds 2 and 4 follow the same rule, unmeasured";

    // A marker's color is the channel's (its member 5): its entry holds the symbol only.
    private static readonly FenixNdLookEntry[] Entries =
    [
        new("lubber line", "lubber line", "yellow", "03-nd.md, Task 1 Part A, the header as drawn"),
        new("track diamond", "track diamond", "green", "03-nd.md, Task 1 Part A, the header as drawn"),
        new(FenixChannelNdMapData.LookItem.VorDme, "circle with a cross", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Dme, "circle", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Ndb, "triangle", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Vor, "cross", "magenta",
            MapDataInFlight + " (efis/211133-arc320-vord: MYS, FFT, XUB, ODR)"),
        new(FenixChannelNdMapData.LookItem.TunedVorDme, "circle with a cross", "cyan", TunedCaptures + TunedKind3),
        new(FenixChannelNdMapData.LookItem.TunedVor, "cross", "cyan", TunedCaptures + TunedKind3 + TunedUnmeasured),
        new(FenixChannelNdMapData.LookItem.TunedDme, "circle", "cyan", TunedCaptures + TunedKind3 + TunedUnmeasured),
        new(FenixChannelNdMapData.LookItem.Airport, "star", "magenta", MapPointsEvidence),
        new(FenixChannelNdMapData.LookItem.Fix, "diamond", "magenta", MapPointsEvidence),
        new(FenixChannelNdRoute.LookItem.ToWaypoint, "diamond", "white", RouteInFlight + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.Waypoint, "diamond", "green", RouteInFlight + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.Vor, "VOR symbol", "green", RouteLooks + " (VHP, on the alternate)"),
        new(FenixChannelNdRoute.LookItem.ToVor, "VOR symbol", null,
            RouteLooks + " (VHP, on the alternate); a TO waypoint's color on it is not measured"),
        new(FenixChannelNdRoute.LookItem.Runway, "runway symbol", "white", RouteInFlight + "; " + RouteLooks),
        new(FenixChannelNdRoute.LookItem.RouteLine, "solid line", "green", RouteInFlight + " (58.3 sent, NAV)"),
        new(FenixChannelNdRoute.LookItem.DashedRouteLine, "dashed line", "green", RouteInFlight + " (58.3 left out, HDG)"),
        new(FenixChannelNdRoute.LookItem.TemporaryLine, "dashed line", "yellow", RouteLooks + " (the temporary plan)"),
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
            "03-nd.md, Task 1 Part A, the header as drawn; " + HeaderInFlight)
    ];

    /// <summary>Every entry, in the order they were measured.</summary>
    public static IReadOnlyList<FenixNdLookEntry> All => Entries;

    /// <summary>The look of <paramref name="item"/>, or null when no capture measured it.</summary>
    public static FenixNdLookEntry? For(string item)
        => Entries.FirstOrDefault(entry => string.Equals(entry.Item, item, StringComparison.Ordinal));
}
