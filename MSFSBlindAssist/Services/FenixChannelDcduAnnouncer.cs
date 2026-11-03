namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when a message on the captain's DCDU is new, and gives the text to say about it. The announcer host's
/// sampler hands it each tick's reading of the DCDU and it answers with the text to speak, or null. It speaks nothing
/// itself and reads no clock: every time it is given is the caller's, in milliseconds
/// (<see cref="Environment.TickCount64"/> in the app).
/// </summary>
/// <remarks>
/// <para>What is new. A message is its <see cref="IdentityOf">identity</see>: the text of the header's left half and of the
/// first message line that has words. The status in the header's right half is no part of it (the DCDU walks a message
/// through OPEN, WILCO, SENT and the like), and neither are the other message lines (paging moves them). A DCDU with no
/// header text has no identity and draws no message.</para>
/// <para>The baseline. The first ok screen after the start is only remembered, and so is the first ok screen after a
/// screen that was not ok (a display-off gap, an untrusted display) or after the channel reconnected: a message already on
/// the DCDU then is not news, and a message that came while nothing could be read is not either. The screen that shows
/// the problem or the new connection is not itself the baseline: the next ok screen is. What was remembered before the
/// baseline is forgotten with it.</para>
/// <para>After the baseline an identity counts once it has stayed on the screen <see cref="SettleMs"/> without a break, the
/// settle the FMA announcements use, so a screen that is still being drawn is not called. When it counts it is
/// remembered, up to <see cref="SeenCapacity"/> of them (the oldest dropped), and an identity already remembered never
/// counts again: stepping back to an earlier message is silent. Remembering does not depend on being heard, so a
/// message that was not called is never called later.</para>
/// <para>When a counted identity is not called. The DCDU is muted: a message that was on the screen while it was muted at
/// any sample of its hold is not called, and unmuting replays nothing. Or the pilot's own page or message key
/// (<see cref="NoteNavigation"/>) was pressed within <see cref="NavigationQuietMs"/>: that key's result, a new first line
/// when the message pages or the next message on the screen, is heard in the list, never as a call. The quiet is the
/// cabin window's own-press rule (<see cref="FenixCabinOwnPresses"/>), counted here from the first sample after the key
/// because the key takes no clock; it is over at exactly <see cref="NavigationQuietMs"/> after that sample.</para>
/// <para>What is said. <see cref="CallText"/>: "DCDU: " and the header's left half spoken, then the first message line
/// spoken. Never the status.</para>
/// <para>Threads. Called on the UI thread only, by the host's timer, and <see cref="NoteNavigation"/> from the window's key
/// handler on the same thread, so the announcer takes no lock. The text is the DCDU's own words and colors, so the PC's
/// culture changes none of it.</para>
/// </remarks>
internal sealed class FenixChannelDcduAnnouncer
{
    /// <summary>How long a new identity must stay on the screen before it counts, in milliseconds (the FMA's settle).</summary>
    public const long SettleMs = 300;

    /// <summary>
    /// How long after the first sample that follows <see cref="NoteNavigation"/> a counted identity is not called, in
    /// milliseconds.
    /// </summary>
    public const long NavigationQuietMs = 3000;

    /// <summary>How many identities are remembered since the baseline; past it the oldest is dropped.</summary>
    public const int SeenCapacity = 64;

    // Between the two halves of an identity: a line break, which no line of the display holds.
    private const char IdentitySeparator = '\n';

    // The identities remembered since the baseline: the set for the lookup, the queue for which is oldest.
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();

    // The connection of the last sample, whether or not its screen was ok; null before the first.
    private int? _lastConnection;

    // Whether the baseline has been taken: false at the start and after a problem or a reconnect.
    private bool _baselined;

    // The identity on the screen that has not yet counted, since when it has been there, and whether the DCDU was muted at
    // any sample of that stretch.
    private string? _candidate;
    private long _candidateSinceMs;
    private bool _candidateMuted;

    // NoteNavigation has been called since the last sample, and the end of the quiet that sample began.
    private bool _navigationPending;
    private long _quietUntilMs = long.MinValue;

