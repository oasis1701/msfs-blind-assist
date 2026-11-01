using System.Globalization;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// The text of a read-only numeric row: "{value} {units}", except that the PLACEHOLDER unit is
/// never appended. "number" is SimConnect's name for a dimensionless quantity and
/// <c>SimVarDefinition.Units</c>'s default, so every unit-less read-out used to read
/// "V1: 0 number", "VHF 1 Volume: 50 number" — a word that told the pilot nothing.
/// A real unit ("feet", "psi", "inHg") still reads.
/// </summary>
public static class ReadoutFormat
{
    public const string PlaceholderUnit = "number";

    /// <summary>Format the outside-temperature readout in whole degrees, converting only the spoken value.
    /// Rounds half away from zero, as the "0" format did, but through an int: formatting a double
    /// with "0" keeps the sign of a value that rounds to zero, so -0.3 °C read "-0 degrees".</summary>
    public static string OutsideTemperature(double celsius, bool useFahrenheit)
    {
        double temperature = useFahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius;
        int whole = (int)Math.Round(temperature, MidpointRounding.AwayFromZero);
        string unit = useFahrenheit ? "Fahrenheit" : "Celsius";
        return $"{whole.ToString(CultureInfo.InvariantCulture)} degrees {unit}";
    }

    public static string WithUnit(string value, string? units)
    {
        if (string.IsNullOrWhiteSpace(units)) return value;
        var u = units.Trim();
        return string.Equals(u, PlaceholderUnit, StringComparison.OrdinalIgnoreCase) ? value : $"{value} {u}";
    }
}
