using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What one layer of the ND gives <see cref="FenixChannelNdRows.Compose(FenixChannelSnapshot, Action{string})"/>: its
/// rows, a problem of its own that costs only its rows, or a problem that stops the whole ND (a letter the app does not
/// know, [FNX-24]).
/// </summary>
internal sealed record FenixNdLayer(IReadOnlyList<string> Rows, string? LayerProblem, string? NdProblem)
{
    /// <summary>A layer that was read: its rows.</summary>
    public static FenixNdLayer Of(IReadOnlyList<string> rows) => new(rows, null, null);

    /// <summary>A layer that could not be read: one row in its place, and the problem for the log.</summary>
    public static FenixNdLayer Failed(string name, string problem)
        => new([$"{name}: not available, {problem}"], $"{name}: {problem}", null);

    /// <summary>A problem that stops the whole ND, the other layers and the header with it.</summary>
    public static FenixNdLayer StopsNd(string problem) => new([], null, problem);
}

/// <summary>
/// The rows of the Fenix ND window: one snapshot of the display channel turned into what the captain's ND draws, in
/// screen order, each row starting with its place. The header's rows are built here; the route, map, radio and traffic
/// layers are functions of the snapshot and the header, run in screen order. A row the app builds from values speaks
/// a color only from <see cref="FenixChannelNdLook"/>; channel text speaks its own through
/// <see cref="FenixChannelSpeech"/>. Numbers are invariant whatever the PC's culture is.
/// </summary>
internal static class FenixChannelNdRows
{
    /// <summary>What <see cref="FenixChannelMonitor.ReportFeatureProblem"/> logs the window under.</summary>
    public const string Feature = "ND";

    private const string TaOnly = "TA ONLY";

    /// <summary>The rows for <paramref name="s"/>: today the header and the messages, no layer yet.</summary>
    public static FenixChannelRows Compose(FenixChannelSnapshot s, Action<string> reportProblem)
        => Compose(s, reportProblem, []);

    /// <summary>
    /// The rows for <paramref name="s"/> with the "Map points" layer after the header. <paramref name="mapPoints"/> is the
    /// poller's latest reply, when it was last confirmed current (on the clock <paramref name="nowMs"/> reads) and its
    /// problem; the layer uses the points only under <see cref="FenixChannelNdMapData.Check"/> ([FNX-22]).
    /// </summary>
    internal static FenixChannelRows Compose(
        FenixChannelSnapshot s, Action<string> reportProblem,
        (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) mapPoints, long nowMs)
        => Compose(s, reportProblem, [(snapshot, nd) => MapPointsLayer(snapshot, nd, mapPoints, nowMs)]);

    private const string MapPointsName = "Map points";

    // The ND draws map points in ROSE NAV, ARC and PLAN only: in ROSE LS and ROSE VOR the layer gives no rows at all.
    private static FenixNdLayer MapPointsLayer(
        FenixChannelSnapshot s, FenixNdData nd, (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) mapPoints,
        long nowMs)
    {
        if (nd.Mode is not ("NAV" or "ARC" or "PLAN")) return FenixNdLayer.Of([]);

        FenixNdFrame? frame = FenixNdFrame.From(nd);
        if (frame is null) return FenixNdLayer.Failed(MapPointsName, "the ND's view is not known");
        if (mapPoints.Points is not { } points) return FenixNdLayer.Failed(MapPointsName, mapPoints.Problem ?? "no data");

        if (FenixChannelNdMapData.Check(points, nd, frame, mapPoints.ReceivedAtMs, nowMs) is { } disagrees)
        {
            return FenixNdLayer.Failed(MapPointsName, disagrees);
        }

        // No flight plan is no route: every point is listed. A route in the wrong shape is the layer's problem.
        FenixRoute? route = null;
        FenixChannelSection<FenixRoute> read = s.Push is null
            ? FenixChannelSection<FenixRoute>.Missing("data")
            : FenixChannelRoute.Decode(s.Push);
        if (read.IsOk) route = read.Value;
        else if (read.Problem is { } problem && !problem.StartsWith("no ", StringComparison.Ordinal))
        {
            return FenixNdLayer.Failed(MapPointsName, problem);
        }

        FenixNdLayer rows = FenixChannelNdMapData.Rows(points, frame, route);

        // The poller's latest failure (the reply above is the last good one) is for the log.
        return mapPoints.Problem is { } latest ? rows with { LayerProblem = $"{MapPointsName}: {latest}" } : rows;
    }

