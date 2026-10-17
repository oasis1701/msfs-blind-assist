using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>The three speed-tape speeds that depend on the flap lever.</summary>
public enum A300PfdSpeed { GreenDot, S, F }

/// <summary>
/// How the display status boxes read their values. Every number goes through the invariant culture, so a
/// comma-decimal Windows locale never turns "5.0 degrees up" into "5,0 degrees up". Pure.
/// </summary>
public static class A300DisplayText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The words for a flap speed the tape is not showing at this lever position.</summary>
    public const string NotShown = "not shown at this flap setting";

    /// <summary>Pitch attitude from SimConnect's radians (positive is nose DOWN).</summary>
    public static string Pitch(double radians)
    {
        double degrees = radians * 180 / Math.PI;
        return Math.Abs(degrees) < 0.5 ? "Level" : $"{Math.Abs(degrees).ToString("0.0", Inv)} degrees {(degrees < 0 ? "up" : "down")}";
    }

    /// <summary>Bank from SimConnect's radians (positive is LEFT).</summary>
    public static string Bank(double radians)
    {
        double degrees = radians * 180 / Math.PI;
        return Math.Abs(degrees) < 0.5 ? "Wings level" : $"{Math.Abs(degrees).ToString("0.0", Inv)} degrees {(degrees > 0 ? "left" : "right")}";
    }

    /// <summary>A heading from radians, as three digits (360 reads 000).</summary>
    public static string HeadingFromRadians(double radians)
    {
        int degrees = (int)Math.Round(radians * 180 / Math.PI);
        return ((degrees % 360 + 360) % 360).ToString("000", Inv);
    }

    /// <summary>Vertical speed to the nearest ten feet per minute; under 50 either way is level.</summary>
    public static string VerticalSpeed(double feetPerMinute)
    {
        if (Math.Abs(feetPerMinute) < 50)
            return "Level";
        double tens = Math.Round(Math.Abs(feetPerMinute) / 10) * 10;
        return $"{tens.ToString("#,0", Inv)} feet per minute {(feetPerMinute > 0 ? "up" : "down")}";
    }

    /// <summary>A speed in whole knots; zero or less means the aircraft has not computed it.</summary>
    public static string Speed(double knots) =>
        knots > 0 ? $"{Math.Round(knots).ToString("0", Inv)} knots" : "not available";

    /// <summary>
    /// Green dot, S or F, read only at the flap lever positions where the A300's speed tape draws it
    /// (PFD::drawSpeedTape: green dot at lever 0, S at lever 1, F at levers 2 and 3).
    /// </summary>
    public static string FlapSpeed(A300PfdSpeed speed, double knots, double? flapLever)
    {
        if (flapLever is not double lever)
            return "not available";
        return TapeSpeedAt(lever) == speed ? Speed(knots) : NotShown;
    }

    /// <summary>
    /// Which of green dot, S and F the speed tape draws at this flap lever position
    /// (PFD::drawSpeedTape, 1.0.11): green dot at 0, S at 1, F at 2 and 3, none of them at 4.
    /// </summary>
    public static A300PfdSpeed? TapeSpeedAt(double flapLever) => (int)Math.Round(flapLever) switch
    {
        0 => A300PfdSpeed.GreenDot,
        1 => A300PfdSpeed.S,
        2 or 3 => A300PfdSpeed.F,
        _ => null,
    };

    /// <summary>Green dot off the tape, and the speed the tape shows in its place ("Green dot not shown.
    /// S speed 198 knots"); <paramref name="knots"/> null is a speed that did not answer.</summary>
    public static string GreenDotNotShown(A300PfdSpeed shown, double? knots) =>
        $"Green dot not shown. {(shown == A300PfdSpeed.S ? "S speed" : "F speed")} {(knots is double k ? Speed(k) : "unavailable")}";

    /// <summary>The minimums set on the EFIS panel, in feet; zero is not set.</summary>
    public static string Minimums(double feet) =>
        feet > 0 ? Feet(feet) : "not set";

    /// <summary>Whole feet with a thousands separator.</summary>
    public static string Feet(double feet) => $"{Math.Round(feet).ToString("#,0", Inv)} feet";

    /// <summary>The distance to the next waypoint in tenths of a mile; zero is no waypoint.</summary>
    public static string WaypointDistance(double nauticalMiles) =>
        nauticalMiles > 0 ? $"{nauticalMiles.ToString("0.0", Inv)} nautical miles" : "not available";

    /// <summary>A DME distance in tenths of a mile; zero is no DME received.</summary>
    public static string Dme(double nauticalMiles) =>
        nauticalMiles > 0 ? $"{nauticalMiles.ToString("0.0", Inv)} nautical miles" : "no DME";

    /// <summary>A VOR or ILS frequency.</summary>
    public static string Megahertz(double megahertz) =>
        megahertz > 0 ? $"{megahertz.ToString("0.00", Inv)} megahertz" : "not tuned";

    /// <summary>An ADF frequency.</summary>
    public static string Kilohertz(double kilohertz) =>
        kilohertz > 0 ? $"{Math.Round(kilohertz).ToString("0", Inv)} kilohertz" : "not tuned";

    /// <summary>The wind's direction (the sim gives it true), as three digits.</summary>
    public static string WindDirection(double degrees) =>
        $"{(((int)Math.Round(degrees) % 360 + 360) % 360).ToString("000", Inv)} true";

    /// <summary>Whether a receiver has its signal.</summary>
    public static string Received(double flag) => flag >= 0.5 ? "received" : "not received";

    /// <summary>Whole knots, zero included (a ground speed or airspeed, never "not available").</summary>
    public static string Knots(double knots)
    {
        string text = Math.Round(knots).ToString("0", Inv);
        return $"{(text == "-0" ? "0" : text)} knots";
    }
}
