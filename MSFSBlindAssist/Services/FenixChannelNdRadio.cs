using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The ND's radio content: ROSE LS's and ROSE VOR's top right block, ROSE LS's course pointer and deviations, the
/// VOR 1 / VOR 2 / ADF 1 / ADF 2 bearing pointers, and the two bottom corners with their flags. Two layers, because
/// the glass draws them in two places: <see cref="Rows"/> (the top right, the center, the right side and the pointers,
/// first in the ND window) and <see cref="CornerRows"/> (the bottom corners, after the map). Measured on 3.0.0.7613
/// (evidence03-nd.md, sitting 1 Part D and sitting 2, "the radio content"). Only what the glass
/// draws is listed; every number is invariant-culture; a color is spoken from the channel's letters or a
/// <see cref="FenixChannelNdLook"/> entry, and only where it tells something (<see cref="FenixChannelNdSpeech"/>).
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

    // ROSE VOR's bar stops at 2.5 dots too (sitting 3).
    private const double VorFullScaleDots = 2.5;

    /// <summary>The look items of the radio layers, each a <see cref="FenixChannelNdLook"/> entry.</summary>
    internal static class LookItem
    {
        public const string CoursePointer = "course pointer";
        public const string LocalizerBar = "localizer bar";
        public const string GlideslopeDiamond = "glideslope diamond";
        public const string GlideslopeHalfDiamond = "glideslope half diamond";
        public const string VorCoursePointer = "VOR course pointer";
        public const string VorDeviationBar = "VOR deviation bar";
        public const string PointerNav1 = "bearing pointer on NAV 1";
        public const string PointerNav2 = "bearing pointer on NAV 2";
        public const string ArrowNav1 = "corner arrow on NAV 1";
        public const string ArrowNav2 = "corner arrow on NAV 2";
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
    /// in ROSE VOR the failed flag at the center, or the course pointer (80.1); then, in ROSE NAV, ROSE LS and ROSE VOR, a
    /// row for each bearing pointer (78) its selector allows and its receiver's flag (102.1) does not hide. ARC and PLAN
    /// pointers are not measured or not drawn, so they are not listed; ROSE VOR's deviation bar (80.2) and TO/FROM (80.5)
    /// follow its course pointer on one row. The layer's channel text is read first (<see cref="ReadTopText"/>): a field
    /// of it in another shape gives one row in the layer's place, then a letter the app does not know stops the ND
    /// ([FNX-24]), before any other field is read; a later field in another shape gives the one row.
    /// </summary>
    public static FenixNdLayer Rows(FenixChannelPush push, FenixNdData header)
    {
        bool ls = header.Mode == "LS", vor = header.Mode == "VOR", pointers = header.Mode is "NAV" or "LS" or "VOR";
        if (!ls && !vor && !pointers) return FenixNdLayer.Of([]);

        var read = new Reader();
        TopText text = ReadTopText(read, push, header);
        if (read.Wrong is { } wrongText) return WrongShape(LayerName, wrongText);
        if (FirstUnknown(text.Coded) is { } unknown) return StopsNd(unknown);

        string?[] block = text.Block, flags = text.Flags;
        string? vor1Flag = flags[0];

        // The ILS scales: 82 absent is no ILS (every member NaN); a member left out of a present 82 is protobuf's 0.
        double course = double.NaN, localizer = double.NaN, glideslope = double.NaN;
        if (ls && push.TryGet(Remote.LsDataCapt, out _))
        {
            FenixChannelPush? ils = read.Part(push, Remote.LsDataCapt, "82");
            course = read.Double(ils, LsInputData.Course, "82.6") ?? double.NaN;
            localizer = read.Double(ils, LsInputData.PfdHorDots, "82.1") ?? 0;
            glideslope = read.Double(ils, LsInputData.PfdVertDots, "82.2") ?? 0;
        }

        // ROSE VOR's course pointer: 80.1 only, never 70.2's text (sitting 3: set with nothing received).
        double vorCourse = double.NaN, vorDeviation = double.NaN, vorToFrom = double.NaN;
        if (vor)
        {
            FenixChannelPush? courseBlock = read.Part(push, Remote.VorCourseCapt, "80");
            vorCourse = read.Double(courseBlock, NdVorCourse.Course, "80.1") ?? double.NaN;
            vorDeviation = read.Double(courseBlock, NdVorCourse.Deviation, "80.2") ?? double.NaN;
            vorToFrom = read.Double(courseBlock, NdVorCourse.ToFrom, "80.5") ?? double.NaN;
        }

        FenixChannelPush? bearings = pointers ? read.Part(push, Remote.BearingPointersCapt, "78") : null;
        double[] bearing =
        [
            read.Double(bearings, NdBearingPointers.Nav1, "78.1") ?? double.NaN,
            read.Double(bearings, NdBearingPointers.Nav2, "78.2") ?? double.NaN
        ];
        IReadOnlyList<ulong> selectors = pointers ? read.Selectors(push) : [];

        if (read.Wrong is { } wrong) return WrongShape(LayerName, wrong);

        var rows = new List<string>();
        if (vor && !IsBlank(vor1Flag))
        {
            rows.Add($"Top right: {Spoken(LookItem.FailedVorIdent)}, {Spoken(LookItem.FailedVorFrequency)}; "
                + $"{Spoken(LookItem.FailedCourseLabel)}, {Spoken(LookItem.FailedCourseFigure)}");
            rows.Add($"Center: {Spoken(LookItem.FailedVorFlag)}");
        }
        else if (JoinedLines(block, ls ? FenixChannelNdSpeech.LsBlock : FenixChannelNdSpeech.VorBlock) is { } topRight)
        {
            rows.Add("Top right: " + topRight);
        }

        if (vor && double.IsFinite(vorCourse))
        {
            rows.Add($"Center: {FenixChannelNdRows.Words(LookItem.VorCoursePointer)} at "
                + $"{FenixChannelNdRows.CompassText(vorCourse)} on the compass");
            rows.Add(VorDeviationRow(vorDeviation, vorToFrom));
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
                if (PointerRow(side, bearing[side], SelectorOf(selectors, side), flags) is { } row) rows.Add(row);
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

    // 80.2 is negative with the bar right of the course pointer, the opposite of the localizer's 82.1; 80.5 is 1 FROM, 2 TO.
    private static string VorDeviationRow(double dots, double toFrom)
    {
        if (double.IsNaN(dots)) return "Center: deviation scale, no bar";

        string bar = FenixChannelNdRows.Words(LookItem.VorDeviationBar);
        string side = dots < 0 ? "right" : "left";
        string part = toFrom == 1 ? ", FROM" : toFrom == 2 ? ", TO" : "";
        if (Math.Abs(dots) >= VorFullScaleDots) return $"Center: {bar}, full scale {side}{part}";

        double rounded = Math.Round(Math.Abs(dots), 1, MidpointRounding.AwayFromZero);
        return rounded == 0
            ? $"Center: {bar} centered{part}"
            : $"Center: {bar} {rounded.ToString("0.0", CultureInfo.InvariantCulture)} dots {side}{part}";
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

    // A pointer is listed only with a bearing, a selector at VOR or ADF (80 and 81 stay set with it at OFF; 78 does not)
    // and no flag on that receiver (102.1: VOR 1, VOR 2, ADF 1, ADF 2 in member order; sitting 3: a failed ADF 1 drew no
    // pointer while 78.1 held its bearing). The shape follows the side, whichever receiver.
    private static string? PointerRow(int side, double bearing, ulong selector, string?[] flags)
    {
        if (!double.IsFinite(bearing)) return null;

        string? kind = KindOf(selector);
        if (kind is null) return null;

        if (!IsBlank(flags[(selector == EfisSelectorValue.Vor ? 0 : 2) + side])) return null;

        string words = FenixChannelNdRows.Words(side == 0 ? LookItem.PointerNav1 : LookItem.PointerNav2);
        return $"Map: {kind} {side + 1} {words}, at {FenixChannelNdRows.CompassText(bearing)} on the compass";
    }

    // ---- The bottom corners.

    /// <summary>
    /// Layer "Radio corners": the bottom left (NAV 1, 76.1) and the bottom right (NAV 2, 76.2), in every mode but PLAN
    /// (which draws none, flags included) while sent: their lines, then the arrow. An empty entry (its selector at OFF)
    /// gives no row. A flag in 102.1 replaces its side's corner, spoken as sent, with no arrow: members 1 (VOR 1) and 3
    /// (ADF 1) the left, 2 and 4 the right. The arrow's shape follows the side (NAV 1 single line, NAV 2 double line),
    /// for either receiver. The text is read first, as in <see cref="Rows"/> (<see cref="ReadCornerText"/>).
    /// </summary>
    public static FenixNdLayer CornerRows(FenixChannelPush push, FenixNdData header)
    {
        if (header.Mode == "PLAN") return FenixNdLayer.Of([]);

        var read = new Reader();
        CornerText text = ReadCornerText(read, push, header);
        if (read.Wrong is { } wrongText) return WrongShape(CornersLayerName, wrongText);
        if (FirstUnknown(text.Coded) is { } unknown) return StopsNd(unknown);

        string?[][] lines = text.Lines;
        string?[] flags = text.Flags;

        // Read for its shape only: a selector in another shape costs this layer too.
        _ = read.Selectors(push);
        if (read.Wrong is { } wrong) return WrongShape(CornersLayerName, wrong);

        var rows = new List<string>();
        string[] places = ["Bottom left", "Bottom right"];
        for (int side = 0; side < 2; side++)
        {
            // Members 1 and 3 are the left side's flags, 2 and 4 the right's, in member order.
            string[] flagged =
            [
                .. new[] { flags[side], flags[side + 2] }
                    .Select(flag => FenixChannelNdSpeech.Line(flag, FenixChannelNdSpeech.CornerFlag)).OfType<string>()
            ];
            if (flagged.Length > 0)
            {
                rows.Add($"{places[side]}: {string.Join("; ", flagged)}");
                continue;
            }

            if (JoinedLines(lines[side], FenixChannelNdSpeech.Corner) is not { } spoken) continue;

            rows.Add($"{places[side]}: {spoken}; {FenixChannelNdRows.Words(side == 0 ? LookItem.ArrowNav1 : LookItem.ArrowNav2)}");
        }

        return FenixNdLayer.Of(rows);
    }

    // ---- The channel text. Each layer reads its text through one function, and the ND's shared check reads through
    // the same two, so the window, Ctrl+W and the announcer never disagree on which lines a mode reads.

    // A layer's channel text: the lines it speaks, and every coded line with the place an unknown letter is named by.
    private sealed record TopText(string?[] Flags, string?[] Block, IReadOnlyList<(string? Text, string Place)> Coded);

    private sealed record CornerText(string?[][] Lines, string?[] Flags, IReadOnlyList<(string? Text, string Place)> Coded);

    // Layer "Radio"'s text at the header's mode, in reading order: the corner flags (102.1) in ROSE NAV, ROSE LS and ROSE
    // VOR, then ROSE LS's block (72), or ROSE VOR's (70) unless VOR 1's flag replaces it. Nothing in ARC and PLAN.
    private static TopText ReadTopText(Reader read, FenixChannelPush push, FenixNdData header)
    {
        bool ls = header.Mode == "LS", vor = header.Mode == "VOR", pointers = header.Mode is "NAV" or "LS" or "VOR";
        var coded = new List<(string? Text, string Place)>();

        // The top right block, or the flag that replaces it.
        string?[] block = [null, null, null];
        string?[] flags = [null, null, null, null];
        if (vor || pointers)
        {
            FenixChannelPush? flagLabels = read.Part(push, Remote.NdFlagLabelsCapt, "102");
            FenixChannelPush? corners = read.Part(flagLabels, NdFlagLabels.CornerFlags, "102.1");
            flags =
            [
                read.Text(corners, NdCornerFlags.Vor1, "102.1.1"), read.Text(corners, NdCornerFlags.Vor2, "102.1.2"),
                read.Text(corners, NdCornerFlags.Adf1, "102.1.3"), read.Text(corners, NdCornerFlags.Adf2, "102.1.4")
            ];
            foreach (string? flag in flags) coded.Add((flag, "corner flag"));
        }

        if (ls || (vor && IsBlank(flags[0])))
        {
            int field = ls ? Remote.LsBlockCapt : Remote.VorBlockCapt;
            FenixChannelPush? part = read.Part(push, field, $"{field}");
            block = [read.Text(part, NdRadioBlock.Line1, $"{field}.1"), read.Text(part, NdRadioBlock.Line2, $"{field}.2"),
                read.Text(part, NdRadioBlock.Line3, $"{field}.3")];
            foreach (string? line in block) coded.Add((line, ls ? "ILS block" : "VOR block"));
        }

        return new TopText(flags, block, coded);
    }

    // Layer "Radio corners"' text in every mode but PLAN, in reading order: the corners (76.1 and 76.2, three lines each),
    // then the corner flags (102.1). Nothing in PLAN.
    private static CornerText ReadCornerText(Reader read, FenixChannelPush push, FenixNdData header)
    {
        if (header.Mode == "PLAN") return new CornerText([[null, null, null], [null, null, null]], [null, null, null, null], []);

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

        return new CornerText(lines, flags, coded);
    }

    /// <summary>
    /// The letter the app does not know that the radio layers stop the ND on at <paramref name="header"/>'s mode, and
    /// the line it is in: <see cref="Rows"/>'s text first, then <see cref="CornerRows"/>'s, as <c>Compose</c> runs
    /// them, each read by the layer's own reader and in the layer's order. A layer whose text has a field in another
    /// shape stops nothing (that costs the layer its rows). Null when neither layer stops the ND. The ND's shared check,
    /// <see cref="FenixChannelNd.ReadChecked"/>, asks this.
    /// </summary>
    internal static (char Code, string Place)? UnknownCode(FenixChannelPush push, FenixNdData header)
    {
        var top = new Reader();
        TopText topText = ReadTopText(top, push, header);
        if (top.Wrong is null && FirstUnknown(topText.Coded) is { } inTop) return inTop;

        var corners = new Reader();
        CornerText cornerText = ReadCornerText(corners, push, header);
        return corners.Wrong is null ? FirstUnknown(cornerText.Coded) : null;
    }

    // The first letter the app does not know, in reading order, with the line's place.
    private static (char Code, string Place)? FirstUnknown(IEnumerable<(string? Text, string Place)> texts)
    {
        foreach ((string? text, string place) in texts)
        {
            FenixTextLine line = FenixChannelText.Parse(text);
            if (line.UnknownCodes.Count > 0) return (line.UnknownCodes[0], place);
        }

        return null;
    }

    private static FenixNdLayer StopsNd((char Code, string Place) unknown)
        => FenixNdLayer.StopsNd(FenixChannelSection<FenixNdData>.UnknownCode(unknown.Code, unknown.Place).Problem!);

    private static FenixNdLayer WrongShape(string layer, string path)
        => FenixNdLayer.Failed(layer, $"field {path} has the wrong shape");

    // ---- Helpers.

    private static bool IsBlank(string? coded) => FenixChannelNdSpeech.Line(coded, null) is null;

    // The lines spoken through FenixChannelNdSpeech with the field's usual colors, an absent or empty one skipped,
    // joined by "; ".
    private static string? JoinedLines(IEnumerable<string?> lines, IReadOnlySet<FenixTextColor> usual)
    {
        string[] spoken = [.. lines.Select(line => FenixChannelNdSpeech.Line(line, usual)).OfType<string>()];
        return spoken.Length == 0 ? null : string.Join("; ", spoken);
    }

    // An item the app draws a fixed text for: "text", and its color only where the color tells something (red).
    private static string Spoken(string item)
    {
        FenixNdLookEntry look = FenixChannelNdLook.For(item)!;
        return look.Color is null || !look.ColorTellsApart ? look.Symbol : $"{look.Symbol} {look.Color}";
    }

    private static ulong SelectorOf(IReadOnlyList<ulong> selectors, int side)
        => side < selectors.Count ? selectors[side] : EfisSelectorValue.Off;

    private static string? KindOf(ulong selector) => selector switch
    {
        EfisSelectorValue.Vor => "VOR",
        EfisSelectorValue.Adf => "ADF",
        _ => null
    };

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