    /// <summary>Every element of the four route lists the ND draws: active, temporary, alternate, temporary alternate.</summary>
    internal static IEnumerable<FenixRouteLeg> RouteLegs(FenixRoute route)
        => route.Legs.Concat(route.TemporaryLegs).Concat(route.AlternateLegs).Concat(route.TemporaryAlternateLegs);

    /// <summary>
    /// The ident of the route element within 0.1 NM of the PLAN center, for "of VAKSU"; null when there is none ("of the
    /// center").
    /// </summary>
    internal static string? PlanCenterIdent(FenixNdFrame frame, FenixRoute? route)
    {
        if (route is null) return null;

        return RouteLegs(route)
            .Where(leg => leg.Ident.Length > 0)
            .Select(leg => (leg.Ident, Away: Navigation.NavigationCalculator.CalculateDistance(
                frame.Center.Latitude, frame.Center.Longitude, leg.Point.Latitude, leg.Point.Longitude)))
            .Where(entry => entry.Away <= 0.1)
            .OrderBy(entry => entry.Away)
            .Select(entry => entry.Ident)
            .FirstOrDefault();
    }

    /// <summary>
    /// The rows for <paramref name="s"/> with <paramref name="layers"/> between the header and the messages. The seam
    /// later tasks fill: the list is in screen order.
    /// </summary>
    internal static FenixChannelRows Compose(
        FenixChannelSnapshot s, Action<string> reportProblem,
        IReadOnlyList<Func<FenixChannelSnapshot, FenixNdData, FenixNdLayer>> layers)
    {
        FenixChannelSection<FenixNdData> section = FenixChannelNd.Read(s);
        if (!section.IsOk) return Stop(section.Problem ?? "no data");

        FenixNdData nd = section.Value!;
        var rows = new List<string>();

        // (1) and (2): top left.
        if (FenixChannelSpeech.Line(nd.GroundSpeedLine) is { } groundSpeed) rows.Add("Top left: " + groundSpeed);
        if (FenixChannelSpeech.Line(nd.WindLine) is { } wind) rows.Add("Top left: wind " + wind);

        // (3): top center. PLAN is north up with no heading under a lubber line.
        if (nd.Mode != "PLAN") rows.Add(CompassRow(nd));

        // (4): top right. In LS and VOR the glass draws the LS or VOR block there instead.
        if (nd.Mode is "ARC" or "NAV" or "PLAN" && nd.Waypoint is { } waypoint && FenixChannelNdWaypoint.IsShown(waypoint))
        {
            string?[] lines = [waypoint.Line1, waypoint.Line2, waypoint.Line3];
            string[] spoken = [.. lines.Select(line => FenixChannelSpeech.Line(line)).OfType<string>()];
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
        { } entry => $"{entry.Color} {entry.Symbol}"
    };

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
        foreach (string message in nd.CenterMessages)
        {
            if (FenixChannelSpeech.Line(message) is { } spoken) yield return "Message: " + spoken;
        }

        string? upper = nd.ListedMessages.FirstOrDefault(entry => PlainText(entry) == TaOnly);
        string? lower = nd.ListedMessages.FirstOrDefault(entry => PlainText(entry) != TaOnly);
        foreach (string? box in new[] { upper, lower })
        {
            if (box is not null && FenixChannelSpeech.Line(box) is { } spoken) yield return "Message: " + spoken;
        }
    }

    private static string PlainText(string coded) => FenixChannelText.Parse(coded).JoinedText.Trim();
}
