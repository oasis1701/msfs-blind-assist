using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What a row of the CDU window is. <c>Blank</c> is the one row of a screen with nothing on it.</summary>
public enum Bbs757CduRowKind { Title, Label, Value, Scratchpad, Blank }

/// <summary>
/// One row of the CDU window: its kind, the line select key it belongs to (1-6 for a label or a
/// value row, 0 otherwise) and the text the window lists. Kind and line are the row's identity: they
/// survive the screen changing around it, where an index does not.
/// </summary>
public sealed record Bbs757CduRow(Bbs757CduRowKind Kind, int Line, string Text);

/// <summary>
/// The CDU window's rows with an identity, so the cursor follows the pilot's line instead of a list
/// index ([MD11-17]'s rule, the MD-11's <c>Md11McduRows</c>). A label row is listed only when it has
/// text, so a constraint deleted above the cursor removes a row and shifts every index below it; a
/// title going ACT → MOD after an entry is the same page. Before this, both moved the cursor (plan 08).
/// The texts are the shape the other Boeing CDU windows list, for speech and for a 40-cell braille line
/// (<see cref="Bbs757CduFrame.ToDisplayLines"/> lists them); see <see cref="Build"/>. Pure.
/// </summary>
public static class Bbs757CduRows
{
    // A label row of this many dashes and nothing else is the page's separator rule (R71).
    private const int SeparatorDashes = 20;

    /// <summary>
    /// The window's rows, in the shape the other aircraft's CDU windows share. The title is "Title: "
    /// and the page name, then three spaces and "page/pages" when the page has a counter, as the MD-11
    /// window sets it ("Title: ACT RTE 1 LEGS   1/7", "Title: NAV RADIO"). Then for each line select key
    /// its label row, indented three spaces and left out when blank, and its data row numbered "n:"
    /// ("n:" alone when blank; Ctrl+n presses the key on the left, Alt+n on the right). A label row of
    /// 20 or more dashes and nothing else is left out (R71: it is only the page's separator rule, braille
    /// noise); one with other text among the dashes stays, and so does any data row. The scratchpad ends
    /// the list: "Scratchpad: " and what it holds, "Scratchpad:" when empty. Trailing spaces are
    /// dropped, interior ones kept, so a right-hand field stays in its column in braille; no row is
    /// longer than 36 cells (24 columns and the longest prefix), inside a 40-cell braille line, except
    /// for a selection marker below. The label and data rows are read from the cells, not the lines, so
    /// a selected (inverse) option reads with "X " in front of it and the columns after it unchanged;
    /// only when no gap can pay for the marker is its row wider, by two or three cells each, and never
    /// past the 40 cells of the braille line (<see cref="Bbs757CduMarkers.Mark"/>: 37 before the
    /// prefix). The title and the scratchpad are text, and the separator test looks at the text
    /// without a marker.
    /// <para>
    /// A screen with nothing on any of its 14 lines is one <see cref="Bbs757CduRowKind.Blank"/> row that
    /// says which CDU it is (<paramref name="cdu"/> 1 the captain's, 2 the first officer's): a window that
    /// listed only empty "n:" rows would say nothing about why. A screen with something on it always
    /// lists the title row, "Title:" when the title is empty.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Bbs757CduRow> Build(Bbs757CduScreen screen, int cdu)
    {
        if (screen.Lines.All(string.IsNullOrWhiteSpace))
            return new[] { new Bbs757CduRow(Bbs757CduRowKind.Blank, 0, cdu == 1 ? "Captain CDU screen blank" : "First officer CDU screen blank") };

        var rows = new List<Bbs757CduRow> { new(Bbs757CduRowKind.Title, 0, TitleText(screen.Lines[0])) };
        for (int key = 1; key <= 6; key++)
        {
            string label = Bbs757CduMarkers.Mark(screen.Cells[key * 2 - 1]).TrimEnd();
            string data = Bbs757CduMarkers.Mark(screen.Cells[key * 2]).TrimEnd();
            if (label.Trim().Length > 0 && !IsSeparator(screen.Lines[key * 2 - 1])) rows.Add(new(Bbs757CduRowKind.Label, key, "   " + label));
            string number = key.ToString(CultureInfo.InvariantCulture);
            rows.Add(new(Bbs757CduRowKind.Value, key, data.Trim().Length > 0 ? $"{number}: {data}" : $"{number}:"));
        }
        string scratchpad = screen.Scratchpad;
        rows.Add(new(Bbs757CduRowKind.Scratchpad, 0, scratchpad.Length > 0 ? "Scratchpad: " + scratchpad : "Scratchpad:"));
        return rows;
    }

