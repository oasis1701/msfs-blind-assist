using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What is spoken and listed for the 757 upper EICAS (S8, S9). A message is BlueBird's text
/// exactly, its leading space dropped here and nowhere else, and is told apart from the others by its
/// <see cref="Bbs757EicasMessage.Id"/>. Every reading's message counts, on the screen or past the first page. Only the
/// messages, the thrust reference and the N1 and EGT colors are ever spoken, each on its own Ctrl+M row; the window
/// lists everything BlueBird draws. Pure; every number is formatted with the invariant culture (the reading's own are
/// already strings).
/// </summary>
public static class Bbs757UpperEicasSpeech
{
    /// <summary>The <see cref="Bbs757EicasMessage.Level"/> words the layout's level table gives (layout-fields.md).</summary>
    public const string WarningLevel = "warning";
    public const string CautionLevel = "caution";
    public const string AdvisoryLevel = "advisory";
    public const string CommunicationLevel = "communication";

    private const string MessageLine = "EICAS: ";
    private const string GoneLine = "EICAS gone: ";
    private const string RecallLine = "EICAS recall: ";
    private const string NothingRecalled = "no cancelled messages";
    private const string ThrustLine = "Thrust reference: ";
    private const string None = "none";
    private const string Blank = "blank";
    private const string Normal = "normal";
    private const string Left = "L", Right = "R";
    private const string CancelledMark = " (cancelled)";
    private const string NoMessages = "Messages: none";

    /// <summary>The advisory saying the FMC has a scratchpad message, which the CDU window already speaks ("CDU Messages
    /// While Closed"): never said as new or gone (the owner, after the 2026-11-10 flight), but listed, counted by CANC and
    /// read back by RECALL.</summary>
    internal const string FmcMessageText = "FMC MESSAGE";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The levels in the order their new messages are said, each with the start of its line and its row's
    /// label. A level not among them (only on a corrupt reading, which is never spoken or shown) is said last, as
    /// "EICAS: ", and listed under its own word.</summary>
    private static readonly (string Level, string Line, string Row)[] Levels =
    {
        (WarningLevel, "EICAS warning: ", "Warning"),
        (CautionLevel, "EICAS caution: ", "Caution"),
        (AdvisoryLevel, MessageLine, "Advisory"),
        (CommunicationLevel, MessageLine, "Communication"),
    };

