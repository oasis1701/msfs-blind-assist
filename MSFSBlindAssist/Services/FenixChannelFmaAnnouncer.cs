namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when a change of the Fenix FMA is heard: a pure state machine with the clock passed in. What it compares is
/// <see cref="FenixFmaReading"/>, the FMA as the pilot would hear it, so the change box and the flash letters never make
/// a change. The first trusted reading after a start, a trust gap or a reconnect is the silent baseline; a change is
/// spoken once it has held <see cref="SettleMs"/> from the first sample that differed; a change that reverts inside the
/// settle says nothing; a muted announcer keeps tracking and speaks nothing. Not thread-safe: the host's UI-thread timer
/// is its one caller.
/// </summary>
internal sealed class FenixChannelFmaAnnouncer
{
    public const long SettleMs = 300;

    private FenixFmaReading? _baseline;
    private int _connection;
    private long? _changeSince;

    /// <summary>The text to speak now, or null.</summary>
    public string? Sample(FenixDisplayVerdict verdict, IReadOnlyList<FenixFmaSlot>? slots, int connection, long nowMs, bool muted)
    {
        if (!verdict.Trusted || slots is null || slots.Count != FenixChannelFields.FmaEntry.SlotCount)
        {
            Forget();
            return null;
        }

        if (_baseline is not null && connection != _connection) Forget();

        FenixFmaReading reading = FenixChannelFmaSpeech.Read(slots);

        if (_baseline is null)
        {
            _baseline = reading;
            _connection = connection;
            return null;
        }

        if (reading.Equals(_baseline))
        {
            _changeSince = null;
            return null;
        }

        _changeSince ??= nowMs;
        if (nowMs - _changeSince.Value < SettleMs) return null;

        string? text = FenixChannelFmaSpeech.Change(_baseline, reading);
        _baseline = reading;
        _changeSince = null;
        return muted ? null : text;
    }

    private void Forget()
    {
        _baseline = null;
        _changeSince = null;
    }
}
