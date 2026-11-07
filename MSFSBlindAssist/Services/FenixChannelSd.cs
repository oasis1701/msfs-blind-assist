using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Reads the fields of one RemoteData message for the SD window and its pages: a field left out as its protobuf
/// default, a field in another wire shape as a problem named by its path in the push ("field 133.1.1 has the wrong
/// shape"), and a nested message the page needs and the push left out as a problem too ("no field 133"), never as its
/// defaults. <see cref="Of"/> gives the reader of RemoteData; <see cref="Message"/> gives one for a part nested in it.
/// </summary>
/// <remarks>
/// <para>One problem for all. Every reader handed out from one <see cref="Of"/> shares one <see cref="Problem"/>: the
/// first one found, which a later one never replaces. From then on every read gives its default, so that nothing is
/// read out of a message that is not what the app was checked against. A page composer reads what it needs and, while
/// <see cref="Problem"/> is set, returns <c>FenixChannelRows.NotAvailable(Problem)</c> instead of its rows, so none of
/// those defaults reaches a pilot; the SD window reports that problem once, under the page's name. The reader itself
/// reports a stopping problem nowhere.</para>
/// <para>A problem that does not stop the page goes to the report given to <see cref="Of"/>, at once: a valve value
/// that is no state of <see cref="FenixChannelFields.ValveStateEnum"/> ("field 134.11.4 has an unknown value 9"). A
/// page composer passes its context's <c>ReportProblem</c>, which names the page and logs each distinct line once.</para>
/// <para>Absent is not zero, except where protobuf makes it so. A double or a varint left out is its default (0, false,
/// and for a valve Open, which the Fenix leaves out on the wire); a repeated field left out has no elements; a nested
/// message left out is the problem above. NaN, which the channel sends for a value it does not draw, is kept.</para>
/// <para>Not thread-safe: one composition makes one reader, on one thread (the window's, the UI thread).</para>
/// </remarks>
internal sealed class FenixChannelSdReader
{
    // What every reader handed out from one Of shares.
    private sealed class Shared(Action<string> report)
    {
        public string? Problem;
        public readonly Action<string> Report = report;
    }

    // Null only when a problem has been found: a part left out or reshaped is that problem.
    private readonly FenixChannelPush? _message;
    private readonly string _path;
    private readonly Shared _shared;

    private FenixChannelSdReader(FenixChannelPush? message, string path, Shared shared)
    {
        _message = message;
        _path = path;
        _shared = shared;
    }

    /// <summary>
    /// The reader of a RemoteData message (path ""), with no problem yet. <paramref name="report"/> gets each problem
    /// that does not stop the page, as it is found (see the remarks).
    /// </summary>
    public static FenixChannelSdReader Of(FenixChannelPush message, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(report);

        return new FenixChannelSdReader(message, "", new Shared(report));
    }

    /// <summary>
    /// The first problem any reader of this message found, in the words a window's status row reads after
    /// "not available, "; null while there is none.
    /// </summary>
    public string? Problem => _shared.Problem;

    /// <summary>
    /// The reader of the nested message <paramref name="field"/>. Left out, it is "no field &lt;path&gt;"; not a message
    /// that can be read, "field &lt;path&gt; has the wrong shape". With either, or a problem found before, the reader
    /// given reads nothing but defaults.
    /// </summary>
    public FenixChannelSdReader Message(int field)
    {
        string path = PathOf(field);
        FenixChannelPush? part = null;
        if (Find(field) is { } found)
        {
            part = found.AsMessage();
            if (part is null) Fail(WrongShape(path));
        }
        else
        {
            Fail(NoField(path));
        }

        return new FenixChannelSdReader(Problem is null ? part : null, path, _shared);
    }

    /// <summary>A fixed64 read as a double: 0 when left out, NaN as sent.</summary>
    public double Double(int field) => Read(field, FenixWireType.Fixed64) is { } found ? found.AsDouble() : 0;

    /// <summary>A varint read as a bool: false when left out.</summary>
    public bool Bool(int field) => Read(field, FenixWireType.Varint) is { } found && found.AsBool();

