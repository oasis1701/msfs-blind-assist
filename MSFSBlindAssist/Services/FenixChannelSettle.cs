namespace MSFSBlindAssist.Services;

/// <summary>
/// The settle the Fenix channel's announcers share: a reading counts once it has stayed the same, without a break, for the
/// settle time. A sample that differs from the reading being held starts the hold over with itself, so a reading that
/// flickers through another counts only once the last one has held the whole time ([FNX-42]), and a reading that goes back
/// to what the announcer already said is no change at all (the announcer then calls <see cref="Clear"/>). Pure: every time
/// is the caller's, in milliseconds, and it reads no clock. Not thread-safe: each announcer's one caller is the host's
/// UI-thread timer.
/// </summary>
/// <remarks>
/// The hold also remembers whether its announcer was muted at any sample of it (<see cref="HeldMuted"/>), for an announcer
/// that must not call a reading that came while it was muted once it is unmuted: the DCDU's. The first sample of a hold
/// never counts, whatever the settle, so a reading is always seen twice before it counts.
/// </remarks>
/// <typeparam name="T">What a sample reads; compared with <paramref name="comparer"/>, or by default the type's own equality.</typeparam>
/// <param name="settleMs">How long a reading must hold before it counts, in milliseconds.</param>
/// <param name="comparer">What makes two readings the same reading.</param>
internal sealed class FenixChannelSettle<T>(long settleMs, IEqualityComparer<T>? comparer = null)
{
    private readonly IEqualityComparer<T> _comparer = comparer ?? EqualityComparer<T>.Default;

    private bool _holding;
    private T? _reading;
    private long _sinceMs;
    private bool _muted;

    /// <summary>How long a reading must hold before it counts, in milliseconds.</summary>
    public long SettleMs { get; } = settleMs;

    /// <summary>
    /// Whether the reading that last counted was muted at any sample of its hold. Read it right after <see cref="Held"/>
    /// said true.
    /// </summary>
    public bool HeldMuted { get; private set; }

    /// <summary>
    /// Offers one sample's reading. True when it is the reading being held and it has now held <see cref="SettleMs"/>: it
    /// counts, and the hold ends, so the caller takes it as its own and the next sample that differs from that starts a
    /// new hold. False otherwise; a reading that differs from the one being held starts its hold over at
    /// <paramref name="nowMs"/>.
    /// </summary>
    /// <param name="reading">This sample's reading.</param>
    /// <param name="nowMs">The caller's clock, in milliseconds; the same clock at every sample.</param>
    /// <param name="muted">Whether the announcer is muted at this sample.</param>
    public bool Held(T reading, long nowMs, bool muted = false)
    {
        if (!_holding || !_comparer.Equals(_reading!, reading))
        {
            _holding = true;
            _reading = reading;
            _sinceMs = nowMs;
            _muted = muted;
            return false;
        }

        _muted |= muted;
        if (nowMs - _sinceMs < SettleMs) return false;

        HeldMuted = _muted;
        Clear();
        return true;
    }

    /// <summary>
    /// Ends the hold: the reading is the one the announcer already has, or a gap (not trusted, a reconnect) made it no
    /// reading at all. The next sample starts a new hold.
    /// </summary>
    public void Clear()
    {
        _holding = false;
        _reading = default;
    }
}
