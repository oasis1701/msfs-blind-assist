namespace MSFSBlindAssist.Forms;

/// <summary>Poll-driven CDU scratchpad read-back (PR #163 review, major 1), shared by the iFly
/// 737 CDU window and the TFDi MD-11 MCDU window.
/// Runs on EVERY poll, independent of the form's screen-change gate: the
/// typing suppression window can swallow the change that carried the final
/// scratchpad state, and a change-gated announce then never fires (and leaks the
/// stale text minutes later). While suppressed the baseline is deliberately NOT
/// advanced, so the first poll after expiry still sees the difference and reads
/// the settled entry back — the PMDG form's polling pattern, factored out
/// as pure logic so it is testable.</summary>
internal sealed class CduScratchpadAnnouncer
{
    private readonly string _clearedText;
    private readonly int _stablePolls;
    private string _last = "";
    private bool _first = true;
    private string? _candidate;
    private int _candidatePolls;

    /// <param name="clearedText">What an emptied scratchpad is announced as: "Cleared" on the
    /// iFly (the default — unchanged since it shipped), "Scratchpad cleared" on the MD-11.</param>
    /// <param name="stablePolls">How many consecutive polls a changed scratchpad must read the same
    /// before it is announced. 1 (the default — the iFly's behaviour since it shipped) announces the
    /// first differing poll; the MD-11 uses 2, so a one-poll redraw flicker (a blank frame between two
    /// identical ones) is never spoken — the job its old 300 ms debounce did.</param>
    public CduScratchpadAnnouncer(string clearedText = "Cleared", int stablePolls = 1)
    {
        _clearedText = clearedText;
        _stablePolls = Math.Max(1, stablePolls);
    }

    /// <summary>Announcements are held until this UTC time (typing/CLR bursts).</summary>
    public DateTime SuppressUntil { get; set; }

    /// <summary>Feed the current scratchpad text each poll; returns the text to
    /// announce (the cleared wording for an emptied scratchpad), or null for silence.</summary>
    public string? OnPoll(string scratchpad, DateTime nowUtc)
    {
        if (_first)
        {
            _first = false;
            _last = scratchpad;
            return null;
        }
        if (scratchpad == _last || nowUtc < SuppressUntil)
        {
            // Unchanged, or held (keep _last stale — re-checked next poll): no candidate survives.
            _candidate = null;
            _candidatePolls = 0;
            return null;
        }
        if (scratchpad == _candidate) _candidatePolls++;
        else { _candidate = scratchpad; _candidatePolls = 1; }
        if (_candidatePolls < _stablePolls) return null;
        _last = scratchpad;
        _candidate = null;
        _candidatePolls = 0;
        return scratchpad.Length > 0 ? scratchpad : _clearedText;
    }

    /// <summary>Silent re-seed (form reopen — parity with the old _firstRender behavior).</summary>
    public void Reset()
    {
        _first = true;
        _last = "";
        _candidate = null;
        _candidatePolls = 0;
    }
}
