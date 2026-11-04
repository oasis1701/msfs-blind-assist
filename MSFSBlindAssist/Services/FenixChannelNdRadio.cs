using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The ND's radio content: ROSE LS's and ROSE VOR's top right block, ROSE LS's course pointer and deviations, the
/// VOR 1 / VOR 2 / ADF 1 / ADF 2 bearing pointers, and the two bottom corners with their flags. Two layers, because
/// the glass draws them in two places: <see cref="Rows"/> (the top right, the center, the right side and the pointers,
/// first in the ND window) and <see cref="CornerRows"/> (the bottom corners, after the map). Measured on 3.0.0.7613
/// (docs/fenix-channel/evidence/03-nd.md, sitting 1 Part D and sitting 2, "the radio content"). Only what the glass
/// draws is listed; every number is invariant-culture; a color is spoken from the channel's letters or a
/// <see cref="FenixChannelNdLook"/> entry.
/// </summary>
internal static class FenixChannelNdRadio
{
    /// <summary>The name of the top right, center, right side and pointer layer.</summary>
    public const string LayerName = "Radio";

    /// <summary>The name of the bottom corners' layer.</summary>
    public const string CornersLayerName = "Radio corners";

    // The localizer bar stops at 2.5 dots, the glideslope half diamond starts at 2.0 (sitting 2).
    private const double LocalizerFullScaleDots = 2.5;
    private const double GlideslopeFullScaleDots = 2.0;

    /// <summary>The look items of the radio layers, each a <see cref="FenixChannelNdLook"/> entry.</summary>
    internal static class LookItem
    {
        public const string CoursePointer = "course pointer";
        public const string LocalizerBar = "localizer bar";
        public const string GlideslopeDiamond = "glideslope diamond";
        public const string GlideslopeHalfDiamond = "glideslope half diamond";
        public const string VorPointerNav1 = "VOR pointer on NAV 1";
        public const string AdfPointerNav2 = "ADF pointer on NAV 2";
        public const string VorArrowNav1 = "VOR corner arrow on NAV 1";
        public const string AdfArrowNav2 = "ADF corner arrow on NAV 2";
        public const string FailedVorIdent = "failed VOR block ident";
        public const string FailedVorFrequency = "failed VOR block frequency";
        public const string FailedCourseLabel = "failed VOR block course label";
        public const string FailedCourseFigure = "failed VOR block course figure";
        public const string FailedVorFlag = "failed VOR center flag";
    }

    // ---- The top right, the center, the right side and the pointers.

