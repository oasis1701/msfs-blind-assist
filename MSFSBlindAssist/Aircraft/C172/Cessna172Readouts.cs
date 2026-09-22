// MSFSBlindAssist/Aircraft/C172/Cessna172Readouts.cs
using System.Globalization;

namespace MSFSBlindAssist.Aircraft.C172;

/// <summary>
/// Display/spoken formatting for the C172 status rows and read-outs. All InvariantCulture: these
/// strings are spoken and shown beside sim-native numbers, and MainForm's generic formatter has no
/// "MHz" case and no unit suffix for psi / fahrenheit / gallons / rpm.
/// </summary>
public static class Cessna172Readouts
{
    public const double HpaPerInHg = 33.8639;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ComFrequency(double mhz) =>
        mhz >= 118.0 && mhz <= 137.0 ? mhz.ToString("0.000", Inv) + " MHz" : "---.--- MHz";

    public static string NavFrequency(double mhz) =>
        mhz >= 108.0 && mhz <= 118.0 ? mhz.ToString("0.00", Inv) + " MHz" : "---.-- MHz";

    public static string Course(double degrees) =>
        ((int)Math.Round(degrees) % 360 + 360) % 360 is var d ? d.ToString("000", Inv) + " degrees" : "";

    public static string Altimeter(double inHg) =>
        inHg.ToString("0.00", Inv) + " inHg (" + Math.Round(inHg * HpaPerInHg).ToString("0", Inv) + " hPa)";

    /// <summary>The B key: the HS787's form — standard at 29.92, else hPa then inHg.</summary>
    public static string AltimeterSpoken(double? inHg)
    {
        if (inHg == null || inHg <= 0) return "Altimeter not available";
        if (Math.Abs(inHg.Value - 29.92) < 0.005) return "Altimeter standard";
        int hpa = (int)Math.Round(inHg.Value * HpaPerInHg);
        return "Altimeter: " + hpa.ToString(Inv) + ", " + inHg.Value.ToString("0.00", Inv);
    }

    public static bool IsValidComMhz(double mhz) => mhz >= 118.0 && mhz <= 136.975;
    public static bool IsValidAltimeterInHg(double inHg) => inHg >= 27.0 && inHg <= 31.5;

    /// <summary>KOHLSMAN_SET parameter: millibars × 16 (the A380 convention), never inHg × 16.</summary>
    public static uint KohlsmanSetParam(double inHg) => (uint)Math.Round(inHg * HpaPerInHg * 16.0);

    /// <summary>MIXTURE1_SET parameter: 0–16383 for 0–100 %.</summary>
    public static uint MixtureSetParam(double percent) =>
        (uint)Math.Round(Math.Clamp(percent, 0.0, 100.0) / 100.0 * 16383.0);

    public static string Rpm(double v) => v.ToString("0", Inv) + " RPM";
    public static string Psi(double v) => v.ToString("0", Inv) + " psi";
    public static string Fahrenheit(double v) => v.ToString("0", Inv) + " °F";
    public static string GallonsPerHour(double v) => v.ToString("0.0", Inv) + " gallons per hour";
    public static string Gallons(double v) => v.ToString("0.0", Inv) + " gallons";
    public static string Volts(double v) => v.ToString("0.0", Inv) + " volts";
    public static string Amps(double v) => v.ToString("0.0", Inv) + " amps";
    public static string Percent(double v) => v.ToString("0", Inv) + " percent";
}
