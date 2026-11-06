using System.Globalization;

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
/// baseline is forgotten with it, and the total of messages (below) is taken afresh.</para>
/// <para>After the baseline an identity counts once it has stayed on the screen <see cref="SettleMs"/> without a break, the
/// settle the FMA announcements use (<see cref="FenixChannelSettle{T}"/>, which the total's hold below uses too), so a
/// screen that is still being drawn is not called. When it counts it is
/// remembered, up to <see cref="SeenCapacity"/> of them (the oldest dropped), and an identity already remembered never
/// counts again: stepping back to an earlier message is silent. Remembering does not depend on being heard, so a
/// message that was not called is never called later.</para>
/// <para>When a counted identity is not called. The DCDU is muted: a message that was on the screen while it was muted at
/// any sample of its hold is not called, and unmuting replays nothing. Or any key the pilot pressed in the window
/// (<see cref="NoteOwnPress"/>: a page or message key, a soft key, print) was pressed within <see cref="NavigationQuietMs"/>:
/// that key's result, a new first line when the message pages, the next message on the screen, or the message drawn again
/// once it is answered (SEND re-wraps it with a new first line, which was heard as a false "DCDU: 2124Z FROM EDDF, CLRD TO
/// EGSS. OBOK2E" on 2026-11-03), is heard in the list, never as a call. The quiet is the cabin window's own-press rule
/// (<see cref="FenixCabinOwnPresses"/>), counted here from the first sample after the key because the key takes no clock;
/// it is over at exactly <see cref="NavigationQuietMs"/> after that sample.</para>
/// <para>A message waiting behind another. Two messages can arrive and only the first reaches the screen, so its identity
/// never shows the second. The total is what the screen's counter says (<see cref="TotalOf"/>), and a total that rises and
/// holds <see cref="SettleMs"/> to two or more is a message that came: it is called "DCDU: 2 messages". The total that
/// has held is remembered whether or not it was heard, and it falls silently: a message closed says nothing, and a rise
/// after a fall speaks again. A total that moves for less than the settle (a screen being redrawn) moves nothing.
/// Stepping or paging changes no total. A rise is a real message and not the pilot's reading, so the quiet after the
/// pilot's own key does not silence it; the mute does, and unmuting replays nothing. When a new message's call and a rise
/// of the total to two or more fall due in the same sample, one call says both: "DCDU: 2021Z FROM EDDF, DEPART REQUEST
/// STATUS ., 2 messages".</para>
/// <para>What is said. <see cref="CallText"/>: "DCDU: " and the header's left half, then the first message line, as plain
/// words with no color named (owner's ruling, 2026-11-03: the DCDU speaks none, as the MCDU does) and runs of spaces made
/// one. Never the status.</para>
/// <para>Threads. Called on the UI thread only, by the host's timer, and <see cref="NoteOwnPress"/> from the window's key
/// handler on the same thread, so the announcer takes no lock. The text is the DCDU's own words, so the PC's culture
/// changes none of it, and the only figure written, the count, is formatted with the invariant culture.</para>
/// </remarks>
internal sealed class FenixChannelDcduAnnouncer
{
    /// <summary>How long a new identity must stay on the screen before it counts, in milliseconds (the FMA's settle).</summary>
    public const long SettleMs = 300;

    /// <summary>
    /// How long after the first sample that follows <see cref="NoteOwnPress"/> a counted identity is not called, in
    /// milliseconds. The name is from when only the page and message keys began it.
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

    // The hold of the identity on the screen that has not yet counted, which also notes whether the DCDU was muted at any
    // sample of it.
    private readonly FenixChannelSettle<string> _identitySettle = new(SettleMs, StringComparer.Ordinal);

    // The total that has held the settle (taken at the baseline), and the hold of a total on the screen that has not yet.
    private int _total;
    private readonly FenixChannelSettle<int> _totalSettle = new(SettleMs);

    // NoteOwnPress has been called since the last sample, and the end of the quiet that sample began.
    private bool _ownPressPending;
    private long _quietUntilMs = long.MinValue;

    /// <summary>
    /// Notes that the pilot pressed one of the window's keys, any of the nine: what it brings onto the screen is the pilot's
    /// own reading, not news. The first sample after it begins the quiet of <see cref="NavigationQuietMs"/>, and a later key
    /// before that sample changes nothing more. Call it before the key is sent, so that the sample cannot come first.
    /// </summary>
    public void NoteOwnPress() => _ownPressPending = true;