    /// <summary>
    /// Notes that the pilot pressed one of the window's page or message keys. The first sample after it begins the quiet
    /// of <see cref="NavigationQuietMs"/>, and a later key before that sample changes nothing more. Call it before the
    /// key is sent, so that the sample cannot come first.
    /// </summary>
    public void NoteNavigation() => _navigationPending = true;

    /// <summary>
    /// One reading of the DCDU. Returns the text to speak when the message on the screen has just counted as new, else
    /// null (see the class remarks).
    /// </summary>
    /// <param name="screen">The DCDU as the decoder gave it; a section that is not ok says nothing and ends the baseline.</param>
    /// <param name="connection">Which connection of the channel the screen came from; another than the last sample's
    /// is a reconnect.</param>
    /// <param name="muted">Whether the DCDU's announcements are muted now.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every sample.</param>
    public string? Sample(FenixChannelSection<FenixDcduScreen> screen, int connection, bool muted, long nowMs)
    {
        if (_navigationPending)
        {
            _navigationPending = false;
            _quietUntilMs = nowMs + NavigationQuietMs;
        }

        bool reconnected = _lastConnection is { } last && last != connection;
        _lastConnection = connection;

        if (!screen.IsOk || reconnected)
        {
            _baselined = false;
            _candidate = null;
            return null;
        }

        string? identity = IdentityOf(screen.Value!);

        if (!_baselined)
        {
            _baselined = true;
            _seen.Clear();
            _seenOrder.Clear();
            if (identity is not null) Remember(identity);
            return null;
        }

        // Nothing is drawn, or the message is one already counted: no hold to keep.
        if (identity is null || _seen.Contains(identity))
        {
            _candidate = null;
            return null;
        }

        if (identity != _candidate)
        {
            _candidate = identity;
            _candidateSinceMs = nowMs;
            _candidateMuted = muted;
            return null;
        }

        _candidateMuted |= muted;
        if (nowMs - _candidateSinceMs < SettleMs) return null;

        // It counts: remembered whether or not it is called.
        Remember(identity);
        _candidate = null;

        if (_candidateMuted || nowMs < _quietUntilMs) return null;

        return CallText(screen.Value!);
    }

    /// <summary>
    /// What makes a message the same message: the header's left half and the first message line that has words, each
    /// parsed (its colors and codes are not part of it) and trimmed, with a line break between. Null while the header's
    /// left half is blank. A header with no message line is an identity with an empty second half.
    /// </summary>
    public static string? IdentityOf(FenixDcduScreen screen)
    {
        string header = TextOf(screen.HeaderLeft);
        if (header.Length == 0) return null;

        string first = screen.Message.Select(TextOf).FirstOrDefault(text => text.Length > 0) ?? "";
        return header + IdentitySeparator + first;
    }

    /// <summary>
    /// What to say about a new message: "DCDU: " and the header's left half as <see cref="FenixChannelSpeech"/> speaks it,
    /// then ", " and the first message line as <see cref="FenixChannelDcduRows.FirstMessageLine"/> speaks it ("DCDU: 1426Z
    /// FROM EDGG, white, CLIMB TO, cyan"); the last part is left out when no message line has words. The status, the
    /// right half of the header, is not said (a new message's status adds nothing). Null when the header's left half has
    /// no words.
    /// </summary>
    public static string? CallText(FenixDcduScreen screen)
    {
        if (FenixChannelSpeech.Line(screen.HeaderLeft) is not { } header) return null;

        string call = "DCDU: " + header;
        return FenixChannelDcduRows.FirstMessageLine(screen) is { } message ? call + ", " + message : call;
    }

    // The line's own text, with the codes read out of it and the ends trimmed.
    private static string TextOf(string coded) => FenixChannelText.Parse(coded).JoinedText.Trim();

    private void Remember(string identity)
    {
        if (!_seen.Add(identity)) return;

        _seenOrder.Enqueue(identity);
        if (_seenOrder.Count > SeenCapacity) _seen.Remove(_seenOrder.Dequeue());
    }
}
