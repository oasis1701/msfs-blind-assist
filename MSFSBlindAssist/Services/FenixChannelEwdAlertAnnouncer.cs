namespace MSFSBlindAssist.Services;

/// <summary>One line an E/WD alert step gives to say, and whether it interrupts what is being said.</summary>
internal readonly record struct FenixEwdAlertLine(string Text, bool Interrupt);

/// <summary>
/// Turns the Fenix's ECAM entries stream into spoken warnings, cautions and memos. The announcer host hands it a snapshot of
/// the channel at each tick and it answers with the lines to say, in order. It speaks nothing itself except through
/// <see cref="Speak"/>, and it reads no clock and starts no timer: every time it is given is the caller's, in milliseconds.
/// </summary>
/// <remarks>
/// <para>What is read. The list is the ECAM's own, sampled every 3.84 s and sent only when it changed, whole; an empty list is a
/// real message, and a heartbeat every 5 s has no entries (measured on the A321 IAE, build 3.0.0.7613, 2026-11-04). So a message
/// other than the last one read replaces the remembered list on every step, whatever the verdict, unless it is a bare heartbeat
/// or has a wrong shape (the gate latches on that anyway). Its entries are classified then, once per message.</para>
/// <para>What is an item. An item is keyed by its Ata and Name, and a key that comes twice in one list counts once, the first. A
/// name holding "-STS-" is never one. A name of "MEMO", any spaces, "-" and any spaces is a memo, whose text and one line text are
/// its Title, or with a blank Title <see cref="FenixChannelEwdAlertLines.MemoWords"/> of the name after that prefix. If that name
/// starts "LAND ASAP" (measured: "MEMO-LAND ASAP (RED)") the entry is the one LAND ASAP failure, whatever its Ata and the rest of
/// its Name (owner's ruling, spec row 60): its line turned from "rLAND ASAP" to "aLAND ASAP" in the fire drill, and its one key
/// holds if the Name changes with it. Any other entry with IsAlert and a Title is a failure, whose text and one line text are the
/// Title. The rest are reported and are no item.</para>
/// <para>The baseline. Not trusted, no frame, or a connection number other than the last step's says nothing and forgets
/// everything said, waiting and held, without depending on how quickly the verdict came back. The step that shows it is not the
/// baseline: the next trusted step with a remembered list is, which takes every item as said, silently, and looks up its line so
/// that a later "cleared" has its color. A failure the list showed coming or going in the gap is never spoken. The list does not
/// change while the power is off, so after a blackout the baseline is the list from before it, and the first new list, 18 to 24 s
/// after the E/WD is trusted (measured), is followed like any other: its start-up failures are spoken and what left it says
/// "cleared", as a sighted pilot sees them on the display (owner's ruling, spec row 62). Never wait for that list. A list
/// belongs to the connection of the ECAM entries call that sent it: an ECAM connection number other than the last step's
/// (<see cref="FenixChannelSnapshot.EcamConnection"/>) forgets the remembered list as well as everything said, waiting and
/// held, so the first list of the new connection is a silent baseline. Only the stream breaking or the Fenix restarting
/// reconnects that call (a blackout does not), and a list from a Fenix that has since restarted, followed by the new one's,
/// would say every failure of the earlier session "cleared".</para>
/// <para>What is new. An item in the list that has not been said waits for its E/WD line. No entry ever came before its line in
/// the measurements, so <see cref="LineWaitMs"/> is the rule's floor. Found, it is due at once with that line; not found by then,
/// it is due without one and with no color, for an entry with no line is a failure or a memo the E/WD is not showing (one hidden
/// behind a fire, measured), and a color is never guessed. One that leaves before it is due says nothing.</para>
/// <para>What is said. A due item says <see cref="FenixChannelSpeech.Line(IReadOnlyList{string?})"/> of its line as
/// <see cref="FenixChannelEwdAlertLines.Find"/> gave it ("ENG 1 FIRE, red"), or of its text with no line ("ENG 1 FIRE"), so a memo
/// says the words the E/WD draws. A failure's row is its line's color: red is under warnings, amber under cautions, and any other
/// color or none is heard unless both rows are muted. A memo is under memos whatever its color. Text the speech refuses is not
/// said and goes to the report.</para>
/// <para>What goes. An item taken as said that is missing from the list is held from the step that read that list, and is gone only
/// once it has stayed out <see cref="LeaveHoldMs"/>. One back before then never left, so nothing is said and it is not new: during
/// a power transfer ENG 1 FIRE was missing from one list, 3.84 s, with the fire still set and the E/WD trusted (owner's ruling,
/// spec row 59). A failure that is gone says its text's words and "cleared" under the row of its latest color; a memo says
/// nothing. While an item stays or is held its line is looked up each step and its color kept, for CLR takes a failure's line off
/// the E/WD while its entry stays (measured): a step with no line keeps the color it had. A color change is never spoken, and an
/// item spoken with no color is not spoken again when its line appears.</para>
/// <para>One step's order. The due red failures, the first one spoken interrupting; the other due failures; the cleared lines; the
/// due memos. LAND ASAP comes last of its group, among the due failures and among the cleared lines, for its entry sorts ahead (its
/// Ata is a memo's) and the failure that caused it should be heard first. No other line interrupts. A muted row's line is dropped and never kept: the items move on all the same, so
/// an unmute replays nothing.</para>
/// <para>Threads. Called on the UI thread only, by the host's timer, so the announcer takes no lock. Every line is the display's
/// own words, so the PC's culture changes none of it.</para>
/// </remarks>
internal sealed class FenixChannelEwdAlertAnnouncer
{
    /// <summary>
    /// How long a new item waits for its E/WD line, in milliseconds. No entry ever came before its line in the measurements, so
    /// this is the rule's floor.
    /// </summary>
    public const long LineWaitMs = 500;

