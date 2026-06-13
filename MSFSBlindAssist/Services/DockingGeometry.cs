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

    // ─── Lineup intercept profile (shared with DockingGuidanceManager) ─────
    // The lateral cue steers to desiredHdg = gate heading + intercept, where the
    // intercept rises on a sqrt curve with cross-track and is capped at
    // MaxInterceptDeg. These were locals inside DockingGuidanceManager's
    // ComputeLineupError; they live here so the engage-feasibility check below
    // and the probe can share the exact same numbers.
    public const double MaxInterceptDeg = 35.0;
    public const double InterceptDeadbandFt = 1.0;
    public const double InterceptSaturationFt = 40.0;
    /// <summary>The squaring fade starts here: intercept begins easing toward 0.</summary>
    public const double SquareUpStartMetres = 6.0;
    /// <summary>The squaring fade completes here: cue is the pure gate heading.</summary>
    public const double SquareUpEndMetres = 2.5;
    /// <summary>Cross-track (ft) at or below which the squaring fade FULLY applies.</summary>
    public const double SquareUpCrossFullFt = 4.0;
    /// <summary>
    /// Cross-track (ft) at or above which the squaring fade is DISABLED and the full
    /// intercept is kept. KATL C55 regression (2026-06-10): the fade was keyed on
    /// along-track alone, so an aircraft still ~75 ft off the centerline at along 6 m
    /// had its desired heading snapped from (gate−35°) to the raw gate heading — a
    /// sudden full-right hard pan ~20 ft of along-track from the stop. Squaring up
    /// only makes sense when the aircraft is actually NEAR the line; far off the line
    /// the correct cue is "keep converging" until the lateral-miss callout adjudicates.
    /// </summary>
    public const double SquareUpCrossNoneFt = 8.0;

    /// <summary>
    /// Max lateral offset (m) from the gate centerline that still counts as a good
    /// park: gates the "GSX docking complete." callout and anchors the lateral-miss
    /// and engage-feasibility bounds. ~2 m keeps the jetway reachable; GSX itself
    /// tolerates a sloppier park, so this errs on announcing a miss rather than
    /// celebrating a bad park.
    /// </summary>
    public const double StopMaxCrossMetres = 2.0;

    /// <summary>
    /// Theoretical max cross-track closure per metre of along-track at the full
    /// 35° intercept: tan(35°) ≈ 0.70 m/m. Used by <see cref="IsLateralMiss"/> as
    /// the "could the pilot still possibly make it" bound.
    /// </summary>
    public static readonly double MaxCrossClosurePerMetre =
        Math.Tan(MaxInterceptDeg * Math.PI / 180.0);

    /// <summary>
    /// AVERAGE achievable cross-track closure per metre of along-track over a real
    /// approach, used for engage feasibility. Smaller than tan(35°)=0.70 because the
    /// sqrt intercept profile ramps DOWN as cross-track closes (numeric integration
    /// of d(cross)/d(along) = tan(35°·sqrt(cross/40ft)) from ~32 ft of cross gives
    /// ≈0.45 m closed per metre travelled). Engaging beyond this bound hands the
    /// pilot an approach the cue can never converge — KATL C55 (2026-06-10) engaged
    /// at along 25 m with 38 m of cross-track and was doomed from the first frame.
    /// </summary>
    public const double EngageCrossClosurePerMetre = 0.45;

    /// <summary>
    /// Max cross-track (m) at which docking may ENGAGE for a given along-track
    /// distance: what the intercept profile can realistically close by the time the
    /// squaring zone starts, plus the stop tolerance. Floors at
    /// <see cref="StopMaxCrossMetres"/> so an already-on-the-line aircraft right at
    /// the stop (e.g. a GSX reposition/teleport) still engages.
    /// </summary>
    public static double MaxEngageCrossMetres(double alongMetres)
        => StopMaxCrossMetres
           + EngageCrossClosurePerMetre * Math.Max(0.0, alongMetres - SquareUpEndMetres);
    /// <summary>
    /// Stop band (m). The aircraft is "at the stop" from this far out to <see cref="OvershootMetres"/>
    /// past. Tightened 0.5→0.3 m for an unforgiving park: it also gates the beeper's SOLID tone and
    /// the "GSX docking complete." callout, so the pilot creeps until the solid tone and lands within
    /// ~0.3 m of the exact stop instead of 0.5 m short.
    /// </summary>
    public const double StopToleranceMetres = 0.3;
    /// <summary>Announce overshoot when the aircraft datum is 1 m past the stop.</summary>
    public const double OvershootMetres = 1.0;
    public const double SlowDownMetres = 6.0;
    /// <summary>"Slow down" only fires when ground speed exceeds this threshold (kts).</summary>
    public const double SlowDownSpeedKts = 5.0;
    public const double DisengageRangeMetres = EngageRangeMetres * 1.5;

    // Beep interval mapping (ms): slow when far, fast when near.
    public const double BeepIntervalFarMs = 1000.0;
    public const double BeepIntervalNearMs = 90.0;
    /// <summary>Beep ramps gradually over the final ~30 m of forward distance to the stop.</summary>
    public const double BeepFarMetres = 30.0;
    /// <summary>
    /// Distance (m) at which the beep reaches its FASTEST pulse — set equal to
    /// <see cref="StopToleranceMetres"/> so the pulse keeps accelerating right up to the point the
    /// SOLID stop tone begins, with NO max-speed plateau before it. The old 2 m plateau made every
    /// distance from 2 m to the stop sound identical ("fast beep"), so the pilot read fast-beep as
    /// "stop" and parked up to ~1.5 m short, mid-alignment-turn. Now: accelerating pulse = keep
    /// creeping; solid tone = stop, here, exactly.
    /// </summary>
    public const double BeepNearMetres = StopToleranceMetres;

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

    /// <summary>
    /// Should docking engage given current ground speed, forward distance, heading
    /// error, and lateral offset? <paramref name="engageRangeMetres"/> is the gate's
    /// GSX <c>gatedistancethreshold</c> when set, else <see cref="EngageRangeMetres"/>.
    /// <para>
    /// The <paramref name="absCrossMetres"/> feasibility bound is the load-bearing
    /// part (KATL C55, 2026-06-10): the ±70° cone alone admitted an aircraft 38 m off
    /// the gate centerline at along 25 m — a geometry the 35°-max intercept can never
    /// close — so docking took the tone over from taxi guidance mid-turn and steered a
    /// doomed diagonal. While infeasible, docking stays Armed and taxi guidance keeps
    /// owning the turn; docking engages once the turn brings the cross-track within
    /// what the intercept profile can actually close (<see cref="MaxEngageCrossMetres"/>).
    /// </para>
    /// </summary>
    public static bool ShouldEngage(
        double groundSpeedKts, double alongMetres, double headingErrorDeg,
        double absCrossMetres, double engageRangeMetres)
        => groundSpeedKts < EngageGroundSpeedKts
           && alongMetres > 0.0
           && alongMetres < engageRangeMetres
           && Math.Abs(NormalizeDeg180(headingErrorDeg)) < EngageConeDeg
           && absCrossMetres <= MaxEngageCrossMetres(alongMetres);

    /// <summary>
    /// The mid-dock "this approach can no longer succeed" test, adjudicated only in
    /// the squaring zone (along ≤ <see cref="SquareUpStartMetres"/>): even at the full
    /// 35° intercept the remaining along-track cannot bring the cross-track within
    /// <see cref="StopMaxCrossMetres"/>. The caller announces a verbal stop-and-retry
    /// instead of letting the tone steer garbage. An engage-passing geometry can never
    /// trip this on the same frame: the engage bound (0.45·(along−2.5)) is strictly
    /// tighter than this one (0.70·along) for all along ≥ 0.
    /// </summary>
    public static bool IsLateralMiss(double alongMetres, double absCrossMetres)
        => alongMetres <= SquareUpStartMetres
           && absCrossMetres > StopMaxCrossMetres
                               + MaxCrossClosurePerMetre * Math.Max(0.0, alongMetres);

    /// <summary>
    /// Intercept angle (deg, signed; + = aim left of the gate heading) for the lateral
    /// docking cue: sqrt ramp over cross-track (deadband 1 ft, saturation 40 ft, cap
    /// 35°), then the squaring fade toward 0 over along 6 → 2.5 m — but the fade is
    /// CROSS-GATED: it fully applies only when the aircraft is within
    /// <see cref="SquareUpCrossFullFt"/> of the line, blends out by
    /// <see cref="SquareUpCrossNoneFt"/>, and is disabled beyond that, so an off-line
    /// aircraft near the stop keeps a steady converge cue instead of getting the
    /// desired heading snapped to the raw gate heading (the KATL C55 hard-pan bug).
    /// Continuous in BOTH inputs — no step anywhere in the (cross, along) plane.
    /// <paramref name="crossTrackFeet"/> uses the RunwayCenterlineTracker convention:
    /// + = aircraft left of the centerline.
    /// </summary>
    public static double LineupInterceptDeg(double crossTrackFeet, double alongMetres)
    {
        double absCross = Math.Abs(crossTrackFeet);

        double intercept = 0.0;
        if (absCross > InterceptDeadbandFt)
        {
            double eff = absCross - InterceptDeadbandFt;
            double span = InterceptSaturationFt - InterceptDeadbandFt;
            intercept = MaxInterceptDeg * Math.Sqrt(Math.Clamp(eff / span, 0.0, 1.0))
                        * Math.Sign(crossTrackFeet);
        }

        // Squaring fade (1 = keep intercept, 0 = squared to the gate heading) …
        double fade = Math.Clamp(
            (alongMetres - SquareUpEndMetres) / (SquareUpStartMetres - SquareUpEndMetres),
            0.0, 1.0);
        // … applied only to the extent the aircraft is actually near the line.
        double nearLine = Math.Clamp(
            (SquareUpCrossNoneFt - absCross) / (SquareUpCrossNoneFt - SquareUpCrossFullFt),
            0.0, 1.0);
        double multiplier = 1.0 - nearLine * (1.0 - fade);
        return intercept * multiplier;
    }

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

    /// <summary>
    /// Safety margin (m) the occupancy clamp keeps the datum INSIDE the static-circle edge.
    /// The KBOS E13 live sweep (2026-06-13) flipped GSX services→reposition at ~25.5 m
    /// (gatedistancethreshold 25); clamping to threshold − 2 m (23 m) was verified to give
    /// GSX arrival services AND a connecting jetway, with ~2.5 m of slack for docking scatter.
    /// </summary>
    public const double OccupancyClampMarginMetres = 2.0;

    /// <summary>
    /// Tolerance (m) for treating the base stop as "inside the static circle" in the clamp's
    /// gate (a). Scenery authors routinely set <c>gatedistancethreshold</c> equal to the stop
    /// distance (KBOS E13: both 25 m), so a strict <c>baseStopAlong &gt; threshold</c> test
    /// rejects exactly the gate we want to fix the moment float/coordinate noise pushes the
    /// computed gap a hair over the threshold. 1 m keeps that knife-edge on the CLAMP side
    /// while still cleanly excluding genuinely VDGS-reliant stands (EDDF A66: gap 25.1 m vs
    /// threshold 15 m — 10 m clear). Worst-case extra pull-short of the bar is margin + this = 3 m.
    /// </summary>
    public const double ClampBaseStopToleranceMetres = 1.0;

    /// <summary>
    /// Occupancy-safe clamp of the (already-.py-shifted) docking stop point so the aircraft
    /// DATUM cannot end OUTSIDE GSX's static occupancy region — a circle of radius
    /// ≈ <paramref name="gateDistanceThreshold"/> centred on this_parking_pos
    /// (<paramref name="ppLat"/>,<paramref name="ppLon"/>). When the GSX .py per-aircraft
    /// offset pushes the datum past that circle, GSX does NOT register the aircraft as parked
    /// and offers "reposition" instead of arrival services (live-verified KBOS E13: the B772
    /// datum landed 31.5 m out, past the 25 m circle → reposition; the same circle accepts the
    /// datum at ≤ 25 m, including 10 m BEHIND this_parking_pos → it is a radius, not a
    /// forward-only segment).
    /// <para>
    /// Fires ONLY when (a) the base stop itself is inside the circle
    /// (<paramref name="gateDistanceThreshold"/> ≥ base-stop along-track distance) AND (b) the
    /// shifted datum would land outside it. It then pulls the stop back ALONG the gate heading
    /// to (threshold − <see cref="OccupancyClampMarginMetres"/>), leaving the lateral component
    /// untouched. At gates whose stop already sits BEYOND the threshold (e.g. EDDF A66:
    /// threshold 15 &lt; gap 25.1) it is a strict NO-OP — those stands are accepted only via
    /// GSX's dynamic VDGS approach-dock, and clamping would pull the datum far short of the bar
    /// and break jetway reach. Also a NO-OP when the datum is already inside the circle, so
    /// currently-working docks are byte-identical (minimal blast radius: only datums that would
    /// otherwise land outside their circle are moved).
    /// </para>
    /// <para>
    /// Verified live (KBOS E13, B772, 2026-06-13): datum 31.5 m → 23 m → GSX arrival services
    /// + jetway connects; EDDF A66 / KATL C20 untouched. See
    /// docs/superpowers/specs/2026-06-13-gsx-vdgs-nose-stop-datum-handoff.md. Pure function;
    /// a sign error here parks the aircraft in the wrong spot — keep probe-tested.
    /// </para>
    /// </summary>
    /// <returns>true if the stop point was moved.</returns>
    public static bool ClampStopToOccupancy(
        double ppLat, double ppLon,
        double baseStopLat, double baseStopLon,
        double stopHeadingTrue, double gateDistanceThreshold,
        ref double sLat, ref double sLon,
        out double baseStopAlong, out double desiredAlong, out double clampedAlong)
    {
        baseStopAlong = AlongTrackFrom(ppLat, ppLon, baseStopLat, baseStopLon, stopHeadingTrue);
        desiredAlong  = AlongTrackFrom(ppLat, ppLon, sLat, sLon, stopHeadingTrue);
        clampedAlong  = desiredAlong;

        // (a) Base stop sits clearly beyond the static circle → VDGS-reliant gate; clamping
        // would pull the datum well short of the bar (EDDF A66: gap 25.1 m, threshold 15 m).
        // The tolerance keeps the author-set-threshold-==-stop-distance knife-edge (KBOS E13:
        // both 25 m) on the CLAMP side despite float/coordinate noise. (b) Datum already inside
        // the circle → nothing to fix. Either case: no-op.
        if (baseStopAlong > gateDistanceThreshold + ClampBaseStopToleranceMetres) return false;
        if (desiredAlong <= gateDistanceThreshold) return false;

        clampedAlong = gateDistanceThreshold - OccupancyClampMarginMetres;
        double pullBack = desiredAlong - clampedAlong; // > 0
        ShiftStopMetres(sLat, sLon, stopHeadingTrue, -pullBack, 0.0, out sLat, out sLon);
        return true;
    }

    /// <summary>
    /// Forward (along-<paramref name="headingTrueDeg"/>) distance in metres from
    /// (<paramref name="originLat"/>,<paramref name="originLon"/>) to
    /// (<paramref name="pLat"/>,<paramref name="pLon"/>). Equirectangular, same bearing
    /// convention as <see cref="ShiftStopMetres"/> (north = cos(hdg), east = sin(hdg)).
    /// </summary>
    private static double AlongTrackFrom(
        double originLat, double originLon, double pLat, double pLon, double headingTrueDeg)
    {
        double northM = (pLat - originLat) * MetresPerDegLat;
        double eastM  = (pLon - originLon) * MetresPerDegLat * Math.Cos(originLat * Math.PI / 180.0);
        double hdg = headingTrueDeg * Math.PI / 180.0;
        return northM * Math.Cos(hdg) + eastM * Math.Sin(hdg);
    }
}
