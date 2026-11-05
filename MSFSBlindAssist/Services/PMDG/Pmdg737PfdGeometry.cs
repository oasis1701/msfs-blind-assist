namespace MSFSBlindAssist.Services.PMDG;

/// <summary>Where a value sits on a tape's visible strip: on it, held at its top or bottom edge, or out of view.</summary>
public enum PfdStripPlace { On, TopEdge, BottomEdge, Off }

/// <summary>The flight director's roll bar as the rows read it: degrees of bank difference, positive right (fly right),
/// and whether PMDG holds the bar at the scale's edge.</summary>
public readonly record struct PfdFdRoll(int DegreesRight, bool AtEdge);

/// <summary>
/// Where things sit on the PFD, from the layout's derived scales; pure. A tape shows the current value ±
/// HalfHeight / PixelsPerUnit, both ends drawn (PMDG's clip); a bug PMDG parks is held at the edge when out of view.
/// Offsets and degrees are rounded as the rows read them, half away from zero, and never give a negative zero.
/// PMDG's bank is positive left (it turns the horizon clockwise; confirmed in flight 2026-10-30), its roll bar
/// positive right. The pitch bar and the flight path vector come in degrees from the layout; the roll bar in PMDG's
/// steps, read here in degrees of bank difference (the attitude's bank less the roll command, the owner's unit at the
/// Task 5 checkpoint), at the edge where PMDG clamps it. The ILS pointers read in tenths of a dot from the layout's
/// dots (PMDG's field × 2), at the edge where PMDG's place reaches the scale's end.
/// </summary>
public static class Pmdg737PfdGeometry
{
    public static (double Low, double High) Slice(double current, PfdTapeGeometry g)
    {
        double half = g.HalfHeightPixels / g.PixelsPerUnit;
        return (current - half, current + half);
    }

    public static PfdStripPlace Place(double value, double current, PfdTapeGeometry g, bool parksAtEdge)
    {
        var (low, high) = Slice(current, g);
        if (value >= low && value <= high) return PfdStripPlace.On;
        if (!parksAtEdge) return PfdStripPlace.Off;
        return value > high ? PfdStripPlace.TopEdge : PfdStripPlace.BottomEdge;
    }

    /// <summary>Whether any of a band lies on the strip: from <paramref name="from"/> up or down to
    /// <paramref name="to"/>, or off the tape when <paramref name="to"/> is null.</summary>
    public static bool BandOnStrip(double from, double? to, bool upward, double current, PfdTapeGeometry g)
    {
        var (low, high) = Slice(current, g);
        double bottom = upward ? from : to ?? double.NegativeInfinity;
        double top = upward ? to ?? double.PositiveInfinity : from;
        if (bottom > top) (bottom, top) = (top, bottom);
        return bottom <= high && top >= low;
    }

    /// <summary>How far a value sits above the current value (negative: below), rounded.</summary>
    public static int Offset(double value, double current) => (int)Math.Round(value - current, MidpointRounding.AwayFromZero);

    /// <summary>How far right of the heading a target lies, rounded, from −179 to 180.</summary>
    public static int HeadingOffset(double target, double heading)
    {
        int d = (int)(((long)Math.Round(target - heading, MidpointRounding.AwayFromZero) % 360 + 360) % 360);
        return d > 180 ? d - 360 : d;
    }

    public static double HalfDegrees(double degrees) => Math.Round(degrees * 2, MidpointRounding.AwayFromZero) / 2 + 0.0;

    public static int WholeDegrees(double degrees) => (int)Math.Round(degrees, MidpointRounding.AwayFromZero);

    public static double TenthsOfDot(double dots) => Math.Round(dots * 10, MidpointRounding.AwayFromZero) / 10 + 0.0;

    /// <summary>Whether PMDG draws a pointer at its scale's end (its place clamped to ±<paramref name="edgePixels"/>).</summary>
    public static bool AtEdge(double place, double edgePixels) => Math.Abs(place) >= edgePixels;

    /// <summary>The pitch bar's place above the attitude's centre (the layout's degrees, PMDG's clamp included), in
    /// half degrees.</summary>
    public static double FdPitchDegrees(double bar) => HalfDegrees(bar);

    /// <summary>The roll bar from a reading: <paramref name="bar"/> its place in PMDG's steps (the clamp included),
    /// <paramref name="bank"/> the attitude's bank and <paramref name="command"/> the roll command, both positive
    /// left.</summary>
    public static PfdFdRoll FdRoll(double bar, double bank, double command, PfdFlightDirectorFields fd) =>
        new(WholeDegrees(bank - command), Math.Abs(bar) * fd.RollBarPixelsPerUnit >= fd.RollBarFullScalePixels);
}