    /// <summary>
    /// How long an item must stay out of the list before it is gone, in milliseconds: the list's 3.84 s beat and a margin.
    /// </summary>
    public const long LeaveHoldMs = 4_500;

    private const string LandAsap = "LAND ASAP";

    // Between the Ata and the Name in a key: a line break, which neither holds, so no key is another's, nor LAND ASAP's.
    private const char KeySeparator = '\n';

    // One item of a list. Its text is what it says with no line, and its line texts are what its line may hold, tried in turn.
    private sealed record Item(string Key, bool IsFailure, bool IsLandAsap, string Text, IReadOnlyList<string> LineTexts);

    // An item taken as said: its latest classification and color, and since when it has been out of the list, if it is.
    private sealed class SaidItem(Item item, FenixTextColor? color)
    {
        public Item Item { get; set; } = item;

        public FenixTextColor? Color { get; set; } = color;

        public long? OutSinceMs { get; set; }
    }

    // An item that fell due this step, with the line it was found on, if any.
    private readonly record struct DueItem(Item Item, IReadOnlyList<string>? Lines, FenixTextColor? Color);

    // The last entries message read, by reference, and the items of the last one that replaced the list; null before any did.
    private FenixEcamEntries? _lastRead;
    private IReadOnlyList<Item>? _items;

    // The connection of the last step, and that of the ECAM entries call, whether or not it was trusted; null before the first.
    private int? _lastConnection;
    private int? _lastEcamConnection;

    // Whether the baseline has been taken: false at the start and after a problem or a reconnect.
    private bool _baselined;

    // The items taken as said, in the order they were, and the new ones with the time they began to wait.
    private readonly List<SaidItem> _said = [];
    private readonly Dictionary<string, long> _waiting = new(StringComparer.Ordinal);

    /// <summary>
    /// One tick. Returns the lines to say, in order (see the class remarks); none while the display is not trusted.
    /// </summary>
    /// <param name="s">The channel's snapshot for the E/WD.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every step.</param>
    /// <param name="isMuted">Whether a row of <see cref="FenixChannelAnnouncementRows"/> is muted now.</param>
    /// <param name="report">Takes what the log should know: an entry that is no item, a line not found, text refused.</param>
    public IReadOnlyList<FenixEwdAlertLine> Step(FenixChannelSnapshot s, long nowMs, Func<string, bool> isMuted,
        Action<string> report)
    {
        // A new ECAM connection first: the list remembered from the connection before is dropped before this step's is read.
        bool newEcamConnection = _lastEcamConnection is { } lastEcam && lastEcam != s.EcamConnection;
        _lastEcamConnection = s.EcamConnection;
        if (newEcamConnection)
        {
            _lastRead = null;
            _items = null;
        }

        bool listRead = ReadEntries(s.Entries, report);

        bool reconnected = _lastConnection is { } last && last != s.Connection;
        _lastConnection = s.Connection;

        if (!s.Verdict.Trusted || s.Frame is not { } frame || reconnected || newEcamConnection)
        {
            _baselined = false;
            _said.Clear();
            _waiting.Clear();
            return [];
        }

        if (_items is not { } items) return [];

        if (!_baselined)
        {
            _baselined = true;
            foreach (Item item in items) _said.Add(new SaidItem(item, ColorOf(LineOf(item, frame))));
            return [];
        }

        if (listRead) FollowList(items, nowMs);

        foreach (SaidItem said in _said)
        {
            if (LineOf(said.Item, frame) is { } lines) said.Color = FenixChannelEwdAlertLines.TitleColor(lines);
        }

        List<DueItem> due = TakeDue(items, frame, nowMs, report);
        List<SaidItem> gone = TakeGone(nowMs);

        return Compose(due, gone, isMuted, report);
    }

