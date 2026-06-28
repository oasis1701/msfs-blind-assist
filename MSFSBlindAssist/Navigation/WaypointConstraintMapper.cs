using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Maps a navdata <see cref="WaypointFix"/> (as parsed from a SID/STAR/approach leg or a SimBrief
/// route by <c>NavigationDatabaseProvider.ParseLegToWaypoint</c>) onto the Waypoint Flight Director's
/// per-slot guidance parameters: the altitude-crossing constraint (+ altitude(s)) and the inbound
/// course. Used when tracking a waypoint straight from the Electronic Flight Bag route viewer (Shift+E)
/// so the FD honours the published altitude/course automatically, instead of flying lateral-only
/// direct-to. The Track Fix window (Shift+F) lets the pilot enter these by hand; this derives the same
/// from the fix's own data.
/// </summary>
public static class WaypointConstraintMapper
{
    /// <summary>
    /// Derive (crossingAltitude, crossingAltitudeUpper, constraint, course) for a slot from a fix.
    /// A zero/absent altitude → no vertical constraint (lateral-only); a real (&gt;0) MAGNETIC inbound
    /// course → a course-tracking leg, else direct-to.
    /// </summary>
    public static (double? crossingAltitude, double? crossingAltitudeUpper,
                   AltitudeConstraintType constraint, double? course) FromFix(WaypointFix fix)
    {
        double? crossingAltitude = null, crossingAltitudeUpper = null;
        var constraint = AltitudeConstraintType.None;

        // The descriptor (AT / AT OR ABOVE / AT OR BELOW / BETWEEN) survives only on the semantic
        // AltitudeRestriction string; the numbers come from the robust MinAltitude (=alt1) / MaxAltitude
        // (=alt2) ints. So derive the TYPE from the string prefix and the VALUES from the ints.
        string r = (fix.AltitudeRestriction ?? string.Empty).Trim().ToUpperInvariant();
        double? min = fix.MinAltitude;
        double? max = fix.MaxAltitude;

        if (r.StartsWith("AT OR ABOVE"))
        {
            constraint = AltitudeConstraintType.AtOrAbove;
            crossingAltitude = min;
        }
        else if (r.StartsWith("AT OR BELOW"))
        {
            constraint = AltitudeConstraintType.AtOrBelow;
            crossingAltitude = min;
        }
        else if (r.StartsWith("BETWEEN") && min.HasValue && max.HasValue)
        {
            constraint = AltitudeConstraintType.Between;
            crossingAltitude = System.Math.Min(min.Value, max.Value);        // lower bound
            crossingAltitudeUpper = System.Math.Max(min.Value, max.Value);   // upper bound
        }
        else if (r.StartsWith("AT ") || (min.HasValue && min.Value > 0))
        {
            // Explicit "AT", or a bare altitude with a blank descriptor (ARINC blank ≈ "at").
            constraint = AltitudeConstraintType.At;
            crossingAltitude = min;
        }

        // A zero/absent crossing altitude is lateral-only — drop the constraint entirely.
        if (!crossingAltitude.HasValue || crossingAltitude.Value <= 0)
        {
            constraint = AltitudeConstraintType.None;
            crossingAltitude = null;
            crossingAltitudeUpper = null;
        }

        // Inbound course → a course-tracking leg, but ONLY for a real (>0) MAGNETIC course. True-course
        // legs (rare; converting would need the fix's magnetic variation) and zero/absent courses stay
        // direct-to.
        double? course = null;
        if (fix.Course is double c && c > 0 && !fix.IsTrueCourse)
            course = c % 360.0;

        return (crossingAltitude, crossingAltitudeUpper, constraint, course);
    }
}
