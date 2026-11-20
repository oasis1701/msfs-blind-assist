namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// When the RPM has settled on one magneto, which is when a sighted pilot reads the drop: the
/// needle has stopped moving. Pure, for the suite; <see cref="CowsDA40Definition"/> feeds it the
/// tachometer and polls <see cref="IsDue"/>.
///
/// ⚠️ A FIXED TWO SECONDS READ THE NEEDLE STILL FALLING. Live on the XLS at VCBI (2026-10-20), at
/// 2000 RPM: the right magneto read 1900, 1880, 1870 one second apart, so "drop 90" was spoken
/// for a drop of 120, and the left "60" for 80; the differential came out 30 for 40. The tach is a
/// 1 Hz batch, so the value read at two seconds could be a second old as well. So the drop is
/// read once the RPM has moved no more than <see cref="BandRpm"/> for <see cref="QuietMs"/>, never
/// sooner than <see cref="MinMs"/> and never later than <see cref="MaxMs"/> (a needle that keeps
/// wandering is read anyway). Samples that do not change at all may not arrive, which is why
/// quiet is measured as time since the last change, not as a count of agreeing samples.
/// </summary>
public sealed class DA40MagnetoSettle
{
    public const int BandRpm = 10;
    public const int QuietMs = 1500;
    public const int MinMs = 2000;
    public const int MaxMs = 8000;

    private long _since = -1;
    private long _lastChange;
    private double _last = double.NaN;

    /// <summary>A side is waiting to be read.</summary>
    public bool Pending => _since >= 0;

    /// <summary>The key has just moved to one magneto.</summary>
    public void Start(long nowMs)
    {
        _since = nowMs;
        _lastChange = nowMs;
    }

    public void Cancel() => _since = -1;

    /// <summary>A tachometer sample. A change beyond <see cref="BandRpm"/> restarts the quiet.</summary>
    public void Sample(double rpm, long nowMs)
    {
        if (Pending && (double.IsNaN(_last) || Math.Abs(rpm - _last) > BandRpm)) _lastChange = nowMs;
        _last = rpm;
    }

    /// <summary>The needle has settled (or will never): read the drop now.</summary>
    public bool IsDue(long nowMs)
        => Pending && nowMs - _since >= MinMs
           && (nowMs - _lastChange >= QuietMs || nowMs - _since >= MaxMs);
}