    /// <summary>
    /// A varint read as an int32, its low 32 bits (protobuf sends a negative int32 sign-extended to ten bytes): 0 when
    /// left out.
    /// </summary>
    public int Int(int field) => Read(field, FenixWireType.Varint) is { } found ? unchecked((int)found.Varint) : 0;

    /// <summary>
    /// A <see cref="FenixChannelFields.ValveStateEnum"/> varint as its one word (<see cref="FenixChannelSdPages.ValveWord"/>):
    /// "open" when left out, which is how the Fenix sends Open. A value that is no state reads "not available" and is
    /// reported, "field &lt;path&gt; has an unknown value &lt;n&gt;", without stopping the page.
    /// </summary>
    public string Valve(int field)
    {
        int state = Int(field);
        if (!IsValveState(state))
        {
            _shared.Report(string.Create(CultureInfo.InvariantCulture,
                $"field {PathOf(field)} has an unknown value {state}"));
        }

        return FenixChannelSdPages.ValveWord(state);
    }

    /// <summary>
    /// A repeated NullWrapped string, every element in the order sent: each is a message whose field 1 is the string, and
    /// one without it (a null the contract wraps) is "". None when left out. An element that is not a message is "field
    /// &lt;path&gt; has the wrong shape", and one whose field 1 is not a string "field &lt;path&gt;.1 has the wrong
    /// shape"; either gives no elements.
    /// </summary>
    public IReadOnlyList<string> Strings(int field)
    {
        if (Problem is not null || _message is null) return [];

        string path = PathOf(field);
        var texts = new List<string>();
        foreach (FenixChannelField element in _message.All(field))
        {
            FenixChannelPush? wrapped = element.AsMessage();
            if (wrapped is null)
            {
                Fail(WrongShape(path));
                return [];
            }

            string text = "";
            if (wrapped.TryGet(FenixChannelFields.NullWrapped.Value, out FenixChannelField value))
            {
                if (value.AsString() is not { } sent)
                {
                    Fail(WrongShape($"{path}.{Number(FenixChannelFields.NullWrapped.Value)}"));
                    return [];
                }

                text = sent;
            }

            texts.Add(text);
        }

        return texts;
    }

    /// <summary>
    /// Records <paramref name="problem"/> as this message's problem, unless one was found before: the first one stays.
    /// For a page that finds content missing that no field read can see ("no engines").
    /// </summary>
    public void Fail(string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);

        _shared.Problem ??= problem;
    }

    // The field, or null when it was left out, this reader reads nothing, or a problem has been found.
    private FenixChannelField? Find(int field)
    {
        if (Problem is not null || _message is null) return null;

        return _message.TryGet(field, out FenixChannelField found) ? found : null;
    }

    // The field when it came in the wire type asked for; null when it was left out, or, as a wrong shape, in another.
    private FenixChannelField? Read(int field, FenixWireType type)
    {
        if (Find(field) is not { } found) return null;
        if (found.Type == type) return found;

        Fail(WrongShape(PathOf(field)));
        return null;
    }

    // The problems in the words every feature's decoder uses (FenixChannelSection's factories): "field 133.1 has the
    // wrong shape", and "no field 133" for a part left out.
    private static string WrongShape(string path) => FenixChannelSection<string>.WrongShape(path).Problem!;

    private static string NoField(string path) => FenixChannelSection<string>.Missing($"field {path}").Problem!;

    private static bool IsValveState(int state) => state is FenixChannelFields.ValveStateEnum.Open
        or FenixChannelFields.ValveStateEnum.Closed
        or FenixChannelFields.ValveStateEnum.Transit
        or FenixChannelFields.ValveStateEnum.FailedOpen
        or FenixChannelFields.ValveStateEnum.FailedClose
        or FenixChannelFields.ValveStateEnum.DataInvalid;

    private string PathOf(int field) => _path.Length == 0 ? Number(field) : $"{_path}.{Number(field)}";

    private static string Number(int field) => field.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The rows of the Fenix SD window: the page shown, its content, and the bottom line, from one snapshot of the display
