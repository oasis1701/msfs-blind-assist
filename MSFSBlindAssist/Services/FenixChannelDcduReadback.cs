namespace MSFSBlindAssist.Services;

/// <summary>
/// Gives the pilot what the DCDU changed away from the row under the cursor: every new first info line, and the status in the
/// header after the pilot's own soft key. The window's list shows most results where the cursor is, but some are on lines the
/// pilot is not on: the status (WILCO) and the first info line (NO MORE MSG after a message key with nothing to step to, SENDING
/// and then RECEIVD BY ATC after SEND). A screen reader cannot say what changed away from its cursor, so the read-back does
/// ([CORE-7]). It reads no clock and speaks nothing itself: the announcer host's sampler hands it each tick's reading of the
/// DCDU and it answers with the text to speak, or null.
/// </summary>
/// <remarks>
/// <para>The first info line. Any change to a line with words is said once as it appears, whatever caused it, in plain words
/// (<see cref="FenixChannelDcdu.Spoken"/>: no codes, no color names, runs of spaces made one): the pilot hears SENDING, then
/// RECEIVD BY ATC. A line that goes blank says nothing, and the same line coming back after a blank is said again. A change of
/// spacing alone is no change. The first ok reading after the start, a gap or a screen that is not ok is a silent baseline: a
/// line already showing is not news, whether it was up at the start or came during the gap.</para>
/// <para>The mute. The line follows the "DCDU messages" mute (the caller's <c>muted</c>), except inside <see cref="WindowMs"/>
/// after the pilot's own press, so that a key is never silent. A line that appeared while muted is tracked all the same, so
/// unmuting replays nothing.</para>
/// <para>A press. <see cref="NotePress"/> is called for every key the window sends, before it is sent, and takes no clock: the
/// first sample after it is the "before" reading, and a press lands at least 200 ms after it (the sampler ticks every 100 ms),
/// so that sample is always before the key's effect. The window runs from that reading and is over at exactly
/// <see cref="WindowMs"/> (as the announcer's quiet is). A new press replaces a pending one and takes its own before reading.</para>
/// <para>The status. A soft key watches the header's right half: at the first change to something with words inside the window
/// the new text is said, once, and the watch is over (the key's name is never said). After WILCO that is WILCO, and after CLOSE
/// with a message behind it OPEN; on SEND the status word stays WILCO, so only the info line speaks. A change to nothing says
/// nothing, and the status then counts as blank, so the same words coming back are a change again. Any other key (page, message
/// or print) watches no status. With no press pending a status that changes by itself is never read back. It is the pilot's own
/// press, so the mute does not silence it, and a screen that is not ok ends the watch: a display that went off or a problem is
/// not the press's result.</para>
/// <para>Both in one sample are said together, the status first, joined by ", ". Called on the UI thread only, by the host's
/// timer, and <see cref="NotePress"/> from the definition's press on the same thread, so it takes no lock. The text is the
/// DCDU's own words, so the PC's culture changes none of it.</para>
/// </remarks>
internal sealed class FenixChannelDcduReadback
{
    /// <summary>
    /// How long after the before reading a press can still be read back, and a muted first info line still be said, in
    /// milliseconds.
    /// </summary>
    public const long WindowMs = 3000;

    // A press is noted and its before reading is not taken yet, and whether its key watches the status.
    private bool _pressNoted;
    private bool _watchesStatus;

    // The status of the press under watch, as of the before reading or its last change, kept as plain words (moved on to a
    // blank when it goes blank); watched until it has been said or the window is over.
    private bool _statusWatched;
    private string _status = "";

    // The end of the window of the last press, from its before reading; over at exactly that.
    private long _windowEndMs = long.MinValue;

    // Whether the first info line has a baseline, and the line as of the last ok sample, as plain words (a blank is "").
    private bool _baselined;
    private string _info = "";

    /// <summary>
    /// Notes that the pilot pressed <paramref name="key"/>, replacing a press still pending. Call it before the press is
    /// sent, so that the sample cannot come first.
    /// </summary>
    public void NotePress(FenixDcduKey key)
    {
        _pressNoted = true;
        _watchesStatus = FenixChannelDcduKeys.IsSoftKey(key);
        _statusWatched = false;
    }

    /// <summary>
    /// One reading of the DCDU. Returns what changed, in plain words: the status after the pilot's soft key, and a first info
    /// line that has just changed to something with words; else null (see the class remarks).
    /// </summary>
    /// <param name="screen">The DCDU as the decoder gave it; a section that is not ok ends a pending press and the baseline.</param>
    /// <param name="muted">Whether the DCDU's announcements are muted now.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every sample.</param>
    public string? Sample(FenixChannelSection<FenixDcduScreen> screen, bool muted, long nowMs)
    {
        if (!screen.IsOk)
        {
            _pressNoted = false;
            _statusWatched = false;
            _windowEndMs = long.MinValue;
            _baselined = false;
            return null;
        }

        FenixDcduScreen current = screen.Value!;
        string status = FenixChannelDcdu.Spoken(current.HeaderRight);
        string info = FenixChannelDcdu.Spoken(current.Info[0]);

        // Whether this sample is inside the window of a press made before it: the press this sample is the before reading of
        // opens its own window, after it.
        bool inWindow = nowMs < _windowEndMs;

        // The first sample after the press is the state to compare with, whatever it shows.
        bool beforeReading = _pressNoted;
        if (_pressNoted)
        {
            _pressNoted = false;
            _windowEndMs = nowMs + WindowMs;
            _status = status;
            _statusWatched = _watchesStatus;
        }

        if (!_baselined)
        {
            _baselined = true;
            _info = info;
            return null;
        }

        var said = new List<string>(2);

        if (_statusWatched && !beforeReading)
        {
            if (nowMs >= _windowEndMs)
            {
                _statusWatched = false;
            }
            else if (status != _status)
            {
                _status = status;
                if (status.Length > 0)
                {
                    said.Add(status);
                    _statusWatched = false;
                }
            }
        }

        if (info != _info)
        {
            _info = info;
            if (info.Length > 0 && (!muted || inWindow)) said.Add(info);
        }

        return said.Count == 0 ? null : string.Join(", ", said);
    }
}