    /// <summary>
    /// Layer "Radio": in ROSE LS and ROSE VOR the top right block (72's lines, 70's lines, or the failed VOR block when
    /// 102.1.1 is set), in ROSE LS the course pointer (82.6), the localizer bar (82.1) and the glideslope diamond (82.2),
    /// in ROSE VOR the failed flag at the center; then, in ROSE NAV and ROSE LS, a row for each bearing pointer (78) its
    /// selector allows. ARC, ROSE VOR and PLAN pointers and ROSE VOR's course, deviation and TO/FROM are not measured, so
    /// they are not listed. A field in another shape gives one row in the layer's place; a letter the app does not know
    /// stops the ND ([FNX-24]).
    /// </summary>
    public static FenixNdLayer Rows(FenixChannelPush push, FenixNdData header)
    {
        bool ls = header.Mode == "LS", vor = header.Mode == "VOR", pointers = header.Mode is "NAV" or "LS";
        if (!ls && !vor && !pointers) return FenixNdLayer.Of([]);

        var read = new Reader();
        var coded = new List<(string? Text, string Place)>();

        // The top right block, or the flag that replaces it.
        string?[] block = [null, null, null];
        string? vor1Flag = null;
        if (vor)
        {
            FenixChannelPush? flagLabels = read.Part(push, Remote.NdFlagLabelsCapt, "102");
            FenixChannelPush? corners = read.Part(flagLabels, NdFlagLabels.CornerFlags, "102.1");
            vor1Flag = read.Text(corners, NdCornerFlags.Vor1, "102.1.1");
            coded.Add((vor1Flag, "VOR 1 flag"));
        }

        if (ls || (vor && IsBlank(vor1Flag)))
        {
            int field = ls ? Remote.LsBlockCapt : Remote.VorBlockCapt;
            FenixChannelPush? part = read.Part(push, field, $"{field}");
            block = [read.Text(part, NdRadioBlock.Line1, $"{field}.1"), read.Text(part, NdRadioBlock.Line2, $"{field}.2"),
                read.Text(part, NdRadioBlock.Line3, $"{field}.3")];
            foreach (string? line in block) coded.Add((line, ls ? "ILS block" : "VOR block"));
        }

        // The ILS scales: 82 absent is no ILS (every member NaN); a member left out of a present 82 is protobuf's 0.
        double course = double.NaN, localizer = double.NaN, glideslope = double.NaN;
        if (ls && push.TryGet(Remote.LsDataCapt, out _))
        {
            FenixChannelPush? ils = read.Part(push, Remote.LsDataCapt, "82");
            course = read.Double(ils, LsInputData.Course, "82.6") ?? double.NaN;
            localizer = read.Double(ils, LsInputData.PfdHorDots, "82.1") ?? 0;
            glideslope = read.Double(ils, LsInputData.PfdVertDots, "82.2") ?? 0;
        }

        FenixChannelPush? bearings = pointers ? read.Part(push, Remote.BearingPointersCapt, "78") : null;
        double[] bearing =
        [
            read.Double(bearings, NdBearingPointers.Nav1, "78.1") ?? double.NaN,
            read.Double(bearings, NdBearingPointers.Nav2, "78.2") ?? double.NaN
        ];
        IReadOnlyList<ulong> selectors = pointers ? read.Selectors(push) : [];

        if (read.Wrong is { } wrong) return FenixNdLayer.Failed(LayerName, $"field {wrong} has the wrong shape");
        if (Unknown(coded) is { } unknown) return unknown;

        var rows = new List<string>();
        if (vor && !IsBlank(vor1Flag))
        {
            rows.Add($"Top right: {Spoken(LookItem.FailedVorIdent)}, {Spoken(LookItem.FailedVorFrequency)}; "
                + $"{Spoken(LookItem.FailedCourseLabel)}, {Spoken(LookItem.FailedCourseFigure)}");
            rows.Add($"Center: {Spoken(LookItem.FailedVorFlag)}");
        }
        else if (JoinedLines(block) is { } topRight)
        {
            rows.Add("Top right: " + topRight);
        }

        if (ls)
        {
            if (double.IsFinite(course))
            {
                rows.Add($"Center: {FenixChannelNdRows.Words(LookItem.CoursePointer)} at "
                    + $"{FenixChannelNdRows.CompassText(course)} on the compass");
            }

            rows.Add(LocalizerRow(localizer));
            rows.Add(GlideslopeRow(glideslope));
        }

        if (pointers)
        {
            for (int side = 0; side < 2; side++)
            {
                if (PointerRow(side, bearing[side], SelectorOf(selectors, side)) is { } row) rows.Add(row);
            }
        }

        return FenixNdLayer.Of(rows);
    }

    private static string LocalizerRow(double dots)
    {
        string bar = FenixChannelNdRows.Words(LookItem.LocalizerBar);
        if (double.IsNaN(dots)) return "Center: localizer scale, no bar";

        string side = dots > 0 ? "right" : "left";
        if (Math.Abs(dots) >= LocalizerFullScaleDots) return $"Center: {bar}, full scale {side}";

        double rounded = Math.Round(Math.Abs(dots), 1, MidpointRounding.AwayFromZero);
        return rounded == 0
            ? $"Center: {bar} centered"
            : $"Center: {bar} {rounded.ToString("0.0", CultureInfo.InvariantCulture)} dots {side}";
    }

