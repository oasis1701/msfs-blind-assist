using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>The three lines of the ND's next-waypoint box, as sent (channel text); "" for a line left out.</summary>
internal sealed record FenixNdWaypointLines(string Line1, string Line2, string Line3);

/// <summary>
/// What the captain's ND header holds: the EFIS mode, range and options, the ground speed and wind texts, the
/// angles, the positions, the next-waypoint lines and the messages. Lines are as sent, with their format letters.
/// </summary>
/// <param name="Mode">"LS", "VOR", "NAV", "ARC" or "PLAN" (another value its number); null only when not shown.</param>
/// <param name="Range">The range as invariant text; "40" when the field was left out.</param>
/// <param name="MapOption">"CSTR", "WPT", "VOR.D", "NDB", "ARPT" (another value its number); null for none.</param>
/// <param name="LsOn">The captain's LS pushbutton is on.</param>
/// <param name="GroundSpeedLine">The ground speed text (163.1); null when absent.</param>
/// <param name="WindLine">The wind text (163.2); null when absent.</param>
/// <param name="MagneticHeading">194.1; null when absent or not finite.</param>
/// <param name="MagneticTrack">194.2; null when absent or not finite.</param>
/// <param name="MagneticVariation">194.3; null when absent or not finite.</param>
/// <param name="SelectedHeading">113.2, the heading bug; null when absent or not finite.</param>
/// <param name="Position">The present position (192); null when absent.</param>
/// <param name="PlanCenter">The PLAN centre (84); null when absent.</param>
/// <param name="Waypoint">The next-waypoint lines (68); null when absent.</param>
/// <param name="Messages">The center, windshear and TCAS messages when not blank, then the ND messages that are not null or blank.</param>
internal sealed record FenixNdData(
    string? Mode, string Range, string? MapOption, bool LsOn, string? GroundSpeedLine, string? WindLine,
    double? MagneticHeading, double? MagneticTrack, double? MagneticVariation, double? SelectedHeading,
    FenixLatLon? Position, FenixLatLon? PlanCenter, FenixNdWaypointLines? Waypoint, IReadOnlyList<string> Messages);

/// <summary>
/// Reads the captain's ND header from one snapshot. Never the first officer's side. The first problem found is the
/// one given: a field in the wrong shape, then missing content, then a format letter the app does not know ([FNX-24]).
/// </summary>
internal static class FenixChannelNd
{
    /// <summary>The mode when the field was left out: ROSE NAV, the value protobuf-net leaves out as its default.</summary>
    public static readonly string? ModeWhenAbsent = "NAV";

    private const string RangeWhenAbsent = "40";

