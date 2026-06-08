using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Single source of truth for user-facing horizontal-distance text. Reads the
/// active unit via <see cref="UnitProvider"/> (wired to SettingsManager at
/// startup) so a setting change takes effect on the next call. Display layer
/// only — never used for guidance thresholds.
/// </summary>
public static class DistanceFormatter
{
    public const double FeetPerMetre = 3.28084;
    public const double MetresPerFoot = 0.3048;

    /// <summary>Resolves the active unit. Defaults to Metres; app wires this to SettingsManager.</summary>
    public static Func<DistanceUnit> UnitProvider { get; set; } = () => DistanceUnit.Metres;

    public static bool IsMetres => UnitProvider() == DistanceUnit.Metres;

    /// <summary>Format a metres value in the active unit, e.g. "150 metres" / "500 feet".</summary>
    public static string FromMetres(double metres, bool shortForm = false)
        => Format(IsMetres ? metres : metres * FeetPerMetre, IsMetres, shortForm);

    /// <summary>Format a feet value in the active unit.</summary>
    public static string FromFeet(double feet, bool shortForm = false)
        => Format(IsMetres ? feet * MetresPerFoot : feet, IsMetres, shortForm);

    public static string UnitWord(bool shortForm = false)
        => IsMetres ? (shortForm ? "m" : "metres") : (shortForm ? "ft" : "feet");

    private static string Format(double value, bool metres, bool shortForm)
    {
        if (value < 0) value = 0;
        double step = metres
            ? (value < 100 ? 5 : value < 500 ? 10 : 50)
            : (value < 200 ? 25 : 50);
        int rounded = (int)(Math.Round(value / step) * step);
        if (shortForm) return $"{rounded} {(metres ? "m" : "ft")}";
        string unit = metres
            ? (rounded == 1 ? "metre" : "metres")
            : (rounded == 1 ? "foot" : "feet");
        return $"{rounded} {unit}";
    }
}
