using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The four FCU windows in words, worded exactly as the typed values confirm them ("Heading 270",
/// "Altitude 12,000 feet").
///
/// Only the altitude window is announced on its own (<see cref="A300WindowTracker"/>): the aircraft
/// writes <c>INI_Altitude_Dial</c> from the knob handlers alone, while the heading, speed and vertical
/// speed windows are also written by the autopilot's own logic (heading sync, managed speed, vertical
/// speed sync; read from the aircraft's code 2026-10-04), so announcing them would speak changes
/// nobody made. Those three are read on request (Shift+H, Shift+S and Shift+V), and each window is set by
/// its typed box: the four FCU knobs are not panel rows.
/// </summary>
public static class A300FcuWindows
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Speed(double value, bool isMach) => isMach
        ? $"Mach {value.ToString("0.00", Inv)}"
        : $"Speed {Math.Round(value).ToString("0", Inv)} knots";

    public static string Heading(double degrees) => $"Heading {A300Readouts.Heading(Math.Round(degrees))}";

    public static string Altitude(double feet) => $"Altitude {Math.Round(feet).ToString("#,0", Inv)} feet";

    public static string VerticalSpeed(double fpm)
    {
        string text = Math.Round(fpm).ToString("#,0", Inv);
        return $"Vertical speed {(text == "-0" ? "0" : text)} feet per minute";
    }
}

/// <summary>
/// One window's call-out: baseline-first (the first value after a reset is silent), a repeat of the
/// last phrase is silent, and a change inside <see cref="EchoWindowMs"/> of MSFSBA's own write is
/// recorded silently, because MSFSBA has already confirmed it. Pure: the caller passes the clock.
/// </summary>
public sealed class A300WindowTracker
{
    /// <summary>Long enough for a write to land and the next 1 Hz batch to deliver it.</summary>
    public const long EchoWindowMs = 2500;

    private string? _last;
    private bool _known;
    private long _echoUntil = long.MinValue;

    public void SuppressEcho(long nowMs) => _echoUntil = nowMs + EchoWindowMs;

    /// <summary>The phrase to speak for this delivery, or null.</summary>
    public string? Observe(string phrase, long nowMs)
    {
        bool known = _known;
        string? last = _last;
        _known = true;
        _last = phrase;
        if (!known || phrase == last || nowMs <= _echoUntil)
            return null;
        return phrase;
    }

    /// <summary>Records a baseline only where none exists yet; true when one was added.</summary>
    public bool Seed(string phrase)
    {
        if (_known)
            return false;
        _known = true;
        _last = phrase;
        return true;
    }

    public void Reset()
    {
        _known = false;
        _last = null;
        _echoUntil = long.MinValue;
    }
}
