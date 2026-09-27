namespace MSFSBlindAssist.Forms.FlyByWireA320;

/// <summary>
/// What the FlyByWire A32NX MCDU window says about the screen, as pure logic; the window's
/// timer and visibility feed it. Two rules on top of the shared <see cref="CduScratchpadAnnouncer"/>
/// (the iFly and MD-11 CDU windows' read-back):
///
///  • A page title is spoken only while the window is OPEN, once per page. A page reached
///    while the window was closed is spoken when the window next shows it.
///  • While the window is CLOSED only an FMS message is spoken — a scratchpad that gains text,
///    such as DEST EFOB BELOW MIN (nobody types into a closed window). "Scratchpad cleared" is
///    for an open window only. The window keeps reading the screen while closed precisely so
///    these messages still reach the pilot, as they did when SimBridge was the only transport.
///
/// A changed scratchpad is spoken once it has shown for <see cref="StableMs"/>, and typed entries
/// are held (<see cref="HoldForTyping"/>) while their keys land, so neither a value the window saw
/// in a single read nor a half-typed entry is read back. Over the Coherent transport a frame
/// arrives only every 250 ms plus the eval round trip, which the window's old 300 ms debounce
/// could not bridge.
/// </summary>
internal sealed class FbwMcduReadBack
{
    public const string ClearedText = "Scratchpad cleared";

    /// <summary>The window's read-back tick. It re-samples the LAST frame the window received, so a
    /// count of ticks measures how long a value has shown, not how many reads saw it.</summary>
    public const int TickMs = 100;

    /// <summary>How long a changed scratchpad must show before it is spoken: longer than one Coherent
    /// read (the 250 ms poll plus an eval round trip of up to 150 ms), so a value the window saw in a
    /// single read is never spoken — a redraw caught mid-way, or the 150 ms blank FBW leaves before
    /// showing the next queued message.</summary>
    public const int StableMs = 400;

    /// <summary>How long after each typed key the read-back holds: FBW's keypad applies a key
    /// 150-200 ms after it arrives, and the window may see the change only on the next 250 ms
    /// Coherent read.</summary>
    public const int TypingSettleMs = 600;

    private readonly CduScratchpadAnnouncer _scratchpad = new(ClearedText, stablePolls: StableMs / TickMs + 1);
    private string _lastTitle = "";

    public FbwMcduReadBack()
    {
        // Seeded EMPTY, so a message already showing when the window first opens is read (the
        // window's old debounce started from "" too).
        _scratchpad.OnPoll("", DateTime.MinValue);
    }

    /// <summary>The page title to speak now, or null.</summary>
    public string? OnTitle(string title, bool windowVisible)
    {
        string trimmed = title.Trim();
        if (!windowVisible || trimmed.Length == 0 || trimmed == _lastTitle) { return null; }
        _lastTitle = trimmed;
        return trimmed;
    }

    /// <summary>Feed the current scratchpad on every window tick; returns what to speak, or null.</summary>
    public string? OnScratchpadTick(string scratchpad, bool windowVisible, DateTime nowUtc)
    {
        string? say = _scratchpad.OnPoll(scratchpad, nowUtc);
        if (say == null) { return null; }
        // Cleared while closed: the baseline has moved to empty (so the next message is still
        // spoken), but there is nothing for a pilot who is not looking at the MCDU to hear.
        if (!windowVisible && scratchpad.Length == 0) { return null; }
        return say;
    }

    /// <summary>A key is being typed: hold the read-back for <see cref="TypingSettleMs"/>. A later
    /// hold extends an earlier one — the window holds before each key is sent and again once it
    /// has been delivered.</summary>
    public void HoldForTyping(DateTime nowUtc) => _scratchpad.SuppressUntil = nowUtc.AddMilliseconds(TypingSettleMs);
}
