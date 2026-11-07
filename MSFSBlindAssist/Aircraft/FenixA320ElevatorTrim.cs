using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The Fenix A320 family's pitch trim (THS) in the units the cockpit shows it. The Fenix keeps its
/// stabiliser in <c>A_FC_ELEVATOR_TRIM</c>, thousandths of a degree with positive = nose UP
/// (measured 2026-11-04 on an A321 at CYYZ: 1698 while the ECAM read UP 1.7 and the display
/// channel's 145.15 read -1.70). The stock <c>ELEVATOR TRIM POSITION</c> does not follow it (0.29°
/// at the same moment), so trimming by the stock value gave F/CTL PITCH TRIM/MCDU/CG DISAGREE.
/// The travel is the A320's: DN 4.0 to UP 13.5 degrees.
/// </summary>
public static class FenixA320ElevatorTrim
{
    public const string LVar = "A_FC_ELEVATOR_TRIM";

    /// <summary>The panel's typed-entry key; "_SET" makes MainForm render it as a text field.</summary>
    public const string SetKey = "A_FC_ELEVATOR_TRIM_SET";

    public const double RawPerDegree = 1000.0;
    public const double MaxNoseDownDegrees = 4.0;
    public const double MaxNoseUpDegrees = 13.5;

    /// <summary>The L:var's raw value as degrees, positive = UP, the scale the shared trim call-out speaks.</summary>
    public static double ToDegrees(double raw) => raw / RawPerDegree;

    /// <summary>
    /// A typed trim in degrees (positive = UP, negative = DN, as the shared "Trim up 0.3" call-out
    /// reads it) as the raw value to write, rounded to the tenth of a degree the ECAM and MCDU show.
    /// Refuses NaN and anything past the THS travel with the error to speak.
    /// </summary>
    public static bool TryToRaw(double degrees, out double raw, out string? error)
    {
        raw = 0;
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
        {
            error = "Invalid number format";
            return false;
        }

        double tenths = Math.Round(degrees, 1, MidpointRounding.AwayFromZero);
        if (tenths > MaxNoseUpDegrees || tenths < -MaxNoseDownDegrees)
        {
            error = string.Create(CultureInfo.InvariantCulture,
                $"Pitch trim out of range. Range: down {MaxNoseDownDegrees:F1} to up {MaxNoseUpDegrees:F1}");
            return false;
        }

        raw = Math.Round(tenths * RawPerDegree);
        error = null;
        return true;
    }

    /// <summary>"up 0.3" / "down 0.8", invariant-formatted like the cockpit writes it.</summary>
    public static string Describe(double degrees)
    {
        double tenths = Math.Round(degrees, 1, MidpointRounding.AwayFromZero);
        string direction = tenths >= 0 ? "up" : "down";
        return string.Create(CultureInfo.InvariantCulture, $"{direction} {Math.Abs(tenths):F1}");
    }
}
