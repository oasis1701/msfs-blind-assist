using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One MCP value's call-out held until the value rests (gap plan 05, Task 4, ruling R60): a knob spun in
/// the cockpit is heard once, at the value it stops on. A continuous row is delivered only when it changes,
/// about once a second, so "rested" is a time with no newer change (<see cref="RestMs"/>), never a value
/// delivered twice. Pure; the definition feeds it deliveries and asks it on a timer.
/// </summary>
public sealed class Bbs757RestingValue
{
    /// <summary>
    /// How long a value goes unchanged before it is spoken: one delivery period with no change, plus
    /// margin. R60's 800 ms is shorter than the 1 s delivery and would speak every step of a spin.
    /// </summary>
    public const int RestMs = 1500;

    private string? _spoken;
    private string? _pending;
    private long _changedAt;

    /// <summary>A change waits to rest.</summary>
    public bool Pending => _pending != null;

    /// <summary>A delivery with a new value; the first after <see cref="Reset"/> is the silent baseline.</summary>
    public void Changed(string phrase, long nowMs)
    {
        if (_spoken == null)
        {
            _spoken = phrase;
            _pending = null;
            return;
        }
        _pending = phrase;
        _changedAt = nowMs;
    }

    /// <summary>
    /// The phrase once it has rested <see cref="RestMs"/> and differs from the last spoken (it then counts
    /// as spoken); else null. A value spun away and back to where it was says nothing.
    /// </summary>
    public string? TakeDue(long nowMs)
    {
        if (_pending == null || nowMs - _changedAt < RestMs) return null;
        string phrase = _pending;
        _pending = null;
        if (phrase == _spoken) return null;
        _spoken = phrase;
        return phrase;
    }

    /// <summary>The app's own set, or a value the pilot is not looking at: counts as spoken; a waiting change is dropped.</summary>
    public void Rebaseline(string phrase)
    {
        _spoken = phrase;
        _pending = null;
    }

    /// <summary>A reconnect or a flight load: the next value is a baseline again.</summary>
    public void Reset()
    {
        _spoken = null;
        _pending = null;
    }
}

/// <summary>
/// The 757's autoflight call-outs (gap plan 05, Task 4): the MCP values once they rest, A/T arm, and the
/// A/P and A/T disconnect lights. The keys watch BlueBird's own variables, never the stock autopilot ones;
/// each is a Continuous announced row with its own Ctrl+M row except the two silent ones (the Mach mode
/// flag and the Mach's tenths wheel), which only feed another row. Pure: keys and phrases, every number
/// invariant.
/// </summary>
public static class Bbs757AutoflightAnnouncements
{
    public const string HeadingWatchKey = "BBS757_MCP_HEADING_WATCH";
    public const string AltitudeWatchKey = "BBS757_MCP_ALTITUDE_WATCH";
    public const string VsWatchKey = "BBS757_MCP_VS_WATCH";
    public const string SpeedWatchKey = "BBS757_MCP_SPEED_WATCH";

    /// <summary>The Mach's row (its Ctrl+M row), watching the hundredths wheel; the Mach is read from both wheels.</summary>
    public const string MachWatchKey = "BBS757_MCP_MACH_WATCH";

    /// <summary>The tenths wheel, silent: it only feeds <see cref="MachWatchKey"/>'s call-out.</summary>
    public const string MachTenthsWatchKey = "BBS757_MCP_MACH_TENTHS_WATCH";

    /// <summary>The IAS/MACH flag, silent: the speed row speaks only in IAS, the Mach row only in Mach.</summary>
    public const string ModeWatchKey = "BBS757_MCP_MODE_WATCH";

    public const string AtArmedKey = "BBS757_AT_ARMED";
    public const string ApDiscLightKey = "BBS757_AP_DISC_LIGHT";
    public const string AtDiscLightKey = "BBS757_AT_DISC_LIGHT";

    public const string AtArmedVar = "BBS_AUTO_THROTTLE";
    public const string ApDiscLightVar = "B757_MAIN_APDISC_1_DECAL";
    public const string AtDiscLightVar = "B757_MAIN_ATDISC_1_DECAL";

    /// <summary>The five MCP rows that speak once they rest, in the order the release asks them.</summary>
    public static IReadOnlyList<string> RestingRows { get; } =
        new[] { HeadingWatchKey, AltitudeWatchKey, VsWatchKey, SpeedWatchKey, MachWatchKey };

    /// <summary>"MCP heading 090": three digits, 360 reads 000.</summary>
    public static string HeadingPhrase(double v)
    {
        long deg = ((Round(v) % 360) + 360) % 360;
        return $"MCP heading {deg.ToString("D3", CultureInfo.InvariantCulture)}";
    }

    /// <summary>"MCP altitude 12000": a plain integer, no thousands separator.</summary>
    public static string AltitudePhrase(double v) => $"MCP altitude {Plain(Round(v))}";

    /// <summary>"MCP vertical speed minus 1500", "MCP vertical speed 1500", "MCP vertical speed 0" (never "minus 0").</summary>
    public static string VsPhrase(double v)
    {
        long fpm = Round(v);
        return fpm < 0 ? $"MCP vertical speed minus {Plain(-fpm)}" : $"MCP vertical speed {Plain(fpm)}";
    }

    /// <summary>"MCP speed 250".</summary>
    public static string SpeedPhrase(double v) => $"MCP speed {Plain(Round(v))}";

    /// <summary>"MCP Mach 0.78".</summary>
    public static string MachPhrase(double v) => $"MCP Mach {v.ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>"A/T armed" (1) or "A/T disarmed" (0); null for an unreadable value.</summary>
    public static string? AtArmedPhrase(double v)
        => double.IsFinite(v) ? (v >= 0.5 ? "A/T armed" : "A/T disarmed") : null;

    /// <summary>"Autopilot disconnect" or "Autothrottle disconnect" when the light comes on; null when it goes out or is unreadable.</summary>
    public static string? DiscLightPhrase(string what, double v)
        => double.IsFinite(v) && v >= 0.5 ? $"{what} disconnect" : null;

    private static long Round(double value)
        => (long)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), -1e12, 1e12);

    private static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);
}
