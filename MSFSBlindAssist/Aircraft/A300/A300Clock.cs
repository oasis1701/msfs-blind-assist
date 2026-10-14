using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The two clocks on the main panel, read from the aircraft's own clock variables (measured 2026-10-10): the
/// GMT digits (<c>INI_CLOCK_GMT_HOURS1</c> and so on, which follow the clock's own mode), the chrono (its
/// button cycles reset 0, running 1, stopped 2; <c>INI_CHRONO_TIME</c> in seconds) and the elapsed time (its
/// button toggles running 1 and stopped 2, restarting from zero; <c>INI_ET_TIME</c> in seconds). The chrono
/// and elapsed time buttons are labelled by their state; the Clock box reads each clock's time, chrono and
/// elapsed time. Every number is formatted with the invariant culture.
/// </summary>
public static class A300Clock
{
    public const string Panel = "Clock";
    public const string CaptainTimeKey = "A300_RO_CLOCK_CPT";
    public const string FirstOfficerTimeKey = "A300_RO_CLOCK_FO";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Each clock's GMT digits: key and variable, hours tens, hours units, minutes tens, minutes units.</summary>
    public static readonly IReadOnlyList<(string Key, string Var)> Digits = new[]
    {
        ("A300_CLK_CPT_H1", "INI_CLOCK_GMT_HOURS1"), ("A300_CLK_CPT_H2", "INI_CLOCK_GMT_HOURS2"),
        ("A300_CLK_CPT_M1", "INI_CLOCK_GMT_MINUTES1"), ("A300_CLK_CPT_M2", "INI_CLOCK_GMT_MINUTES2"),
        ("A300_CLK_FO_H1", "INI_CLOCK_GMT_HOURS1_FO"), ("A300_CLK_FO_H2", "INI_CLOCK_GMT_HOURS2_FO"),
        ("A300_CLK_FO_M1", "INI_CLOCK_GMT_MINUTES1_FO"), ("A300_CLK_FO_M2", "INI_CLOCK_GMT_MINUTES2_FO"),
    };

    public static readonly IReadOnlyList<string> CaptainDigitKeys = Digits.Take(4).Select(d => d.Key).ToArray();
    public static readonly IReadOnlyList<string> FirstOfficerDigitKeys = Digits.Skip(4).Select(d => d.Key).ToArray();

    /// <summary>The digit keys a clock's time line is composed from, by its line's key.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DigitKeysByLine =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [CaptainTimeKey] = CaptainDigitKeys,
            [FirstOfficerTimeKey] = FirstOfficerDigitKeys,
        };

    private static readonly IReadOnlyDictionary<double, string> ChronoWords =
        new Dictionary<double, string> { [0] = "Reset", [1] = "Running", [2] = "Stopped" };

    private static readonly IReadOnlyDictionary<double, string> ElapsedWords =
        new Dictionary<double, string> { [0] = "Off", [1] = "Running", [2] = "Stopped" };

    /// <summary>A clock button's state variable and its words, by row key.</summary>
    public static readonly IReadOnlyDictionary<string, (string Var, IReadOnlyDictionary<double, string> Words)> ButtonStates =
        new Dictionary<string, (string, IReadOnlyDictionary<double, string>)>(StringComparer.Ordinal)
        {
            ["A300_CPT_CLOCK_START"] = ("INI_CHRONO_STATE", ChronoWords),
            ["A300_FO_CLOCK_START"] = ("INI_CHRONO_STATE_FO", ChronoWords),
            ["A300_CPT_CLOCK_RUN"] = ("INI_CHRONO_ET_STATUS", ElapsedWords),
            ["A300_FO_CLOCK_RUN"] = ("INI_CHRONO_ET_STATUS_FO", ElapsedWords),
        };

    /// <summary>The key a clock button's state streams under.</summary>
    public static string StateKey(string button) => "A300_BS_" + button.Substring("A300_".Length);

    /// <summary>"07:38 GMT".</summary>
    public static string Time(double h1, double h2, double m1, double m2) =>
        $"{Digit(h1)}{Digit(h2)}:{Digit(m1)}{Digit(m2)} GMT";

    /// <summary>"1 minute 5 seconds".</summary>
    public static string Chrono(double seconds)
    {
        int total = Math.Max(0, (int)Math.Floor(seconds));
        int minutes = total / 60;
        string secondsText = Count(total % 60, "second");
        return minutes == 0 ? secondsText : $"{Count(minutes, "minute")} {secondsText}";
    }

    /// <summary>"1 hour 5 minutes": the elapsed time counter shows hours and minutes.</summary>
    public static string Elapsed(double seconds)
    {
        int totalMinutes = Math.Max(0, (int)Math.Floor(seconds / 60));
        int hours = totalMinutes / 60;
        string minutesText = Count(totalMinutes % 60, "minute");
        return hours == 0 ? minutesText : $"{Count(hours, "hour")} {minutesText}";
    }

    private static string Digit(double value) => ((int)Math.Round(value)).ToString("0", Inv);

    private static string Count(int n, string unit) => $"{n.ToString(Inv)} {unit}{(n == 1 ? "" : "s")}";
}