/// channel. <see cref="Compose"/> decides what the window shows; the page composers (<see cref="FenixChannelSdPages"/>)
/// read their pages' fields with <see cref="FenixChannelSdReader"/> and write numbers with <see cref="Whole"/> and
/// <see cref="OneDecimal"/>, and weights in <see cref="WeightUnit"/>, so that every page reads alike.
/// </summary>
/// <remarks>
/// <para>What takes the whole window to its one status row, in this order: no message ("no data", which is not
/// reported: nothing is wrong); a format letter the app does not know in the bottom line, or in the STATUS text while
/// the STATUS page is shown ([FNX-24], <see cref="FenixChannelSdFrame.UnknownCode"/>); no bottom line, a bottom line of
/// another length or shape (<see cref="FenixChannelSdFrame.BottomLine"/>); and a weight unit (154) of another shape.
/// Each of those is reported once. A page's own problem stops that page only: its rows become one row, "&lt;PAGE&gt;:
/// not available, &lt;problem&gt;", and the page's name and the bottom line stay (spec 5, row 30).</para>
/// <para>The bottom line reads its TAT, SAT, UTC and gross weight, each element spoken with its colors
/// (<see cref="FenixChannelSpeech"/>) and an empty one "not available". The UTC row is labeled "UTC", though the SD prints
/// only "15 H 06" (owner question Q3), and is spoken as hours:minutes, "15:06", as every other time in the app is.
/// Elements 2, 3, 7 and 8 have no label and are not read.</para>
/// </remarks>
internal static class FenixChannelSd
{
    /// <summary>The SD's short name: the window's title is "Fenix SD", its list "SD display", and the log's lines start "SD: ".</summary>
    public const string DisplayName = "SD";

    /// <summary>
    /// What the SD draws for a value it has none for, and so what a number that is not one reads: crosses (owner
    /// question Q4, spec 5 row 15).
    /// </summary>
    public const string Crosses = "XX";

    // The pages' and the bottom line's numbers are written the same way whatever the PC's culture is.
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// A whole number, rounded away from zero, in the invariant culture (an ASCII minus), never "-0": "-53" for -52.5,
    /// "0" for -0.4. NaN and the infinities are "XX".
    /// </summary>
    public static string Whole(double value) => Written(value, 0, "0");

    /// <summary>
    /// A number to one decimal, rounded away from zero, in the invariant culture, never "-0.0": "13.9" for 13.94, "14.0"
    /// for 14, "0.0" for -0.04. NaN and the infinities are "XX".
    /// </summary>
    public static string OneDecimal(double value) => Written(value, 1, "0.0");

    /// <summary>
    /// The weight unit the SD draws its weights in: "KG" while field 154 (isKg) is set, else "LBS". The Fenix leaves
    /// false out on the wire, so a field left out is pounds. A 154 of another shape is <paramref name="remote"/>'s
    /// problem, and reads "LBS" like every default after a problem.
    /// </summary>
    public static string WeightUnit(FenixChannelSdReader remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        return remote.Bool(FenixChannelFields.Remote.IsKg) ? "KG" : "LBS";
    }