    private static string GlideslopeRow(double dots)
    {
        if (double.IsNaN(dots)) return "Right side: glideslope scale, no diamond";

        string side = dots > 0 ? "above" : "below";
        if (Math.Abs(dots) >= GlideslopeFullScaleDots)
        {
            return $"Right side: {FenixChannelNdRows.Words(LookItem.GlideslopeHalfDiamond)}, full scale {side}";
        }

        double rounded = Math.Round(Math.Abs(dots), 1, MidpointRounding.AwayFromZero);
        string diamond = FenixChannelNdRows.Words(LookItem.GlideslopeDiamond);
        return rounded == 0
            ? $"Right side: {diamond} centered"
            : $"Right side: {diamond} {rounded.ToString("0.0", CultureInfo.InvariantCulture)} dots {side}";
    }

    // A pointer is listed only with a bearing and a selector at VOR or ADF (80 and 81 stay set with it at OFF; 78 does not).
    private static string? PointerRow(int side, double bearing, ulong selector)
    {
        if (!double.IsFinite(bearing)) return null;

        string? kind = KindOf(selector);
        if (kind is null) return null;

        string? item = (side, selector) switch
        {
            (0, EfisSelectorValue.Vor) => LookItem.VorPointerNav1,
            (1, EfisSelectorValue.Adf) => LookItem.AdfPointerNav2,
            _ => null
        };
        string look = item is null ? "" : $", {FenixChannelNdRows.Words(item)}";
        return $"Map: {kind} {side + 1} pointer{look}, at {FenixChannelNdRows.CompassText(bearing)} on the compass";
    }

    // ---- The bottom corners.

    /// <summary>
    /// Layer "Radio corners": the bottom left (NAV 1, 76.1) and the bottom right (NAV 2, 76.2), in every mode while
    /// sent: their lines, then the arrow. An empty entry (its selector at OFF) gives no row. A flag in 102.1 replaces its
    /// side's corner, spoken as sent, with no arrow: members 1 (VOR 1) and 3 (ADF 1) the left, 2 and 4 the right. The
    /// arrow's look is measured for VOR on NAV 1 and ADF on NAV 2 only; otherwise the row says "arrow" and no look.
    /// </summary>
    public static FenixNdLayer CornerRows(FenixChannelPush push, FenixNdData header)
    {
        var read = new Reader();
        var coded = new List<(string? Text, string Place)>();

        FenixChannelPush? corners = read.Part(push, Remote.CornersCapt, "76");
        string?[][] lines = new string?[2][];
        int[] numbers = [NdCorners.Nav1, NdCorners.Nav2];
        for (int side = 0; side < 2; side++)
        {
            FenixChannelPush? part = read.Part(corners, numbers[side], $"76.{side + 1}");
            lines[side] =
            [
                read.Text(part, NdCornerText.Line1, $"76.{side + 1}.1"), read.Text(part, NdCornerText.Line2, $"76.{side + 1}.2"),
                read.Text(part, NdCornerText.Line3, $"76.{side + 1}.3")
            ];
            foreach (string? line in lines[side]) coded.Add((line, "corner text"));
        }

        FenixChannelPush? flagLabels = read.Part(push, Remote.NdFlagLabelsCapt, "102");
        FenixChannelPush? flagPart = read.Part(flagLabels, NdFlagLabels.CornerFlags, "102.1");
        string?[] flags =
        [
            read.Text(flagPart, NdCornerFlags.Vor1, "102.1.1"), read.Text(flagPart, NdCornerFlags.Vor2, "102.1.2"),
            read.Text(flagPart, NdCornerFlags.Adf1, "102.1.3"), read.Text(flagPart, NdCornerFlags.Adf2, "102.1.4")
        ];
        foreach (string? flag in flags) coded.Add((flag, "corner flag"));

        IReadOnlyList<ulong> selectors = read.Selectors(push);

        if (read.Wrong is { } wrong) return FenixNdLayer.Failed(CornersLayerName, $"field {wrong} has the wrong shape");
        if (Unknown(coded) is { } unknown) return unknown;

        var rows = new List<string>();
        string[] places = ["Bottom left", "Bottom right"];
        for (int side = 0; side < 2; side++)
        {
            // Members 1 and 3 are the left side's flags, 2 and 4 the right's, in member order.
            string[] flagged =
            [
                .. new[] { flags[side], flags[side + 2] }.Select(flag => FenixChannelSpeech.Line(flag)).OfType<string>()
            ];
            if (flagged.Length > 0)
            {
                rows.Add($"{places[side]}: {string.Join("; ", flagged)}");
                continue;
            }

            if (JoinedLines(lines[side]) is not { } text) continue;

            string? item = (side, SelectorOf(selectors, side)) switch
            {
                (0, EfisSelectorValue.Vor) => LookItem.VorArrowNav1,
                (1, EfisSelectorValue.Adf) => LookItem.AdfArrowNav2,
                _ => null
            };
            rows.Add($"{places[side]}: {text}; {(item is null ? "arrow" : FenixChannelNdRows.Words(item))}");
        }

        return FenixNdLayer.Of(rows);
    }

