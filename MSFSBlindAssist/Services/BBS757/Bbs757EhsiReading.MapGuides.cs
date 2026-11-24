namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One entry of a FIX page as BlueBird keeps it: a radial in degrees and a distance in NM, each null where the
/// entry holds the layout's "none" value (the CDU's "300/300" holds both; "/15" only the distance).</summary>
public readonly record struct Bbs757EhsiFixEntry(int? Radial, int? Distance);

/// <summary>A FIX page's fix as the map draws it: its name as stored, its position (null where the reader cannot place it:
/// a navaid fix while the layout lacks the map extras' leg position rule) and its entries in BlueBird's order.</summary>
public sealed record Bbs757EhsiFix(string Name, Bbs757EhsiMapPoint? Position, IReadOnlyList<Bbs757EhsiFixEntry> Entries);

/// <summary>The OFFPATH DES circles: their common center and the two radii in NM (clean, then with the speedbrake).</summary>
public readonly record struct Bbs757EhsiOffpathCircles(Bbs757EhsiMapPoint Center, double CleanRadius, double SpeedbrakeRadius);

/// <summary>The altitude range arc: its distance in NM by BlueBird's formula and the point that far along the true track
/// from the aircraft.</summary>
public readonly record struct Bbs757EhsiRangeArc(double Distance, Bbs757EhsiMapPoint Point);

/// <summary>A FIX radial's dashed line on the screen, in the map's pixels (the layout's draw constants): the first dash's
/// distance from the fix, how many dashes, the step from one dash's start to the next, and a dash's length.</summary>
public readonly record struct Bbs757EhsiRadialDashes(double Start, int Count, double Step, double Length)
{
    /// <summary>The most dashes the layout may give (its own count is 18): a wild layout value cannot spin the sampling.</summary>
    public const int MaxCount = 256;

    /// <summary>The farthest the layout's start, step and length may reach in pixels (its own are under 50): generous, and
    /// still a bound.</summary>
    public const double MaxPixels = 100_000;
}

/// <summary>
/// The map's guides (plan 28's map guides), read in a full reading only. In MAP and PLAN: the FIX pages switched on with a
/// fix (FIX 1 then FIX 2) and the off-path descent circles (null: not drawn). In MAP only (null in PLAN): the altitude range
/// arc (null: BlueBird's conditions do not hold) and the VNAV path deviation in feet as stored (null: no path for the route,
/// or its phase is not the descent). <see cref="DeviationClamp"/> and <see cref="Dashes"/> are the draw's constants (the
/// layout's), so the rows need no layout.
/// </summary>
public sealed record Bbs757EhsiMapGuidesReading(
    IReadOnlyList<Bbs757EhsiFix> Fixes,
    Bbs757EhsiOffpathCircles? Offpath,
    Bbs757EhsiRangeArc? Arc,
    double? PathDeviation,
    double DeviationClamp,
    Bbs757EhsiRadialDashes Dashes);