    /// <summary>
    /// The lines that tell what changed from <paramref name="heard"/> to <paramref name="now"/>, in this order (empty:
    /// nothing to say). They keep their words after "resumed": "EICAS warning" names the display, and the thrust
    /// line has no prefix.
    /// <list type="number">
    /// <item>New, on <see cref="Bbs757UpperEicasDisplay.MessagesKey"/>: each message uncancelled now that was not in
    /// <paramref name="heard"/>, or was there cancelled while no RECALL was pressed (it left and was posted again); one
    /// line per level, warning, caution, advisory, then communication: "EICAS warning: L ENGINE FIRE, R ENGINE FIRE",
    /// "EICAS caution: …", "EICAS: …".</item>
    /// <item>Gone: each message uncancelled in <paramref name="heard"/> and no longer in the list, in its order there:
    /// "EICAS gone: A, B". A cancelled message leaving was not on the screen and says nothing; one being cancelled is
    /// not gone. Neither line ever says <see cref="FmcMessageText"/>, which the CDU window speaks.</item>
    /// <item>Cancelled, CANC's read-back: "EICAS: 3 messages cancelled", the messages uncancelled before and cancelled
    /// now.</item>
    /// <item>Recall, RECALL's read-back, when its time changed (each press stamps it): "EICAS recall: " and the
    /// messages it brought back, or "no cancelled messages". They are never also said as new.</item>
    /// <item>The thrust reference as drawn, on <see cref="Bbs757UpperEicasDisplay.ThrustKey"/>: "Thrust reference: CLB",
    /// nothing drawn "none", so the pilot hears the reference go.</item>
    /// <item>The N1 and EGT colors, which a sighted pilot sees turn amber or red, on
    /// <see cref="Bbs757UpperEicasDisplay.ColorsKey"/>: each engine's that changed while its value is drawn before and
    /// after, in one line, left N1, right N1, left EGT, right EGT: "EICAS: L EGT amber, R EGT amber", no color
    /// "normal". A value coming back from blank amber or red says its color in that line ("EICAS: L EGT red": a sighted
    /// pilot sees it come back in that color); one coming back with no color, or going
    /// blank, says nothing.</item>
    /// </list>
    /// Nothing else is spoken: no gauge value, TAT, TAI, REV, in-flight start block, page or RECALL word.
    /// </summary>
    public static IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757UpperEicasReading heard, Bbs757UpperEicasReading now)
    {
        var lines = new List<Bbs757SpokenLine>();
        var before = States(heard);
        var after = States(now);
        bool recallPressed = double.IsFinite(heard.RecallTime) && double.IsFinite(now.RecallTime)
            && !heard.RecallTime.Equals(now.RecallTime);
        bool CancelledBefore(int id) => before.TryGetValue(id, out bool up) && !up;

        // 1. New: grouped by level (the groups keep the screen's order within, and OrderBy is stable).
        var fresh = Uncancelled(now).Where(m => SaidNewOrGone(m) && (!before.ContainsKey(m.Id) || (CancelledBefore(m.Id) && !recallPressed)));
        foreach (var level in fresh.GroupBy(m => LevelIndex(m.Level)).OrderBy(g => g.Key))
            lines.Add(OnMessages(LineStart(level.Key) + Texts(level)));

        // 2. Gone.
        var gone = Uncancelled(heard).Where(m => SaidNewOrGone(m) && !after.ContainsKey(m.Id)).ToList();
        if (gone.Count > 0) lines.Add(OnMessages(GoneLine + Texts(gone)));

        // 3. Cancelled.
        int cancelled = before.Count(kv => kv.Value && after.TryGetValue(kv.Key, out bool up) && !up);
        if (cancelled > 0)
            lines.Add(OnMessages(MessageLine + cancelled.ToString(Inv) + (cancelled == 1 ? " message cancelled" : " messages cancelled")));

        // 4. Recall.
        if (recallPressed)
        {
            var back = Uncancelled(now).Where(m => CancelledBefore(m.Id)).ToList();
            lines.Add(OnMessages(RecallLine + (back.Count == 0 ? NothingRecalled : Texts(back))));
        }

        // 5. The thrust reference.
        if (!string.Equals(heard.ThrustReference, now.ThrustReference, StringComparison.Ordinal))
            lines.Add(new Bbs757SpokenLine(Bbs757UpperEicasDisplay.ThrustKey, ThrustLine + (now.ThrustReference ?? None)));

        // 6. The engine gauge colors.
        var colors = new List<string>(4);
        foreach (string label in new[] { Bbs757EicasValue.N1Label, Bbs757EicasValue.EgtLabel })
        {
            if (Row(heard, label) is not { } then || Row(now, label) is not { } current) continue;
            if (ColorSaid(then.Left, then.LeftColor, current.Left, current.LeftColor) is { } leftColor)
                colors.Add(Left + " " + label + " " + leftColor);
            if (ColorSaid(then.Right, then.RightColor, current.Right, current.RightColor) is { } rightColor)
                colors.Add(Right + " " + label + " " + rightColor);
        }
        if (colors.Count > 0) lines.Add(new Bbs757SpokenLine(Bbs757UpperEicasDisplay.ColorsKey, MessageLine + string.Join(", ", colors)));

        return lines;
    }

    /// <summary>
    /// The Alt+E window's rows, in the order the design gives them: the engine rows ("EPR: 1.03, 1.03", "N1: 19.9,
    /// 19.9", "EGT: 358, 360"; a blank value "blank", a color after its value: "EGT: 800 amber, 360"); TAI and REV
    /// while drawn ("TAI: L, R", "REV: L amber", only the sides drawn); the N1 and EGT max memories while set ("N1 max:
    /// 109.2, blank"); the thrust reference ("Thrust reference: TO 1", or "none") and the reference EPR while drawn;
    /// TAT ("TAT: +11c"); the in-flight start block's lines; every message in screen order ("Caution: L ENG SHUTDOWN",
    /// a cancelled one with " (cancelled)"), or "Messages: none"; the page line ("PAGE 2") while BlueBird draws it and
    /// the RECALL word while it shows, both as the reading carries them in the layout's words.
    /// </summary>
    public static IReadOnlyList<string> Rows(Bbs757UpperEicasReading r)
    {
        var rows = new List<string>();
        foreach (string label in new[] { Bbs757EicasValue.EprLabel, Bbs757EicasValue.N1Label, Bbs757EicasValue.EgtLabel })
            if (Row(r, label) is { } engines) rows.Add(BothSides(engines));
        if (Row(r, Bbs757EicasValue.TaiLabel) is { } tai) rows.Add(DrawnSides(tai));
        if (Row(r, Bbs757EicasValue.RevLabel) is { } rev) rows.Add(DrawnSides(rev));
        foreach (string label in new[] { Bbs757EicasValue.N1MaxLabel, Bbs757EicasValue.EgtMaxLabel })
            if (Row(r, label) is { } max) rows.Add(BothSides(max));
        rows.Add(ThrustLine + (r.ThrustReference ?? None));
        if (Row(r, Bbs757EicasValue.EprReferenceLabel) is { } reference) rows.Add(BothSides(reference));
        if (Row(r, Bbs757EicasValue.TatLabel) is { } tat) rows.Add(tat.Label + ": " + Side(tat.Left, tat.LeftColor));
        rows.AddRange(r.Envelope);
        if (r.Messages.Count == 0) rows.Add(NoMessages);
        foreach (var m in r.Messages)
            rows.Add(RowLabel(m.Level) + ": " + Spoken(m.Text) + (m.Cancelled ? CancelledMark : ""));
        if (r.PageLine is { } page) rows.Add(page);
        if (r.RecallLine is { } recall) rows.Add(recall);
        return rows;
    }

    private static Bbs757SpokenLine OnMessages(string text) => new(Bbs757UpperEicasDisplay.MessagesKey, text);

    /// <summary>Each message id in <paramref name="r"/>, true when any of its entries is uncancelled.</summary>
    private static Dictionary<int, bool> States(Bbs757UpperEicasReading r)
    {
        var states = new Dictionary<int, bool>();
        foreach (var m in r.Messages)
            states[m.Id] = (states.TryGetValue(m.Id, out bool up) && up) || !m.Cancelled;
        return states;
    }

    /// <summary>The uncancelled messages of <paramref name="r"/> in screen order, each id once.</summary>
    private static IEnumerable<Bbs757EicasMessage> Uncancelled(Bbs757UpperEicasReading r)
    {
        var seen = new HashSet<int>();
        foreach (var m in r.Messages)
            if (!m.Cancelled && seen.Add(m.Id)) yield return m;
    }

    private static string Texts(IEnumerable<Bbs757EicasMessage> messages) => string.Join(", ", messages.Select(m => Spoken(m.Text)));

    /// <summary>Every message but <see cref="FmcMessageText"/>, which the CDU window speaks.</summary>
    private static bool SaidNewOrGone(Bbs757EicasMessage m) => !string.Equals(Spoken(m.Text), FmcMessageText, StringComparison.Ordinal);

    /// <summary>BlueBird's text with its leading space (an advisory's or communication message's indent) dropped.</summary>
    private static string Spoken(string? text) => (text ?? "").Trim();

    private static int LevelIndex(string? level)
    {
        for (int i = 0; i < Levels.Length; i++)
            if (string.Equals(Levels[i].Level, level, StringComparison.Ordinal)) return i;
        return Levels.Length;
    }

    private static string LineStart(int levelIndex) => levelIndex < Levels.Length ? Levels[levelIndex].Line : MessageLine;

    private static string RowLabel(string? level)
    {
        int i = LevelIndex(level);
        return i < Levels.Length ? Levels[i].Row : level ?? Bbs757WordTable.Unrecognised;
    }

    /// <summary>The color word to say for one engine's value, or null: a value drawn before and after whose color changed
    /// says its new one ("normal" for none); a value coming back from blank says its color only when it has one (a
    /// sighted pilot sees it come back amber or red); a
    /// value going blank, or staying blank, says nothing.</summary>
    private static string? ColorSaid(string? valueBefore, string? colorBefore, string? valueAfter, string? colorAfter)
    {
        if (valueAfter is null) return null;
        if (valueBefore is null) return colorAfter;
        return string.Equals(colorBefore, colorAfter, StringComparison.Ordinal) ? null : colorAfter ?? Normal;
    }

    /// <summary>The gauge row with <paramref name="label"/>, or null when the reading has none.</summary>
    private static Bbs757EicasValue? Row(Bbs757UpperEicasReading r, string label)
    {
        foreach (var v in r.Values)
            if (string.Equals(v.Label, label, StringComparison.Ordinal)) return v;
        return null;
    }

    /// <summary>"N1: 109.0 red, 98.0": both engines, a blank one "blank".</summary>
    private static string BothSides(Bbs757EicasValue v) => v.Label + ": " + Side(v.Left, v.LeftColor) + ", " + Side(v.Right, v.RightColor);

    /// <summary>"REV: L green, R green": only the engines that draw it, each with its color.</summary>
    private static string DrawnSides(Bbs757EicasValue v)
    {
        var sides = new List<string>(2);
        if (v.Left is not null) sides.Add(v.LeftColor is null ? Left : Left + " " + v.LeftColor);
        if (v.Right is not null) sides.Add(v.RightColor is null ? Right : Right + " " + v.RightColor);
        return v.Label + ": " + string.Join(", ", sides);
    }

    private static string Side(string? value, string? color) => value is null ? Blank : color is null ? value : value + " " + color;
}
