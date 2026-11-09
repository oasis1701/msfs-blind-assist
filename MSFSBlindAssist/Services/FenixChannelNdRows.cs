using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What one layer of the ND gives <c>FenixChannelNdRows.Compose</c>: its rows, a problem of its own that costs only its
/// rows, or a problem that stops the whole ND (a letter the app does not know, [FNX-24]).
/// </summary>
internal sealed record FenixNdLayer(IReadOnlyList<string> Rows, string? LayerProblem, string? NdProblem)
{
    /// <summary>A layer that was read: its rows.</summary>
    public static FenixNdLayer Of(IReadOnlyList<string> rows) => new(rows, null, null);

    /// <summary>A layer that could not be read: one row in its place, and the problem for the log.</summary>
    public static FenixNdLayer Failed(string name, string problem)
        => new([$"{name}: not available, {problem}"], $"{name}: {problem}", null);

    /// <summary>
    /// A problem that stops the whole ND, the other layers and the header with it. A layer that stops the ND on its
    /// channel text also gives that text to <see cref="FenixChannelNd.ReadChecked"/>'s check, through the reader it
    /// reads it with, so Ctrl+W and the announcer stop on it too (today the two radio layers).
    /// </summary>
    public static FenixNdLayer StopsNd(string problem) => new([], null, problem);
}

/// <summary>
/// The rows of the Fenix ND window: one snapshot of the display channel turned into what the captain's ND draws, in
/// screen order, each row starting with its place. The header's rows are built here; the route, map, radio and traffic
/// layers are functions of the snapshot and the header, run in screen order. A row the app builds from values speaks
/// a color only from <see cref="FenixChannelNdLook"/>, and only where it tells something; channel text speaks its own
/// through <see cref="FenixChannelNdSpeech"/>. Numbers are invariant whatever the PC's culture is.
/// </summary>
internal static partial class FenixChannelNdRows
{
    /// <summary>What <see cref="FenixChannelMonitor.ReportFeatureProblem"/> logs the window under.</summary>
    public const string Feature = "ND";

    private const string TaOnly = "TA ONLY";

    /// <summary>
    /// The rows for <paramref name="s"/> with no layer: the header and the messages only. The tests' overload; the window
    /// composes with the layered one, which takes the map points and the clock.
    /// </summary>
    public static FenixChannelRows Compose(FenixChannelSnapshot s, Action<string> reportProblem)
        => Compose(s, reportProblem, []);

    /// <summary>
    /// The rows for <paramref name="s"/> with the layers after the header, in screen order: "Radio" (the top right
    /// block, the center, the right side, the bearing pointers), "Route" (the route, its markers, the heading bug, the
    /// rings, FIX INFO), "Map points", "Traffic", "Radio corners" (the bottom corners), then "Terrain and weather" (the bottom
    /// right label).
    /// <paramref name="mapPoints"/> is the poller's latest reply, when it was last confirmed current (on the clock
    /// <paramref name="nowMs"/> reads) and its problem; the layer uses the points only under
    /// <see cref="FenixChannelNdMapData.Check"/> ([FNX-22]).
    /// </summary>
    internal static FenixChannelRows Compose(
        FenixChannelSnapshot s, Action<string> reportProblem,
        (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) mapPoints, long nowMs)
        => Compose(s, reportProblem,
        [
            (snapshot, nd) => snapshot.Push is { } push ? FenixChannelNdRadio.Rows(push, nd) : FenixNdLayer.Of([]),
            FenixChannelNdRoute.Layer,
            (snapshot, nd) => MapPointsLayer(snapshot, nd, mapPoints, nowMs),
            (snapshot, nd) => snapshot.Push is { } push ? FenixChannelNdTraffic.Rows(push, nd) : FenixNdLayer.Of([]),
            (snapshot, nd) => snapshot.Push is { } push ? FenixChannelNdRadio.CornerRows(push, nd) : FenixNdLayer.Of([]),
            FenixChannelNdRoute.LabelsLayer
        ]);

    private const string MapPointsName = "Map points";

    // The mode decides first: the ND draws map points in ROSE NAV, ARC and PLAN, and with any option, since the tuned
    // navaid is drawn with none (sitting 2; Ruling 50). In ROSE LS and ROSE VOR the layer gives no rows at all,
    // whatever the map data, its problem or its absence.
    private static FenixNdLayer MapPointsLayer(
        FenixChannelSnapshot s, FenixNdData nd, (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) mapPoints,
        long nowMs)
    {
        if (nd.Mode is not ("NAV" or "ARC" or "PLAN"))
        {
            return FenixNdLayer.Of([]);
        }

        FenixNdFrame? frame = FenixNdFrame.From(nd);
        if (frame is null) return FenixNdLayer.Failed(MapPointsName, "the ND's view is not known");
        if (mapPoints.Points is not { } points) return FenixNdLayer.Failed(MapPointsName, mapPoints.Problem ?? "no data");

        if (FenixChannelNdMapData.Check(points, nd, frame, mapPoints.ReceivedAtMs, nowMs) is { } disagrees)
        {
            return FenixNdLayer.Failed(MapPointsName, disagrees);
        }

        // A route that cannot be read, for any reason, is no route: every point is listed. The route's own problem is
        // the route layer's to report; this layer neither reports nor shows it.
        FenixRoute? route = null;
        if (s.Push is not null)
        {
            FenixChannelSection<FenixRoute> read = FenixChannelRoute.Decode(s.Push);
            if (read.IsOk) route = read.Value;
        }

        FenixNdLayer rows = FenixChannelNdMapData.Rows(points, frame, route);

        // The poller's latest failure (the reply above is the last good one) is for the log.
        return mapPoints.Problem is { } latest ? rows with { LayerProblem = $"{MapPointsName}: {latest}" } : rows;
    }

