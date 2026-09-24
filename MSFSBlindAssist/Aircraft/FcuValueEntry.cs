namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Validation and encoding for a pilot-typed FCU value (A32NX/A330 FCU windows and FCU panel number
/// fields), so both surfaces accept the same ranges, speak the same errors and hand the setters the same
/// encoding (Mach travels x100). Pinned by FcuValueEntryTests.
/// </summary>
internal static class FcuValueEntry
{
    public static bool TryHeading(double value, out int heading, out string? error)
    {
        heading = 0;
        error = null;
        if (value < 0 || value > 360) { error = "Heading must be between 0 and 360 degrees"; return false; }
        heading = (int)Math.Round(value) % 360;
        return true;
    }

    public static bool TrySpeed(double value, out int internalSpeed, out string? error)
    {
        internalSpeed = 0;
        error = null;
        if (!((value >= 100 && value <= 399) || (value >= 0.10 && value <= 0.99)))
        {
            error = "Speed must be 100-399 knots or 0.10-0.99 Mach";
            return false;
        }
        internalSpeed = value < 1.0 ? (int)Math.Round(value * 100) : (int)Math.Round(value);
        return true;
    }

    public static bool TryAltitude(double value, out double feet, out string? error)
    {
        feet = 0;
        error = null;
        if (value < 100 || value > 49000) { error = "Altitude must be between 100 and 49000 feet"; return false; }
        feet = value;
        return true;
    }
}