    /// <summary>The EFIS panel's word for a mode number; another number is its own text; an absent mode is <see cref="ModeWhenAbsent"/>.</summary>
    public static string? ModeName(ulong? value) => value switch
    {
        null => ModeWhenAbsent,
        EfisModeValue.Ls => "LS",
        EfisModeValue.Vor => "VOR",
        EfisModeValue.Nav => "NAV",
        EfisModeValue.Arc => "ARC",
        EfisModeValue.Plan => "PLAN",
        _ => value.Value.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>The range figure as invariant text; "40" when absent.</summary>
    public static string RangeName(ulong? value)
        => value is null ? RangeWhenAbsent : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string? MapOptionName(ulong? value) => value switch
    {
        null => null,
        EfisMapOptionValue.Cstr => "CSTR",
        EfisMapOptionValue.Wpt => "WPT",
        EfisMapOptionValue.VorD => "VOR.D",
        EfisMapOptionValue.Ndb => "NDB",
        EfisMapOptionValue.Arpt => "ARPT",
        _ => value.Value.ToString(CultureInfo.InvariantCulture)
    };

    public static FenixChannelSection<FenixNdData> Read(FenixChannelSnapshot s)
    {
        if (s.Push is null) return FenixChannelSection<FenixNdData>.Missing("data");

        FenixChannelPush root = s.Push;
        string? wrong = null;
        void Wrong(string path) => wrong ??= path;

        // ---- Shapes, in field order.
        FenixChannelPush? efis = null;
        IReadOnlyList<FenixChannelField> panels = root.All(Remote.Efis);
        if (panels.Count == 2)
        {
            efis = panels[0].AsMessage();
            if (efis is null) Wrong("1");
        }
        else if (panels.Count != 0)
        {
            Wrong("1");
        }

        ulong? mode = Varint(efis, Efis.Mode, "1.1");
        ulong? mapOption = Varint(efis, Efis.MapOption, "1.2");
        ulong? range = Varint(efis, Efis.ZoomSelection, "1.3");
        ulong? ls = Varint(efis, Efis.Ls, "1.6");

        string? center = Text(root, Remote.NdCenterMessageCapt, "62");
        string? windshear = Text(root, Remote.NdWindshearMessageCapt, "64");
        string? tcas = Text(root, Remote.NdTcasMessageCapt, "66");

        FenixChannelPush? nav = Part(root, Remote.NavInfoCapt, "68");
        string? line1 = Text(nav, NdInfoData.Line1, "68.1");
        string? line2 = Text(nav, NdInfoData.Line2, "68.2");
        string? line3 = Text(nav, NdInfoData.Line3, "68.3");

        FenixChannelPush? planPart = Part(root, Remote.CduCenterCapt, "84");
        FenixLatLon? planCenter = Point(planPart, "84");

        List<string> listed = [];
        foreach (FenixChannelField entry in root.All(Remote.NdMessagesCapt))
        {
            FenixChannelPush? wrapped = entry.AsMessage();
            if (wrapped is null)
            {
                Wrong("103");
                continue;
            }

            string? message = Text(wrapped, NullWrapped.Value, "103.1");
            if (message is not null) listed.Add(message);
        }

        FenixChannelPush? fcu = Part(root, Remote.FcuData, "113");
        double? selected = Finite(Double(fcu, FcuData.SelectedHeading, "113.2"));

        FenixChannelPush? wind = Part(root, Remote.GroundWindCapt, "163");
        string? groundSpeed = Text(wind, GroundWindData.GroundSpeed, "163.1");
        string? windLine = Text(wind, GroundWindData.Wind, "163.2");

        FenixChannelPush? positionPart = Part(root, Remote.TruePos, "192");
        FenixLatLon? position = Point(positionPart, "192");

        FenixChannelPush? heading = Part(root, Remote.Heading, "194");
        double? magHeading = Finite(Double(heading, HeadingData.MagHeading, "194.1"));
        double? magTrack = Finite(Double(heading, HeadingData.MagneticTrack, "194.2"));
        double? magVar = Finite(Double(heading, HeadingData.MagVar, "194.3"));

        if (wrong is not null) return FenixChannelSection<FenixNdData>.WrongShape(wrong);

        // ---- Content.
        if (efis is null) return FenixChannelSection<FenixNdData>.Missing("EFIS data");
        if (wind is null) return FenixChannelSection<FenixNdData>.Missing("ground speed and wind");

        // ---- Format letters, in the order the lines are read.
        (string? Text, string Place)[] texts =
        [
            (groundSpeed, "ground speed line"), (windLine, "wind line"),
            (line1, "next waypoint lines"), (line2, "next waypoint lines"), (line3, "next waypoint lines"),
            (center, "center message"), (windshear, "windshear message"), (tcas, "TCAS message")
        ];
        foreach ((string? text, string place) in texts)
        {
            if (Unknown(text) is char letter) return FenixChannelSection<FenixNdData>.UnknownCode(letter, place);
        }

        foreach (string message in listed)
        {
            if (Unknown(message) is char letter) return FenixChannelSection<FenixNdData>.UnknownCode(letter, "ND messages");
        }

        List<string> messages = [];
        foreach (string? message in new[] { center, windshear, tcas })
        {
            if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
        }

        messages.AddRange(listed.Where(message => !string.IsNullOrWhiteSpace(message)));

        return FenixChannelSection<FenixNdData>.Ok(new FenixNdData(
            ModeName(mode), RangeName(range), MapOptionName(mapOption), ls is not null && ls != 0,
            groundSpeed, windLine, magHeading, magTrack, magVar, selected, position, planCenter,
            nav is null ? null : new FenixNdWaypointLines(line1 ?? "", line2 ?? "", line3 ?? ""), messages));

        // ---- The reads. A field in another wire shape records its path (the first only) and reads as absent.

        FenixChannelPush? Part(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            FenixChannelPush? message = field.AsMessage();
            if (message is null) Wrong(path);
            return message;
        }

        ulong? Varint(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Varint)
            {
                Wrong(path);
                return null;
            }

            return field.Varint;
        }

        double? Double(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Fixed64)
            {
                Wrong(path);
                return null;
            }

            return field.AsDouble();
        }

        string? Text(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            string? text = field.AsString();
            if (text is null) Wrong(path);
            return text;
        }

        // A latitude and longitude: members 1 and 2 only; a member left out is protobuf-net's 0.
        FenixLatLon? Point(FenixChannelPush? part, string path)
        {
            if (part is null) return null;

            double? latitude = Double(part, LatLon.Latitude, $"{path}.1");
            double? longitude = Double(part, LatLon.Longitude, $"{path}.2");
            return new FenixLatLon(latitude ?? 0, longitude ?? 0);
        }
    }

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private static char? Unknown(string? text)
    {
        FenixTextLine line = FenixChannelText.Parse(text);
        return line.UnknownCodes.Count > 0 ? line.UnknownCodes[0] : null;
    }
}
