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

    public static string WithUnit(string value, string? units)
    {
        if (string.IsNullOrWhiteSpace(units)) return value;
        var u = units.Trim();
        return string.Equals(u, PlaceholderUnit, StringComparison.OrdinalIgnoreCase) ? value : $"{value} {u}";
    }
}