    /// <summary>"Title: " and the page name, then three spaces and "page/pages" when there is a counter.</summary>
    private static string TitleText(string titleRow)
    {
        string name = Bbs757CduTitle.PageName(titleRow);
        string text = name.Length > 0 ? "Title: " + name : "Title:";
        if (Bbs757CduTitle.Counter(titleRow) is { } counter)
            text += string.Create(CultureInfo.InvariantCulture, $"   {counter.Page}/{counter.Pages}");
        return text;
    }

    /// <summary>A label row that is only a rule: <see cref="SeparatorDashes"/> or more dashes, nothing else.</summary>
    private static bool IsSeparator(string label)
    {
        string text = label.Trim();
        return text.Length >= SeparatorDashes && text.All(c => c == '-');
    }

    /// <summary>
    /// Where the cursor goes in <paramref name="after"/> when it was on <paramref name="selected"/> in
    /// <paramref name="before"/>: the row with the same kind and line; if that row is gone (a label that
    /// went blank), the first row of the same line; else the same index, clamped. With nothing to go
    /// on (no rows before, or no row selected) the index is kept, clamped.
    /// </summary>
    public static int Restore(IReadOnlyList<Bbs757CduRow> before, int selected, IReadOnlyList<Bbs757CduRow> after)
    {
        if (after.Count == 0) return -1;
        int Clamp(int i) => Math.Clamp(i, 0, after.Count - 1);
        if (selected < 0 || selected >= before.Count) return Clamp(selected < 0 ? 0 : selected);

        var was = before[selected];
        for (int i = 0; i < after.Count; i++)
            if (after[i].Kind == was.Kind && after[i].Line == was.Line) return i;
        if (was.Line > 0)
            for (int i = 0; i < after.Count; i++)
                if (after[i].Line == was.Line) return i;
        return Clamp(selected);
    }

    /// <summary>Where a new page starts: line 1's value row, else the first row.</summary>
    public static int PageStart(IReadOnlyList<Bbs757CduRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == Bbs757CduRowKind.Value && rows[i].Line == 1) return i;
        return 0;
    }

    /// <summary>
    /// Whether a new title row is another page: the page itself (<see cref="PageIdentity"/>, ACT and MOD
    /// folded away) or its page number differs. ACT RTE 1 LEGS 1/7 → MOD RTE 1 LEGS 1/7 is the same page,
    /// and so is 1/2 → 1/3 (an inserted waypoint adding a page: the MD-11's rule, the cursor stays), and
    /// ACT ECON CLB 1/3 → MOD 250KT CLB 1/3; 1/7 → 2/7 is not.
    /// </summary>
    public static bool IsPageChange(string oldTitleRow, string newTitleRow) =>
        PageIdentity(oldTitleRow) != PageIdentity(newTitleRow)
        || Bbs757CduTitle.Counter(oldTitleRow)?.Page != Bbs757CduTitle.Counter(newTitleRow)?.Page;

    // The phase word that ends every VNAV page's title.
    private static readonly string[] VnavPhases = { "CLB", "CRZ", "DES" };

    /// <summary>
    /// <see cref="Bbs757CduTitle.Identity"/>, except that the three VNAV pages are one: their titles change
    /// with the speed mode (ACT ECON CLB → MOD 250KT CLB when a speed is entered: the owner, 2026-10-27; LRC,
    /// a new cruise altitude's CRZ CLB), so they are told apart by their page number alone (CLB 1/3, ECON CRZ
    /// 2/3, DES 3/3, read live 2026-10-27).
    /// </summary>
    private static string PageIdentity(string titleRow)
    {
        string identity = Bbs757CduTitle.Identity(titleRow);
        string last = identity[(identity.LastIndexOf(' ') + 1)..];
        return VnavPhases.Contains(last) ? "VNAV" : identity;
    }

    /// <summary>
    /// Whether the title says something new (<see cref="Bbs757CduTitle.Spoken"/> differs; a blank title
    /// never does): ACT → MOD and 1/2 → 1/3 included. The window speaks it, as every other aircraft's
    /// CDU window speaks a changed title (owner, 2026-10-27); whether the cursor moves is
    /// <see cref="IsPageChange"/>'s question.
    /// </summary>
    public static bool IsTitleChange(string oldTitleRow, string newTitleRow)
    {
        string spoken = Bbs757CduTitle.Spoken(newTitleRow);
        return spoken.Length > 0 && spoken != Bbs757CduTitle.Spoken(oldTitleRow);
    }

    /// <summary>
    /// The Ctrl+M key of "CDU Messages While Closed" (<c>BBS757Definition.AddCduMonitorRows</c>), and the
    /// entry the window's "Speak CDU messages when closed" check box adds to and removes from
    /// <c>BBS757DisabledMonitorVariables</c>. The speech comes from the tablet client's push, outside
    /// MainForm's mute wrap, so the window checks the key itself ([VAR-8]).
    /// </summary>
    public const string MessagesMonitorKey = "BBS757_CDU_MESSAGES";

    /// <summary>
    /// What to say when the captain's scratchpad went from <paramref name="oldPad"/> to
    /// <paramref name="newPad"/> while the CDU window is closed (R72): "CDU: " and the new text when the
    /// FMC put a message there ("TAKEOFF DATA UPLINK", "INVALID ENTRY"), else null. Null while keys are
    /// queued (<paramref name="keysPending"/>: the scratchpad is mid-entry), for a first reading
    /// (<paramref name="oldPad"/> null: nothing was known), for a cleared scratchpad, for the same text,
    /// and for typing: the new text grew from the old one (an entry being typed), or is the start of it
    /// (CLR or DEL took characters off). A message over typed text ("KSEA" → "INVALID ENTRY") and one on
    /// an empty scratchpad both speak. A message that happens to continue the text before it ("INVALID" →
    /// "INVALID ENTRY") reads as typing and is not said: the price of not saying every typed character.
    /// Pure.
    /// </summary>
    public static string? NewMessage(string? oldPad, string newPad, bool keysPending)
    {
        if (keysPending || oldPad == null) return null;
        string was = oldPad.Trim();
        string now = newPad.Trim();
        if (now.Length == 0 || now == was) return null;
        if (was.Length > 0 && now.StartsWith(was, StringComparison.Ordinal)) return null;
        if (was.StartsWith(now, StringComparison.Ordinal)) return null;
        return "CDU: " + now;
    }
}