    /// <summary>Says the lines in order: an interrupting line with AnnounceImmediate, every other with Announce.</summary>
    public static void Speak(IReadOnlyList<FenixEwdAlertLine> lines, Accessibility.ScreenReaderAnnouncer announcer)
    {
        foreach (FenixEwdAlertLine line in lines)
        {
            if (line.Interrupt) announcer.AnnounceImmediate(line.Text);
            else announcer.Announce(line.Text);
        }
    }

    // Replaces the remembered list when the snapshot holds a message not read yet that has a list in it. True when it did.
    private bool ReadEntries(FenixEcamEntries? message, Action<string> report)
    {
        if (message is null || ReferenceEquals(message, _lastRead)) return false;

        _lastRead = message;
        if (!message.ShapesOk || (message.Heartbeat && message.Entries.Count == 0)) return false;

        _items = Classify(message, report);
        return true;
    }

    private static List<Item> Classify(FenixEcamEntries message, Action<string> report)
    {
        var items = new List<Item>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (FenixEcamEntry entry in message.Entries)
        {
            if (entry.Name.Contains("-STS-", StringComparison.Ordinal)) continue;

            string? memoName = MemoNameOf(entry.Name);
            bool landAsap = memoName is not null && memoName.StartsWith(LandAsap, StringComparison.Ordinal);
            string key = landAsap ? LandAsap : entry.Ata + KeySeparator + entry.Name;
            if (!keys.Add(key)) continue;

            bool blankTitle = string.IsNullOrWhiteSpace(entry.Title);
            if (landAsap)
            {
                items.Add(new Item(key, IsFailure: true, IsLandAsap: true, LandAsap, [LandAsap]));
            }
            else if (memoName is not null)
            {
                FenixEwdMemoWords words = blankTitle
                    ? FenixChannelEwdAlertLines.MemoWords(memoName)
                    : new FenixEwdMemoWords(entry.Title, [entry.Title]);
                items.Add(new Item(key, IsFailure: false, IsLandAsap: false, words.Text, words.LineTexts));
            }
            else if (!entry.IsAlert)
            {
                report($"entry of no known kind: {entry.Ata} {entry.Name}");
            }
            else if (blankTitle)
            {
                report($"alert entry with no title: {entry.Ata} {entry.Name}");
            }
            else
            {
                items.Add(new Item(key, IsFailure: true, IsLandAsap: false, entry.Title, [entry.Title]));
            }
        }

        return items;
    }

    // The name after "MEMO", any spaces, "-" and any spaces (measured: "MEMO-PARK BRK", "MEMO - COMPANY MSG"), or null.
    private static string? MemoNameOf(string name)
    {
        if (!name.StartsWith("MEMO", StringComparison.Ordinal)) return null;

        string rest = name["MEMO".Length..].TrimStart(' ');
        return rest.StartsWith('-') ? rest[1..].TrimStart(' ') : null;
    }

    // The list just changed: a said item in it takes its latest classification and is back if it was out, one not in it is
    // out from this step unless it already was, and a waiting item not in it leaves. An item that is neither said nor waiting is new.
    private void FollowList(IReadOnlyList<Item> items, long nowMs)
    {
        foreach (SaidItem said in _said)
        {
            if (items.FirstOrDefault(item => item.Key == said.Item.Key) is { } latest)
            {
                said.Item = latest;
                said.OutSinceMs = null;
            }
            else
            {
                said.OutSinceMs ??= nowMs;
            }
        }

        foreach (string key in _waiting.Keys.ToList())
        {
            if (!items.Any(item => item.Key == key)) _waiting.Remove(key);
        }

        foreach (Item item in items)
        {
            if (!_said.Exists(said => said.Item.Key == item.Key)) _waiting.TryAdd(item.Key, nowMs);
        }
    }

