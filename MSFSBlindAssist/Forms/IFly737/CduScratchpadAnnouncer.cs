namespace MSFSBlindAssist.Forms.IFly737;

/// <summary>Poll-driven CDU scratchpad read-back (PR #163 review, major 1).
/// Runs on EVERY poll, independent of the form's screen-change hash gate: the
/// typing suppression window can swallow the hash change that carried the final
/// scratchpad state, and a hash-gated announce then never fires (and leaks the
/// stale text minutes later). While suppressed the baseline is deliberately NOT
/// advanced, so the first poll after expiry still sees the difference and reads
/// the settled entry back — the PMDG form's polling pattern, factored out
/// as pure logic so it is testable.</summary>
internal sealed class CduScratchpadAnnouncer
{
    private string _last = "";
    private bool _first = true;

    /// <summary>Announcements are held until this UTC time (typing/CLR bursts).</summary>
    public DateTime SuppressUntil { get; set; }

    /// <summary>Feed the current scratchpad text each poll; returns the text to
    /// announce ("Cleared" for an emptied scratchpad), or null for silence.</summary>
    public string? OnPoll(string scratchpad, DateTime nowUtc)
    {
        if (_first)
        {
            _first = false;
            _last = scratchpad;
            return null;
        }
        if (scratchpad == _last) return null;
        if (nowUtc < SuppressUntil) return null; // keep _last stale — re-checked next poll
        _last = scratchpad;
        return scratchpad.Length > 0 ? scratchpad : "Cleared";
    }

    /// <summary>Silent re-seed (form reopen — parity with the old _firstRender behavior).</summary>
    public void Reset()
    {
        _first = true;
        _last = "";
    }
}