    /// <summary>
    /// The window's rows for <paramref name="s"/>: the page's name, the page's content, then the bottom line; or the
    /// window's one status row, its problem reported to <paramref name="report"/> (see the remarks).
    /// </summary>
    /// <param name="s">The SD's snapshot, whose verdict the window has judged trusted.</param>
    /// <param name="variant">
    /// The loaded Fenix, read by the window at each refresh: a page whose layout depends on the airframe (FUEL) follows
    /// it, since the channel sends no airframe.
    /// </param>
    /// <param name="report">
    /// Where each problem goes, the window's own as they are ("no bottom line") and a page's as "&lt;PAGE&gt;:
    /// &lt;problem&gt;"; the window passes the monitor's log under "SD", which writes each distinct line once.
    /// </param>
    /// <param name="apuBleedHold">The window's hold on the APU bleed valve's word; with none the word reads as sent.</param>
    public static FenixChannelRows Compose(
        FenixChannelSnapshot s, Aircraft.FenixVariant variant, Action<string> report, FenixChannelApuBleedHold? apuBleedHold = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(report);

        if (s.Push is not { } push || s.Frame is not { } frame) return FenixChannelRows.NotAvailable("no data");

        int page = frame.FwcPage;
        if (FenixChannelSdFrame.UnknownCode(push, page) is { } unknown) return Stop(unknown);

        FenixChannelSection<IReadOnlyList<string>> bottomLine = FenixChannelSdFrame.BottomLine(push);
        if (!bottomLine.IsOk) return Stop(bottomLine.Problem ?? "no bottom line");

        FenixChannelSdReader remote = FenixChannelSdReader.Of(push, report);
        string unit = WeightUnit(remote);
        if (remote.Problem is { } shape) return Stop(shape);

        string name = FenixChannelSdPages.NameOf(page);
        var ctx = new FenixChannelSdPageContext(push, frame, variant, problem => report($"{name}: {problem}"))
        {
            ApuBleedHold = apuBleedHold,
            AtMs = s.AtMs
        };

        var rows = new List<string> { name };
        rows.AddRange(PageRows(page, name, ctx));
        rows.AddRange(BottomLineRows(bottomLine.Value!, unit));
        return FenixChannelRows.Of(rows);

        FenixChannelRows Stop(string problem)
        {
            report(problem);
            return FenixChannelRows.NotAvailable(problem);
        }
    }

    // What the page's composer gave: its rows; one row with its problem, which is reported under the page's name; or
    // nothing, for a page with no composer. A result with neither rows nor a problem was never filled in, and fails
    // closed as "no data", as the window does with one.
    private static IReadOnlyList<string> PageRows(int page, string name, FenixChannelSdPageContext ctx)
    {
        if (FenixChannelSdPages.Compose(page, ctx) is not { } composed) return [];

        string? problem = composed.Problem ?? (composed.Rows is null ? "no data" : null);
        if (problem is null) return composed.Rows!;

        ctx.ReportProblem(problem);
        return [$"{name}: not available, {problem}"];
    }

    // The bottom line's four rows, from its nine elements, which BottomLine has counted.
    private static IEnumerable<string> BottomLineRows(IReadOnlyList<string> elements, string unit)
    {
        yield return $"TAT °C: {Spoken(elements[FenixChannelFields.EcamBottom.Tat])}";
        yield return $"SAT °C: {Spoken(elements[FenixChannelFields.EcamBottom.Sat])}";
        yield return $"UTC: {Utc(elements[FenixChannelFields.EcamBottom.UtcHour], elements[FenixChannelFields.EcamBottom.UtcMinute])}";
        yield return $"GW {unit}: {Spoken(elements[FenixChannelFields.EcamBottom.GrossWeight])}";
    }

    // One element spoken with its colors, or "not available" when it has nothing to say.
    private static string Spoken(string coded) => FenixChannelSpeech.Line(coded) ?? "not available";

    // The hour and the minute joined with a colon, "15:06", spoken as one coded line; "not available" unless both have
    // something to say. The SD draws "15 H 06", which a screen reader reads as written, and the app speaks every other
    // time as hours:minutes (Output Shift+Z: "18:16Z"), so the row does too (the claimant found the H odd by ear).
    private static string Utc(string hour, string minute)
    {
        if (FenixChannelSpeech.Line(hour) is null || FenixChannelSpeech.Line(minute) is null) return "not available";

        return FenixChannelSpeech.Line($"{hour}:{minute}") ?? "not available";
    }

    // A finite number rounded away from zero to its places and written in the invariant culture; a rounded -0 is written
    // as 0, which the comparison with 0 finds (-0.0 == 0).
    private static string Written(double value, int places, string format)
    {
        if (!double.IsFinite(value)) return Crosses;

        double rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0;

        return rounded.ToString(format, Invariant);
    }
}