    // The waiting items with a line now or out of time, in list order, moved to the said ones.
    private List<DueItem> TakeDue(IReadOnlyList<Item> items, FenixChannelFrame frame, long nowMs, Action<string> report)
    {
        var due = new List<DueItem>();
        foreach (Item item in items)
        {
            if (!_waiting.TryGetValue(item.Key, out long sinceMs)) continue;

            IReadOnlyList<string>? lines = LineOf(item, frame);
            if (lines is null)
            {
                if (nowMs - sinceMs < LineWaitMs) continue;

                report($"no line on the E/WD for {item.Text}; spoken without a color");
            }

            _waiting.Remove(item.Key);

            FenixTextColor? color = ColorOf(lines);
            _said.Add(new SaidItem(item, color));
            due.Add(new DueItem(item, lines, color));
        }

        return due;
    }

    // The said items out of the list for the whole hold, taken off the said ones.
    private List<SaidItem> TakeGone(long nowMs)
    {
        List<SaidItem> gone = _said.FindAll(said => said.OutSinceMs is { } since && nowMs - since >= LeaveHoldMs);
        _said.RemoveAll(gone.Contains);
        return gone;
    }

    private static List<FenixEwdAlertLine> Compose(List<DueItem> due, List<SaidItem> gone, Func<string, bool> isMuted,
        Action<string> report)
    {
        var spoken = new List<FenixEwdAlertLine>();

        // OrderBy is stable: list order but for LAND ASAP, which goes last of its group.
        List<DueItem> failures = due.Where(one => one.Item.IsFailure).OrderBy(one => one.Item.IsLandAsap).ToList();

        // Muted lines are dropped before the interrupt is chosen, and refused text never takes it either.
        bool interrupted = false;
        foreach (DueItem red in failures.Where(one => one.Color == FenixTextColor.Red))
        {
            if (DueWords(red, FailureRowMuted(red.Color, isMuted), report) is not { } words) continue;

            spoken.Add(new FenixEwdAlertLine(words, !interrupted));
            interrupted = true;
        }

        foreach (DueItem other in failures.Where(one => one.Color != FenixTextColor.Red))
        {
            if (DueWords(other, FailureRowMuted(other.Color, isMuted), report) is { } words)
            {
                spoken.Add(new FenixEwdAlertLine(words, false));
            }
        }

        foreach (SaidItem cleared in gone.Where(one => one.Item.IsFailure).OrderBy(one => one.Item.IsLandAsap))
        {
            string text = cleared.Item.Text;
            if (Heard(FenixChannelSpeech.Line(text), text, FailureRowMuted(cleared.Color, isMuted), report) is { } words)
            {
                spoken.Add(new FenixEwdAlertLine(words + " cleared", false));
            }
        }

        foreach (DueItem memo in due.Where(one => !one.Item.IsFailure))
        {
            if (DueWords(memo, isMuted(FenixChannelAnnouncementRows.MemosKey), report) is { } words)
            {
                spoken.Add(new FenixEwdAlertLine(words, false));
            }
        }

        return spoken;
    }

    // What a due item says: its line as Find gave it, or its text with no line. Null as for Heard.
    private static string? DueWords(DueItem due, bool muted, Action<string> report)
    {
        string? words = due.Lines is { } lines ? FenixChannelSpeech.Line(lines) : FenixChannelSpeech.Line(due.Item.Text);
        return Heard(words, due.Item.Text, muted, report);
    }

    // The words, or null when their row is muted or the speech refused them. Refused text is reported either way.
    private static string? Heard(string? words, string text, bool muted, Action<string> report)
    {
        if (words is null) report($"text the speech refused: {text}");

        return muted ? null : words;
    }

    private static bool FailureRowMuted(FenixTextColor? color, Func<string, bool> isMuted) => color switch
    {
        FenixTextColor.Red => isMuted(FenixChannelAnnouncementRows.WarningsKey),
        FenixTextColor.Amber => isMuted(FenixChannelAnnouncementRows.CautionsKey),
        _ => isMuted(FenixChannelAnnouncementRows.WarningsKey) && isMuted(FenixChannelAnnouncementRows.CautionsKey)
    };

    // The item's line on the E/WD, each of its line texts in turn: failures in the left column first, memos in the right.
    private static IReadOnlyList<string>? LineOf(Item item, FenixChannelFrame frame)
    {
        IReadOnlyList<string> own = item.IsFailure ? frame.UpperEcamLeft : frame.UpperEcamRight;
        IReadOnlyList<string> other = item.IsFailure ? frame.UpperEcamRight : frame.UpperEcamLeft;

        foreach (string text in item.LineTexts)
        {
            if (FenixChannelEwdAlertLines.Find(text, own, other) is { } lines) return lines;
        }

        return null;
    }

    private static FenixTextColor? ColorOf(IReadOnlyList<string>? lines)
        => lines is null ? null : FenixChannelEwdAlertLines.TitleColor(lines);
}