/// <summary>
/// Remembers the captain's scratchpad between the hidden CDU window's pushes and decides which change
/// is a message to speak (<see cref="Bbs757CduRows.NewMessage"/>), so the window itself only forwards
/// what it sees and the typing-is-not-a-message rule can be tested with a clock (R72). Not thread-safe:
/// the window drives it on the UI thread.
/// </summary>
public sealed class Bbs757CduMessageWatch
{
    /// <summary>
    /// How long after the window's own keys a scratchpad change is still taken as their echo. The hidden
    /// client scrapes once a second, so the last key of an entry can go out between two scrapes and the
    /// first push that shows the entry then arrives with no key pending and no earlier push of the text
    /// growing, which would read as a message; two scrapes of margin keep it from being one.
    /// </summary>
    public const int SettleMs = 2000;

    private string? _old;
    private DateTime _busyUntil = DateTime.MinValue;

    /// <summary>
    /// The window just queued keys that take about <paramref name="queuedMs"/> ms to go out (those ahead
    /// of them included): they count as pending until then and <see cref="SettleMs"/> after. An earlier,
    /// longer window is never shortened.
    /// </summary>
    public void KeysQueued(DateTime now, int queuedMs) => ExtendTo(now.AddMilliseconds(Math.Max(0, queuedMs) + SettleMs));

    /// <summary>
    /// The captain's scratchpad as a push shows it: what to speak, or null. <paramref name="keysPending"/>
    /// is whether the client still holds keys. It counts only while the window's own captain keys are in
    /// their settle window (<see cref="KeysQueued"/>), and then it extends that window to
    /// <see cref="SettleMs"/> after this push (the keys ran slower than the window estimated). The client has
    /// one queue for both CDUs: the first officer's keys (the page service's reads, the window's keys on that
    /// CDU) keep it above zero too, but they never touch the captain's scratchpad, so they must not hold a
    /// message back (it would be recorded as the last text and never spoken) or add deafness. The text is
    /// remembered whether or not it is spoken, and a message muted in Ctrl+M (<paramref name="muted"/>) is
    /// not spoken later either.
    /// </summary>
    public string? OnScreen(string pad, bool keysPending, DateTime now, bool muted)
    {
        bool ownKeys = keysPending && now < _busyUntil;
        if (ownKeys) ExtendTo(now.AddMilliseconds(SettleMs));
        string? say = Bbs757CduRows.NewMessage(_old, pad, now < _busyUntil);
        _old = pad;
        return muted ? null : say;
    }

    /// <summary>
    /// Remembers <paramref name="pad"/> without speaking it: what the pilot saw in the open window, or
    /// null when nothing is known (the screen was lost), so the next push only seeds. Keys already queued
    /// still count as pending.
    /// </summary>
    public void Seed(string? pad) => _old = pad;

    private void ExtendTo(DateTime until)
    {
        if (until > _busyUntil) _busyUntil = until;
    }
}
