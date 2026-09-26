namespace MSFSBlindAssist.Navigation;

/// <summary>
/// When to say "Off pavement." — after <see cref="OnsetSeconds"/> continuously off pavement while moving
/// at <see cref="MinGroundSpeedKts"/> or more, then every <see cref="RepeatSeconds"/> while still off and
/// moving; re-armed only after <see cref="RearmOnPavementSeconds"/> continuously back on pavement, so a
/// graze along an edge is one alert, not a stream. No direction word: the steering tone is the only
/// direction authority, and a spoken "left" could contradict it. Time is injected — pure,
/// <c>OffPavementAlertTests</c>. Judgement values (KMEM 36L 2026-09-26), not measurements.
/// </summary>
public sealed class OffPavementAlert
{
    public const double OnsetSeconds = 1.0;
    public const double RepeatSeconds = 6.0;
    public const double RearmOnPavementSeconds = 2.0;
    public const double MinGroundSpeedKts = 5.0;
    public const string Phrase = "Off pavement.";

    private DateTime _offSince = DateTime.MinValue;
    private DateTime _onSince = DateTime.MinValue;
    private DateTime _lastSpoken = DateTime.MinValue;

    /// <summary>True when <see cref="Phrase"/> should be spoken now.</summary>
    public bool Update(bool offPavement, double groundSpeedKts, DateTime nowUtc)
    {
        if (!offPavement)
        {
            _offSince = DateTime.MinValue;
            if (_onSince == DateTime.MinValue) _onSince = nowUtc;
            if ((nowUtc - _onSince).TotalSeconds >= RearmOnPavementSeconds) _lastSpoken = DateTime.MinValue;
            return false;
        }

        _onSince = DateTime.MinValue;
        if (_offSince == DateTime.MinValue) _offSince = nowUtc;
        if (groundSpeedKts < MinGroundSpeedKts) return false;

        bool due = _lastSpoken == DateTime.MinValue
            ? (nowUtc - _offSince).TotalSeconds >= OnsetSeconds
            : (nowUtc - _lastSpoken).TotalSeconds >= RepeatSeconds;
        if (!due) return false;
        _lastSpoken = nowUtc;
        return true;
    }

    public void Reset()
    {
        _offSince = DateTime.MinValue;
        _onSince = DateTime.MinValue;
        _lastSpoken = DateTime.MinValue;
    }
}
