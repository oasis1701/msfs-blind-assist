using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The pedestal's trim readings (measured 2026-10-10): the rudder trim display, a direction code
/// (<c>INI_RUDDER_TRIM_DISPLAY0</c>: 0 left, 1 centered, 2 right) then tens, units and tenths
/// (<c>_DISPLAY1</c> to <c>_DISPLAY3</c>); and the pitch trim, the stabilizer angle in degrees, positive nose
/// up (measured 2026-10-06). Every number is formatted with the invariant culture.
/// </summary>
public static class A300Trim
{
    public const string Panel = "Trim";
    public const string RudderKey = "A300_RO_RUDDER_TRIM";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The rudder trim display's direction code, tens and units; its line's own variable is the tenths.</summary>
    public static readonly IReadOnlyList<(string Key, string Var)> RudderDigits = new[]
    {
        ("A300_TRIM_RUD_CODE", "INI_RUDDER_TRIM_DISPLAY0"),
        ("A300_TRIM_RUD_TENS", "INI_RUDDER_TRIM_DISPLAY1"),
        ("A300_TRIM_RUD_UNITS", "INI_RUDDER_TRIM_DISPLAY2"),
    };

    public static readonly IReadOnlyList<string> RudderDigitKeys = RudderDigits.Select(d => d.Key).ToArray();

    /// <summary>"left 3.3", "right 7.2", "0.0" when centered.</summary>
    public static string Rudder(double code, double tens, double units, double tenths)
    {
        double value = Math.Round(tens) * 10 + Math.Round(units) + Math.Round(tenths) / 10;
        string text = value.ToString("0.0", Inv);
        return Math.Round(code) switch
        {
            0 => $"left {text}",
            2 => $"right {text}",
            _ => text,
        };
    }

    /// <summary>"1.1 degrees nose up", "0.5 degrees nose down", "0.0 degrees".</summary>
    public static string Pitch(double degrees)
    {
        double rounded = Math.Round(degrees, 1);
        string text = Math.Abs(rounded).ToString("0.0", Inv);
        return rounded > 0 ? $"{text} degrees nose up" : rounded < 0 ? $"{text} degrees nose down" : $"{text} degrees";
    }
}
