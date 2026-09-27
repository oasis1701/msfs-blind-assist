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
/// Typed entries are held (<see cref="HoldForTyping"/>) so a half-typed scratchpad is never read
/// back: over the Coherent transport a frame arrives only every 250 ms plus the eval round trip,
/// longer than the window's old 300 ms debounce could bridge.
/// </summary>
internal sealed class FbwMcduReadBack
{
    public const string ClearedText = "Scratchpad cleared";

    /// <summary>The window's read-back tick. A change is spoken once it has read the same on two
    /// consecutive ticks (150-300 ms, the old debounce's 300 ms), so a one-tick flicker is not.</summary>
    public const int TickMs = 150;

    /// <summary>How long after each typed key the read-back holds: FBW's keypad applies a key
    /// 150-200 ms after it arrives, and the window may see the change only on the next 250 ms
    /// Coherent read.</summary>
    public const int TypingSettleMs = 600;

    private readonly CduScratchpadAnnouncer _scratchpad = new(ClearedText, stablePolls: 2);
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

    /// <summary>A key is being typed: hold the read-back until it has landed.</summary>
    public void HoldForTyping(DateTime nowUtc) => _scratchpad.SuppressUntil = nowUtc.AddMilliseconds(TypingSettleMs);
}
