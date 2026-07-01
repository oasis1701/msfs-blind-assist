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

        // The constraint TYPE comes from the raw ARINC alt_descriptor (the unambiguous source); the
        // NUMBERS come from the robust MinAltitude (=alt1) / MaxAltitude (=alt2) ints. For a fix that
        // carries no raw descriptor (non-navdata source) fall back to inferring the type from the
        // formatted AltitudeRestriction string's prefix.
        string desc = (fix.AltDescriptor ?? string.Empty).Trim().ToUpperInvariant();
        if (desc.Length == 0)
            desc = InferDescriptorFromText(fix.AltitudeRestriction);

        double? min = fix.MinAltitude;
        double? max = fix.MaxAltitude;

        switch (desc)
        {
            case "+":   // at or above alt1
                constraint = AltitudeConstraintType.AtOrAbove;
                crossingAltitude = min;
                break;

            case "-":   // at or below alt1
                constraint = AltitudeConstraintType.AtOrBelow;
                crossingAltitude = min;
                break;

            case "B":   // between (block): alt1/alt2 are the two bounds, order not guaranteed
            {
                double? lo = null, hi = null;
                bool minOk = min.HasValue && min.Value > 0;
                bool maxOk = max.HasValue && max.Value > 0;
                if (minOk && maxOk)
                {
                    lo = System.Math.Min(min!.Value, max!.Value);
                    hi = System.Math.Max(min!.Value, max!.Value);
                }
                if (lo.HasValue && hi.HasValue && lo.Value < hi.Value)
                {
                    constraint = AltitudeConstraintType.Between;
                    crossingAltitude = lo;
                    crossingAltitudeUpper = hi;
                }
                else if (minOk || maxOk)
                {
                    // Single-bounded (or equal-bound) block — treat the present bound as a floor,
                    // the ARINC convention for a one-sided "B". Never silently drops the altitude.
                    constraint = AltitudeConstraintType.AtOrAbove;
                    crossingAltitude = minOk ? min : max;
                }
                break;
            }

            case "A":   // at alt1
            default:    // blank / unrecognized descriptor with an altitude ≈ "at" (ARINC default)
                if (min.HasValue && min.Value > 0)
                {
                    constraint = AltitudeConstraintType.At;
                    crossingAltitude = min;
                }
                break;
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

    /// <summary>Fallback when a fix carries no raw descriptor: infer it from the formatted restriction
    /// string (the prefixes <c>FormatAltitudeRestriction</c> emits). Returns "" if nothing matches.</summary>
    private static string InferDescriptorFromText(string? restriction)
    {
        string r = (restriction ?? string.Empty).Trim().ToUpperInvariant();
        if (r.StartsWith("AT OR ABOVE")) return "+";
        if (r.StartsWith("AT OR BELOW")) return "-";
        if (r.StartsWith("BETWEEN")) return "B";
        if (r.StartsWith("AT ")) return "A";
        return "";   // bare "NNNN FT" / empty → the switch's default treats a present altitude as "at"
    }
}
