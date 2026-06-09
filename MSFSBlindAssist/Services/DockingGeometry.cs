namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure, side-effect-free geometry + thresholds for docking guidance. Distances in
/// metres, angles in degrees. No I/O, no sim state — unit-testable via the probe.
/// </summary>
public static class DockingGeometry
{
    public const double MetresPerNm = 1852.0;

    // Engage / completion thresholds (tunable).
    public const double EngageGroundSpeedKts = 15.0;
    /// <summary>Engage when the aircraft datum is within 50 m of the gate stop.</summary>
    public const double EngageRangeMetres = 50.0;
    public const double EngageConeDeg = 70.0;
    public const double StopToleranceMetres = 0.5;
    /// <summary>Announce overshoot when the door is 1 m past the stop.</summary>
    public const double OvershootMetres = 1.0;
    public const double SlowDownMetres = 6.0;
    /// <summary>"Slow down" only fires when ground speed exceeds this threshold (kts).</summary>
    public const double SlowDownSpeedKts = 5.0;
    public const double DisengageRangeMetres = EngageRangeMetres * 1.5;

    // Beep interval mapping (ms): slow when far, fast when near.
    public const double BeepIntervalFarMs = 1000.0;
    public const double BeepIntervalNearMs = 90.0;
    /// <summary>Beep ramps gradually over the final ~30 m of door distance.</summary>
    public const double BeepFarMetres = 30.0;
    public const double BeepNearMetres = 2.0;

    /// <summary>Normalize an angle (deg) to (-180, 180].</summary>
    public static double NormalizeDeg180(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg <= -180.0) deg += 360.0;
        return deg;
    }

    /// <summary>Forward (along-centerline) distance to the stop: + ahead, ~0 at stop, - overshot.</summary>
    public static double AlongTrackMetres(double distanceMetres, double headingErrorDeg)
        => distanceMetres * Math.Cos(NormalizeDeg180(headingErrorDeg) * Math.PI / 180.0);

    public static bool IsStop(double alongMetres) => alongMetres <= StopToleranceMetres && alongMetres > -OvershootMetres;
    public static bool IsOvershoot(double alongMetres) => alongMetres <= -OvershootMetres;

    /// <summary>Beep repeat interval (ms): linear in distance, clamped to [near, far].</summary>
    public static double BeepIntervalMs(double alongMetres)
    {
        double d = Math.Clamp(alongMetres, BeepNearMetres, BeepFarMetres);
        double t = (d - BeepNearMetres) / (BeepFarMetres - BeepNearMetres); // 0 near .. 1 far
        return BeepIntervalNearMs + t * (BeepIntervalFarMs - BeepIntervalNearMs);
    }

    /// <summary>Should docking engage given current ground speed, forward distance, and heading error?</summary>
    public static bool ShouldEngage(double groundSpeedKts, double alongMetres, double headingErrorDeg)
        => groundSpeedKts < EngageGroundSpeedKts
           && alongMetres > 0.0
           && alongMetres < EngageRangeMetres
           && Math.Abs(NormalizeDeg180(headingErrorDeg)) < EngageConeDeg;

    /// <summary>
    /// Overload that uses an explicit engage range (metres) instead of the
    /// <see cref="EngageRangeMetres"/> constant — used when a gate's GSX
    /// <c>gatedistancethreshold</c> provides a stand-specific range.
    /// </summary>
    public static bool ShouldEngage(double groundSpeedKts, double alongMetres, double headingErrorDeg, double engageRangeMetres)
        => groundSpeedKts < EngageGroundSpeedKts
           && alongMetres > 0.0
           && alongMetres < engageRangeMetres
           && Math.Abs(NormalizeDeg180(headingErrorDeg)) < EngageConeDeg;

    public const double MetresPerDegLat = 111320.0;

    /// <summary>
    /// Moves a stop point (degrees lat/lon) by a metric offset relative to a true heading:
    /// <paramref name="longitudinalM"/> FORWARD along <paramref name="headingTrueDeg"/> (deeper
    /// toward/along the gate axis) and <paramref name="lateralM"/> perpendicular, RIGHT of that
    /// heading = positive. Equirectangular — exact at the tens-of-metres docking scale. Pure +
    /// probe-tested (this is the one piece of load-bearing offset math; a sign error here parks
    /// the aircraft in the wrong spot). Bearing convention matches NavigationCalculator
    /// (0°=N, 90°=E): north = cos(hdg), east = sin(hdg).
    /// </summary>
    public static void ShiftStopMetres(
        double sLat, double sLon, double headingTrueDeg,
        double longitudinalM, double lateralM, out double newLat, out double newLon)
    {
        double hdg = headingTrueDeg * Math.PI / 180.0;
        double perp = hdg + Math.PI / 2.0; // +90° = right of the heading
        double north_m = longitudinalM * Math.Cos(hdg) + lateralM * Math.Cos(perp);
        double east_m  = longitudinalM * Math.Sin(hdg) + lateralM * Math.Sin(perp);

        newLat = sLat + north_m / MetresPerDegLat;
        double cosLat = Math.Cos(sLat * Math.PI / 180.0);
        newLon = sLon + (Math.Abs(cosLat) > 1e-9 ? east_m / (MetresPerDegLat * cosLat) : 0.0);
    }
}
