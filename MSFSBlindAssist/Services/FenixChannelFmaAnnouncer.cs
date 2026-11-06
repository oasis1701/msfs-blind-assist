namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when a change of the Fenix FMA is heard: a pure state machine with the clock passed in. What it compares is
/// <see cref="FenixFmaReading"/>, the FMA as the pilot would hear it, so the change box and the flash letters never make
/// a change. The first trusted reading after a start, a trust gap or a reconnect is the silent baseline; a change is
/// spoken once the reading it changed to has held <see cref="SettleMs"/> without a break (<see cref="FenixChannelSettle{T}"/>):
/// a reading that moves on inside the settle starts it over, so a change through a passing reading speaks only the one
/// that held; a change that reverts inside the settle says nothing; a muted announcer keeps tracking and speaks nothing.
/// Not thread-safe: the host's UI-thread timer is its one caller.
/// </summary>
internal sealed class FenixChannelFmaAnnouncer
{
    public const long SettleMs = 300;

    private readonly FenixChannelSettle<FenixFmaReading> _settle = new(SettleMs);
    private FenixFmaReading? _baseline;
    private int _connection;

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
            _settle.Clear();
            return null;
        }

        if (!_settle.Held(reading, nowMs)) return null;

        string? text = FenixChannelFmaSpeech.Change(_baseline, reading);
        _baseline = reading;
        return muted ? null : text;
    }

    private void Forget()
    {
        _baseline = null;
        _settle.Clear();
    }
}