    /// <summary>Every element of the four route lists the ND draws: active, temporary, alternate, temporary alternate.</summary>
    internal static IEnumerable<FenixRouteLeg> RouteLegs(FenixRoute route)
        => route.Legs.Concat(route.TemporaryLegs).Concat(route.AlternateLegs).Concat(route.TemporaryAlternateLegs);

    /// <summary>
    /// The ident of the waypoint symbol (a kind 4 element) within 0.1 NM of the PLAN center, for "of VAKSU"; null when
    /// there is none ("of the center"). Only the symbol elements name what is drawn: a line element's ident can be
    /// stale (the first element kept "T-P" and "SMTTH" in flight).
    /// </summary>
    internal static string? PlanCenterIdent(FenixNdFrame frame, FenixRoute? route)
    {
        if (route is null) return null;

        return RouteLegs(route)
            .Where(leg => leg.Kind == FenixChannelFields.RouteLegKind.Symbol && leg.Ident.Length > 0)
            .Select(leg => (leg.Ident, Away: Navigation.NavigationCalculator.CalculateDistance(
                frame.Center.Latitude, frame.Center.Longitude, leg.Point.Latitude, leg.Point.Longitude)))
            .Where(entry => entry.Away <= 0.1)
            .OrderBy(entry => entry.Away)
            .Select(entry => entry.Ident)
            .FirstOrDefault();
    }

    /// <summary>
    /// The rows for <paramref name="s"/> with <paramref name="layers"/> between the header and the messages, in screen
    /// order. The ND is read with <see cref="FenixChannelNd.ReadChecked"/>, the read Ctrl+W and the announcer share.
    /// </summary>
    internal static FenixChannelRows Compose(
        FenixChannelSnapshot s, Action<string> reportProblem,
        IReadOnlyList<Func<FenixChannelSnapshot, FenixNdData, FenixNdLayer>> layers)
    {
        FenixChannelSection<FenixNdData> section = FenixChannelNd.ReadChecked(s);
        if (!section.IsOk) return Stop(section.Problem ?? "no data");

        FenixNdData nd = section.Value!;
        var rows = new List<string>();

        // (1) and (2): top left.
        if (FenixChannelNdSpeech.Line(nd.GroundSpeedLine, FenixChannelNdSpeech.GroundWind) is { } groundSpeed)
        {
            rows.Add("Top left: " + groundSpeed);
        }

        if (FenixChannelNdSpeech.Line(nd.WindLine, FenixChannelNdSpeech.GroundWind) is { } wind) rows.Add("Top left: wind " + wind);

        // The wind arrow under the wind line: heading-up modes only (PLAN is north up; unmeasured there).
        if (nd.Mode != "PLAN" && WindArrowRow(nd) is { } arrow) rows.Add(arrow);

        // (3): top center. The approach label is drawn at the very top, above the compass, in every mode. PLAN is
        // north up with no heading under a lubber line.
        if (FenixChannelNdSpeech.Line(nd.ApproachMessage, FenixChannelNdSpeech.ApproachLabel) is { } approach)
        {
            rows.Add("Top center: " + approach);
        }
        if (nd.Mode != "PLAN") rows.Add(CompassRow(nd));

        // (4): top right. In LS and VOR the glass draws the LS or VOR block there instead.
        if (nd.Mode is "ARC" or "NAV" or "PLAN" && nd.Waypoint is { } waypoint && FenixChannelNdWaypoint.IsShown(waypoint))
        {
            string?[] lines = [waypoint.Line1, waypoint.Line2, waypoint.Line3];
            string[] spoken = [.. lines.Select(line => FenixChannelNdSpeech.Line(line, FenixChannelNdSpeech.Waypoint)).OfType<string>()];
            if (spoken.Length > 0) rows.Add("Top right: " + string.Join("; ", spoken));
        }

        // (5): the layers, each on its own.
        foreach (Func<FenixChannelSnapshot, FenixNdData, FenixNdLayer> layer in layers)
        {
            FenixNdLayer result = layer(s, nd);
            if (result.NdProblem is { } stops) return Stop(stops);

            if (result.LayerProblem is { } problem) reportProblem(problem);
            rows.AddRange(result.Rows);
        }

        // (6): the messages.
        rows.AddRange(MessageRows(nd));
        return FenixChannelRows.Of(rows);

        FenixChannelRows Stop(string problem)
        {
            reportProblem(problem);
            return FenixChannelRows.NotAvailable(problem);
        }
    }