    // ---- Helpers.

    private static bool IsBlank(string? coded) => FenixChannelSpeech.Line(coded) is null;

    // The lines spoken through FenixChannelSpeech, an absent or empty one skipped, joined by "; ".
    private static string? JoinedLines(IEnumerable<string?> lines)
    {
        string[] spoken = [.. lines.Select(line => FenixChannelSpeech.Line(line)).OfType<string>()];
        return spoken.Length == 0 ? null : string.Join("; ", spoken);
    }

    // An item the app draws a fixed text for: "text color".
    private static string Spoken(string item)
    {
        FenixNdLookEntry look = FenixChannelNdLook.For(item)!;
        return look.Color is null ? look.Symbol : $"{look.Symbol} {look.Color}";
    }

    private static ulong SelectorOf(IReadOnlyList<ulong> selectors, int side)
        => side < selectors.Count ? selectors[side] : EfisSelectorValue.Off;

    private static string? KindOf(ulong selector) => selector switch
    {
        EfisSelectorValue.Vor => "VOR",
        EfisSelectorValue.Adf => "ADF",
        _ => null
    };

    private static FenixNdLayer? Unknown(IEnumerable<(string? Text, string Place)> texts)
    {
        foreach ((string? text, string place) in texts)
        {
            FenixTextLine line = FenixChannelText.Parse(text);
            if (line.UnknownCodes.Count > 0)
            {
                return FenixNdLayer.StopsNd($"unknown code '{line.UnknownCodes[0]}' in the {place}");
            }
        }

        return null;
    }

    // Reads parts and values, recording the path of the first field that arrived in another wire shape; such a field
    // reads as absent.
    internal sealed class Reader
    {
        public string? Wrong { get; private set; }

        public FenixChannelPush? Part(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            FenixChannelPush? message = field.AsMessage();
            if (message is null) Wrong ??= path;
            return message;
        }

        public string? Text(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            string? text = field.AsString();
            if (text is null) Wrong ??= path;
            return text;
        }

        public double? Double(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Fixed64)
            {
                Wrong ??= path;
                return null;
            }

            return field.AsDouble();
        }

        public float? Float(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Fixed32)
            {
                Wrong ??= path;
                return null;
            }

            return field.Fixed32;
        }

        public bool? Flag(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            if (field.Type != FenixWireType.Varint)
            {
                Wrong ??= path;
                return null;
            }

            return field.AsBool();
        }

        public void Fail(string path) => Wrong ??= path;

        // The captain's EFIS (the first entry) member 17: one varint per NAV side, unpacked or packed.
        public IReadOnlyList<ulong> Selectors(FenixChannelPush push)
        {
            IReadOnlyList<FenixChannelField> panels = push.All(Remote.Efis);
            if (panels.Count == 0 || panels[0].AsMessage() is not { } efis) return [];

            var values = new List<ulong>();
            foreach (FenixChannelField entry in efis.All(Efis.EfisSelectors))
            {
                if (entry.Type == FenixWireType.Varint)
                {
                    values.Add(entry.Varint);
                }
                else if (entry.Type == FenixWireType.LengthDelimited)
                {
                    var wire = new FenixChannelWire(entry.Bytes.Span);
                    while (!wire.IsAtEnd)
                    {
                        if (!wire.TryReadVarint(out ulong value))
                        {
                            Wrong ??= "1.17";
                            break;
                        }

                        values.Add(value);
                    }
                }
                else
                {
                    Wrong ??= "1.17";
                }
            }

            return values;
        }
    }
}