    /// <summary>
    /// One reading of the DCDU. Returns the text to speak when the message on the screen has just counted as new or the
    /// total of messages has just risen, else null (see the class remarks).
    /// </summary>
    /// <param name="screen">The DCDU as the decoder gave it; a section that is not ok says nothing and ends the baseline.</param>
    /// <param name="connection">Which connection of the channel the screen came from; another than the last sample's
    /// is a reconnect.</param>
    /// <param name="muted">Whether the DCDU's announcements are muted now.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every sample.</param>
    public string? Sample(FenixChannelSection<FenixDcduScreen> screen, int connection, bool muted, long nowMs)
    {
        if (_ownPressPending)
        {
            _ownPressPending = false;
            _quietUntilMs = nowMs + NavigationQuietMs;
        }

        bool reconnected = _lastConnection is { } last && last != connection;
        _lastConnection = connection;

        if (!screen.IsOk || reconnected)
        {
            _baselined = false;
            _identitySettle.Clear();
            _totalSettle.Clear();
            return null;
        }

        FenixDcduScreen current = screen.Value!;
        string? identity = IdentityOf(current);
        int total = TotalOf(current);

        if (!_baselined)
        {
            _baselined = true;
            _seen.Clear();
            _seenOrder.Clear();
            if (identity is not null) Remember(identity);
            _total = total;
            return null;
        }

        string? call = SampleIdentity(current, identity, muted, nowMs);
        int? messages = SampleTotal(total, muted, nowMs);

        if (messages is not { } count) return call;

        string waiting = FormattableString.Invariant($"{count} messages");
        return call is null ? "DCDU: " + waiting : call + ", " + waiting;
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
    /// What to say about a new message: "DCDU: " and the header's left half as <see cref="FenixChannelDcdu.Spoken"/> gives
    /// it, then ", " and the first message line as <see cref="FenixChannelDcduRows.FirstMessageLine"/> gives it ("DCDU: 1426Z
    /// FROM EDGG, CLIMB TO"); the last part is left out when no message line has words. No color is named, and the status,
    /// the right half of the header, is not said (a new message's status adds nothing). Null when the header's left half
    /// has no words.
    /// </summary>
    public static string? CallText(FenixDcduScreen screen)
    {
        string header = FenixChannelDcdu.Spoken(screen.HeaderLeft);
        if (header.Length == 0) return null;

        string call = "DCDU: " + header;
        return FenixChannelDcduRows.FirstMessageLine(screen) is { } message ? call + ", " + message : call;
    }

    /// <summary>
    /// How many messages the DCDU holds: the number after the slash in the first token of the third info line when the
    /// second info line starts with "MSG" ("MSG        PGE" over "1/2        1/2" is two: message 1 of 2, page 1 of 2).
    /// Both lines are read from their first column: a second line that starts with white space, or a third whose first
    /// token is not digits, a slash and digits, is no counter. With no counter (no "MSG" at the first column, a token
    /// that is not that shape, or a total that is not a whole number of one or more) the total is one while the screen has
    /// an <see cref="IdentityOf">identity</see> and none otherwise. The figure is read with the invariant culture, from the
    /// DCDU's own text.
    /// </summary>
    public static int TotalOf(FenixDcduScreen screen)
    {
        if (CounterTotal(screen) is { } counted) return counted;

        return IdentityOf(screen) is null ? 0 : 1;
    }

    // The identity half of a sample, after the baseline: the call for the message on the screen when it has just counted as
    // new and may be heard, else null.
    private string? SampleIdentity(FenixDcduScreen screen, string? identity, bool muted, long nowMs)
    {
        // Nothing is drawn, or the message is one already counted: no hold to keep.
        if (identity is null || _seen.Contains(identity))
        {
            _identitySettle.Clear();
            return null;
        }

        if (!_identitySettle.Held(identity, nowMs, muted)) return null;

        // It counts: remembered whether or not it is called.
        Remember(identity);

        if (_identitySettle.HeldMuted || nowMs < _quietUntilMs) return null;

        return CallText(screen);
    }

    // The total half of a sample, after the baseline: the total to call when it has just risen, held the settle, to two or
    // more and may be heard, else null. A total that differs from the settled one begins a hold of its own; one that holds
    // is the settled total from then on, heard or not, so that a fall is tracked silently and a rise after it speaks. The
    // quiet after the pilot's own key does not apply: a rise is a message that came, not the pilot's reading.
    private int? SampleTotal(int total, bool muted, long nowMs)
    {
        if (total == _total)
        {
            _totalSettle.Clear();
            return null;
        }

        if (!_totalSettle.Held(total, nowMs, muted)) return null;

        int previous = _total;
        _total = total;

        // A rise to one is a message arriving on an empty DCDU, which its call announces; only two or more are a count.
        return total > previous && total > 1 && !_totalSettle.HeldMuted ? total : null;
    }

    // The counter's total, or null when the screen has no counter that reads. The Fenix draws the counter at the first column of
    // both lines: the second starts with "MSG" and the third with the message counter, digits, a slash, digits ("1/2"). A line
    // that starts with spaces ("           PGE" over "           1/2", the page counter alone) or a token that is not exactly
    // that shape ("/2", "X/2", "1/2X", "1/2/3") is no counter.
    private static int? CounterTotal(FenixDcduScreen screen)
    {
        if (screen.Info.Count < 3) return null;
        if (!FenixChannelDcdu.Plain(screen.Info[1]).StartsWith("MSG", StringComparison.Ordinal)) return null;

        string line = FenixChannelDcdu.Plain(screen.Info[2]);

        // ASCII digits only: no sign, no space, no group separator, nor another script's digits.
        int slash = 0;
        while (slash < line.Length && IsDigit(line[slash])) slash++;
        if (slash == 0 || slash >= line.Length || line[slash] != '/') return null;

        int end = slash + 1;
        while (end < line.Length && IsDigit(line[end])) end++;
        if (end == slash + 1 || (end < line.Length && !char.IsWhiteSpace(line[end]))) return null;

        // A counter of nothing is no counter.
        return int.TryParse(line.AsSpan(slash + 1, end - slash - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int total)
               && total >= 1
            ? total
            : null;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    // The line's own text, with the codes read out of it and the ends trimmed.
    private static string TextOf(string coded) => FenixChannelText.TrimmedText(coded);

    private void Remember(string identity)
    {
        if (!_seen.Add(identity)) return;

        _seenOrder.Enqueue(identity);
        if (_seenOrder.Count > SeenCapacity) _seen.Remove(_seenOrder.Dequeue());
    }
}
