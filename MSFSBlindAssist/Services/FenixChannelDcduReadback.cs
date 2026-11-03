namespace MSFSBlindAssist.Services;

/// <summary>
/// Gives the pilot what the DCDU changed after the pilot's own key press, once. The window's list shows the result of most
/// presses where the cursor is, but some results are on lines the pilot is not on: the status in the header after an answer
/// (WILCO) and "NO MORE MSG" on the first info line after a message key with nothing to step to. A screen reader cannot say
/// what changed away from its cursor, so the read-back does ([CORE-7]). It reads no clock and speaks nothing itself: the
/// announcer host's sampler hands it each tick's reading of the DCDU and it answers with the text to speak, or null.
/// </summary>
/// <remarks>
/// <para>A press. <see cref="NotePress"/> is called for every key the window sends, before it is sent, and takes no clock:
/// the first sample after it is the "before" reading, and a press lands at least 200 ms after it (the sampler ticks every
/// 100 ms), so that sample is always before the key's effect. A new press replaces a pending one and takes its own before
/// reading.</para>
/// <para>What is watched. A soft key watches the status (the header's right half) and the first info line; any other key
/// (page, message or print) watches the first info line only. At the first change to something with words, the new text is
/// given in plain words (<see cref="FenixChannelDcdu.Spoken"/>: no codes, no color names, runs of spaces made one), the
/// status first and the first info line second, joined by ", " when both changed in the same sample, and the press is
/// cleared: it is said once. The key's name is never said. A change to nothing says nothing, and the watched line then
/// counts as blank, so the same words coming back are a change again. A change of spacing alone is no change.</para>
/// <para>When a press is given up, silently. <see cref="WindowMs"/> after the before reading (the window is over at exactly
/// that, as the announcer's quiet is), and on a screen that is not ok at any sample: a display that went off or a problem
/// ends the press, and what comes back is not its result. With no press pending a sample says nothing, so a status that
/// changes by itself is never read back.</para>
/// <para>It is the pilot's own press, so it is not a background change: the Ctrl+M row for the DCDU does not mute it, and
/// the variant's mute is not its business either (a Fenix without a DCDU refuses the press before anything changes).
/// Called on the UI thread only, by the host's timer, and <see cref="NotePress"/> from the definition's press on the same
/// thread, so it takes no lock. The text is the DCDU's own words, so the PC's culture changes none of it.</para>
/// </remarks>
internal sealed class FenixChannelDcduReadback
{
    /// <summary>How long after the before reading a press can still be read back, in milliseconds.</summary>
    public const long WindowMs = 3000;

    // A press is pending, whether its key watches the status as well as the first info line, whether its before reading has
    // been taken, and when.
    private bool _pending;
    private bool _watchesStatus;
    private bool _beforeTaken;
    private long _beforeMs;

    // The watched lines as of the before reading, kept as plain words; moved on to a blank when a line goes blank.
    private string _status = "";
    private string _info = "";

    /// <summary>
    /// Notes that the pilot pressed <paramref name="key"/>, replacing a press still pending. Call it before the press is
    /// sent, so that the sample cannot come first.
    /// </summary>
    public void NotePress(FenixDcduKey key)
    {
        _pending = true;
        _watchesStatus = FenixChannelDcduKeys.IsSoftKey(key);
        _beforeTaken = false;
    }

    /// <summary>
    /// One reading of the DCDU. Returns what the pending press changed, in plain words, the first time a watched line
    /// changes to something with words; else null (see the class remarks).
    /// </summary>
    /// <param name="screen">The DCDU as the decoder gave it; a section that is not ok ends a pending press.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every sample.</param>
    public string? Sample(FenixChannelSection<FenixDcduScreen> screen, long nowMs)
    {
        if (!_pending) return null;

        if (!screen.IsOk)
        {
            _pending = false;
            return null;
        }

        FenixDcduScreen current = screen.Value!;
        string status = FenixChannelDcdu.Spoken(current.HeaderRight);
        string info = FenixChannelDcdu.Spoken(current.Info[0]);

        // The first sample after the press is the state to compare with, whatever it shows.
        if (!_beforeTaken)
        {
            _beforeTaken = true;
            _beforeMs = nowMs;
            _status = status;
            _info = info;
            return null;
        }

        if (nowMs - _beforeMs >= WindowMs)
        {
            _pending = false;
            return null;
        }

        var said = new List<string>(2);

        if (_watchesStatus && status != _status)
        {
            if (status.Length > 0) said.Add(status);
            _status = status;
        }

        if (info != _info)
        {
            if (info.Length > 0) said.Add(info);
            _info = info;
        }

        if (said.Count == 0) return null;

        _pending = false;
        return string.Join(", ", said);
    }
}
