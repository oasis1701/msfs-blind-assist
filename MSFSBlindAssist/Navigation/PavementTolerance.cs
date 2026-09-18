namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The one definition of "within a taxi edge's pavement": how far (perpendicular, metres) a
/// position may sit from an edge's centreline and still count as on that edge.
///
/// <para>Shared by TaxiGuidanceManager's off-route detection and <see cref="RouteReachability"/>
/// so the two can never disagree about whether an aircraft is on a taxiway. The values are the
/// off-route thresholds exactly as they were tuned in TaxiGuidanceManager; moving them here
/// changed no behaviour.</para>
/// </summary>
public static class PavementTolerance
{
    /// <summary>Lower bound, so tiny or zero widths on unnamed connectors can't shrink the
    /// tolerance to nothing.</summary>
    public const double FloorMeters = 25.0;

    /// <summary>Added to the half-width; absorbs navdata centreline sampling error and
    /// pilot-discretion margin on wide aprons.</summary>
    public const double MarginMeters = 15.0;

    /// <summary>Used when a row reports no width. 75 ft covers FAA AC 150/5300 Code B/C
    /// taxiways (50-82 ft).</summary>
    public const double DefaultWidthFeet = 75.0;

    /// <summary>Some navdata rows report absurd widths (thousands of feet on mis-tagged aprons).
    /// Capped so one malformed row can't make the aircraft effectively never "off" that
    /// edge.</summary>
    public const double WidthCapFeet = 300.0;

    /// <summary>
    /// Tolerance in metres for an edge whose navdata width is <paramref name="widthFeet"/>:
    /// max(half-width + <see cref="MarginMeters"/>, <see cref="FloorMeters"/>), with a
    /// non-positive width replaced by <see cref="DefaultWidthFeet"/> and the width capped at
    /// <see cref="WidthCapFeet"/>.
    /// </summary>
    public static double ForWidthFeet(double widthFeet)
    {
        double widthFt = widthFeet > 0 ? widthFeet : DefaultWidthFeet;
        if (widthFt > WidthCapFeet) widthFt = WidthCapFeet;
        double halfWidthMeters = widthFt * 0.3048 * 0.5;
        return Math.Max(halfWidthMeters + MarginMeters, FloorMeters);
    }
}