    /// <summary>
    /// A heading or track on the compass: whole degrees, half away from zero, three digits, north as 360 ("318",
    /// "359" for -1.44, "360" for 359.6 and for 0.2, "005"); "not available" for null.
    /// </summary>
    public static string CompassText(double? degrees)
    {
        if (degrees is not { } value) return "not available";

        double whole = Math.Round(value, MidpointRounding.AwayFromZero) % 360;
        if (whole < 0) whole += 360;
        if (whole == 0) whole = 360;
        return ((int)whole).ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// How an item of the ND is worded: its measured color and symbol ("yellow lubber line"), the symbol alone for an
    /// item the ND draws in no fixed color, and the item's own name with no color word for an item no capture measured.
    /// </summary>
    internal static string Words(string item) => FenixChannelNdLook.For(item) switch
    {
        null => item,
        { Color: null } entry => entry.Symbol,
        { ColorTellsApart: false } entry => entry.Symbol,
        { } entry => $"{entry.Color} {entry.Symbol}"
    };

    // The green arrow under the wind line points along the wind line's figure (163.2 as drawn: the first number in
    // its text) plus 180 minus the magnetic heading, so relative to the nose. No row without a figure (the dashes) or a
    // heading. Evidence: sitting 2, the header in flight.
    private static string? WindArrowRow(FenixNdData nd)
    {
        if (nd.WindLine is null || nd.MagneticHeading is not { } heading) return null;

        Match figure = FirstNumber().Match(FenixChannelText.Parse(nd.WindLine).JoinedText);
        if (!figure.Success) return null;

        double direction = double.Parse(figure.Value, NumberStyles.None, CultureInfo.InvariantCulture);
        double relative = (direction + 180 - heading) % 360;
        if (relative > 180) relative -= 360;
        if (relative < -180) relative += 360;

        double whole = Math.Round(relative, MidpointRounding.AwayFromZero);
        string where = Math.Abs(whole) == 180 ? "back"
            : whole == 0 ? "ahead"
            : $"{Math.Abs(whole).ToString("0", CultureInfo.InvariantCulture)}° {(whole > 0 ? "right" : "left")}";
        return $"Top left: {Words("wind arrow")} pointing {where}";
    }

    [GeneratedRegex(@"[0-9]+")]
    private static partial Regex FirstNumber();

    private static string CompassRow(FenixNdData nd)
    {
        if (nd.MagneticHeading is not { } heading) return "Top center: heading not available";

        string row = $"Top center: heading {CompassText(heading)} under the {Words("lubber line")}";
        if (nd.MagneticTrack is not { } track) return row;

        double offset = (track - heading) % 360;
        if (offset > 180) offset -= 360;
        if (offset < -180) offset += 360;

        double whole = Math.Round(offset, MidpointRounding.AwayFromZero);
        if (whole == 0) return $"{row}, {Words("track diamond")} on the heading";

        string side = whole > 0 ? "right" : "left";
        return $"{row}, {Words("track diamond")} {Math.Abs(whole).ToString("0", CultureInfo.InvariantCulture)}° {side}";
    }

    // The center, windshear and TCAS texts first, then the glass's two boxes: "TA ONLY" in the upper one, the first
    // other entry in the lower one. Any further entry is not drawn, so it is not listed (evidence Part A, "The ND's
    // sides").
    private static IEnumerable<string> MessageRows(FenixNdData nd)
    {
        foreach ((string message, IReadOnlySet<FenixTextColor>? usual) in DrawnMessages(nd))
        {
            if (FenixChannelNdSpeech.Line(message, usual) is { } spoken) yield return "Message: " + spoken;
        }
    }

    /// <summary>
    /// The messages the glass draws, as sent, in the order the window lists them: the center, windshear and TCAS texts,
    /// then the upper box ("TA ONLY") and the lower box (the first other 103 entry), each with the usual colors of its
    /// own field for <see cref="FenixChannelNdSpeech"/>: 62 and 103 white; 64 and 66 none measured (null, every color
    /// spoken). The one selection the window's rows and the ND messages announcer both read, so they cannot drift apart.
    /// </summary>
    internal static IEnumerable<(string Coded, IReadOnlySet<FenixTextColor>? Usual)> DrawnMessages(FenixNdData nd)
    {
        foreach (FenixNdCenterMessage message in nd.CenterMessages)
        {
            yield return (message.Coded, message.Field == FenixChannelFields.Remote.NdCenterMessageCapt
                ? FenixChannelNdSpeech.CenterMessage
                : null);
        }

        foreach (string box in ListedBoxes(nd)) yield return (box, FenixChannelNdSpeech.ListedMessage);
    }

    private static IEnumerable<string> ListedBoxes(FenixNdData nd)
    {
        string? upper = nd.ListedMessages.FirstOrDefault(entry => FenixChannelText.TrimmedText(entry) == TaOnly);
        string? lower = nd.ListedMessages.FirstOrDefault(entry => FenixChannelText.TrimmedText(entry) != TaOnly);
        foreach (string? box in new[] { upper, lower })
        {
            if (box is not null) yield return box;
        }
    }
}
