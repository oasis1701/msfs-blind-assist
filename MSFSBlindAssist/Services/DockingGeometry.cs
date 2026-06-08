namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure, side-effect-free geometry + thresholds for docking guidance. Distances in
/// metres, angles in degrees. No I/O, no sim state — unit-testable via the probe.
/// </summary>
public static class DockingGeometry
{
    public const double MetresPerNm = 1852.0;

    // Engage / completion thresholds (tunable).
    public const double EngageGroundSpeedKts = 30.0;
    public const double EngageRangeMetres = 60.0;
    public const double EngageConeDeg = 70.0;
    public const double StopToleranceMetres = 1.5;
    public const double OvershootMetres = 2.0;
    public const double DisengageRangeMetres = EngageRangeMetres * 1.5;

    // Beep interval mapping (ms): slow when far, fast when near.
    public const double BeepIntervalFarMs = 1000.0;
    public const double BeepIntervalNearMs = 90.0;
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
}
