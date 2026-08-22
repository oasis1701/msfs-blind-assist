namespace MSFSBlindAssist.Services;

/// <summary>
/// The pure guidance laws behind <see cref="LandingFlareAssistManager"/>, extracted so their
/// SIGN CONVENTIONS can be pinned by tests.
///
/// Polarity is the safety-critical property here: a sign error does not produce silence or a
/// missing cue, it produces confident guidance in exactly the wrong direction — the failure mode
/// visual guidance's PID once shipped (the "leading minus" bug, masked by autopilot-flown tests
/// that tracked too tightly to reveal it). Every function below states its polarity in its
/// summary, and <c>LandingGuidanceLawTests</c> asserts each one.
/// </summary>
public static class LandingGuidanceLaws
{
    private const double FPM_PER_KNOT = 101.2686;   // 1 kt of groundspeed, in feet per minute

    /// <summary>
    /// Glidepath deviation command for the APPROACH phase, in fpm-equivalent.
    ///
    /// POLARITY: <b>positive = high and/or sinking too little</b> (needs to go DOWN).
    ///
    /// PD, not P: the height term alone would settle with a standing offset, and the rate term
    /// alone is the flare's law — which tracks a nice sink rate down a PARALLEL path and never
    /// rejoins the glidepath. Both terms are required for "am I on track for the runway".
    /// </summary>
    /// <param name="heightAboveThresholdFt">Gear height above the runway THRESHOLD ELEVATION
    ///   (true MSL minus threshold elevation minus the datum→gear bias) — not AGL, which follows
    ///   terrain and would bend the reference path over rising ground.</param>
    /// <param name="alongTrackFt">Distance to the threshold measured along the centerline.</param>
    /// <param name="groundSpeedKnots">Used to derive the VS the path requires at this speed.</param>
    /// <param name="verticalSpeedFpm">Smoothed vertical speed (negative = descending).</param>
    public static double GlidepathCommandFpm(
        double heightAboveThresholdFt,
        double alongTrackFt,
        double groundSpeedKnots,
        double verticalSpeedFpm,
        double glidepathAngleDeg,
        double thresholdCrossingHeightFt,
        double heightGainFpmPerFt)
    {
        double slope = Math.Tan(glidepathAngleDeg * Math.PI / 180.0);

        double heightErrorFt = GlidepathDeviationFt(
            heightAboveThresholdFt, alongTrackFt, glidepathAngleDeg, thresholdCrossingHeightFt);

        // The VS this groundspeed needs just to STAY on the path (negative — a descent).
        double requiredVsFpm = -(groundSpeedKnots * FPM_PER_KNOT * slope);
        double vsErrorFpm = verticalSpeedFpm - requiredVsFpm;             // + = sinking too little

        return heightErrorFt * heightGainFpmPerFt + vsErrorFpm;
    }

    /// <summary>
    /// Vertical deviation from the glidepath, in feet — the number the approach phase SPEAKS
    /// ("100 low"). POLARITY: <b>positive = HIGH</b> (above the path, needs to come down).
    ///
    /// The path is anchored at <paramref name="thresholdCrossingHeightFt"/> over the threshold and
    /// continues down to touch the ground <c>TCH/tan(angle)</c> PAST it — ≈954 ft in at 3°/50 ft,
    /// the standard aiming point. So this is deviation from the path to the TOUCHDOWN POINT, not
    /// to the threshold: in a manual landing the question is where the wheels will arrive, and a
    /// path aimed at the threshold itself would call an approach "on path" that is set up to land
    /// short of the pavement.
    ///
    /// Split out of <see cref="GlidepathCommandFpm"/> (which is still unwired) so the deviation
    /// can be reported on its own, with no rate term: the pilot is being told a POSITION they can
    /// verify against the altimeter, not handed a control command.
    /// </summary>
    /// <param name="heightAboveThresholdFt">Gear height above the runway THRESHOLD ELEVATION
    ///   (true MSL minus threshold elevation minus the datum→gear bias) — not AGL, which follows
    ///   terrain and would bend the reference path over rising ground.</param>
    /// <param name="alongTrackFt">Distance to the threshold measured along the centerline.</param>
    public static double GlidepathDeviationFt(
        double heightAboveThresholdFt,
        double alongTrackFt,
        double glidepathAngleDeg,
        double thresholdCrossingHeightFt)
    {
        double slope = Math.Tan(glidepathAngleDeg * Math.PI / 180.0);
        double targetHeightFt = thresholdCrossingHeightFt + alongTrackFt * slope;
        return heightAboveThresholdFt - targetHeightFt;   // + = high
    }

    /// <summary>
    /// The HEADING to fly so that the aircraft's ground TRACK comes out as
    /// <paramref name="desiredTrackDeg"/>.
    ///
    /// The intercept geometry is a TRACK problem — it is about the path over the ground, and in a
    /// crosswind a heading flown as given produces a different track, so a raw heading command
    /// keeps re-chasing drift it never accounted for. TRK mode solves this in the aircraft, but
    /// only TRK/FPA-capable types have it (the 737s have HDG SEL only), so the correction is
    /// applied here instead and the result stays flyable in HDG SEL on everything:
    /// <c>heading = track + drift</c>, with drift measured live as
    /// <c>current heading − current ground track</c>.
    ///
    /// The clamp is a sanity bound, not tuning: drift is a measured difference of two angles, so a
    /// bad ground-track sample (or a track read while turning) must never be able to swing the
    /// commanded heading arbitrarily far off the intercept.
    ///
    /// The result is in whatever reference <paramref name="desiredTrackDeg"/> is given in
    /// (drift is a DIFFERENCE of two angles, so it is the same number in true or magnetic),
    /// normalized to [0, 360).
    ///
    /// Drift is passed in rather than derived from a heading/track pair so the caller is forced to
    /// decide what it feeds in: the value that belongs here is the SMOOTHED drift, and a signature
    /// that accepted the two raw angles would quietly accept an instantaneous one — which, sampled
    /// mid-turn, is not a drift angle at all.
    /// </summary>
    public static double WindCorrectedHeadingDeg(
        double desiredTrackDeg,
        double driftDeg,
        double maxDriftDeg)
        => NormalizeHeading(desiredTrackDeg + Math.Clamp(driftDeg, -maxDriftDeg, maxDriftDeg));

    /// <summary>
    /// Drift angle: <b>positive = the nose is RIGHT of the ground track</b> (wind from the right).
    /// Both inputs must be in the same reference; the difference is reference-independent.
    /// </summary>
    public static double DriftAngleDeg(double headingDeg, double groundTrackDeg)
        => NormalizeSignedDelta(headingDeg - groundTrackDeg);

    /// <summary>Wraps a heading into [0, 360).</summary>
    public static double NormalizeHeading(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }

    /// <summary>Wraps an angular difference into [-180, +180].</summary>
    public static double NormalizeSignedDelta(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg < -180.0) deg += 360.0;
        return deg;
    }

    /// <summary>
    /// Target vertical speed for the FLARE phase — the classic exponential flare law
    /// (targetVS = −(gearAGL/τ·60 + bias)) an autoland flies. Always negative (a descent).
    ///
    /// Sink rate, not pitch, is the reference: it is what the flare actually controls, and it is
    /// airframe-independent — pitch targets vary per aircraft and per weight/flap setting.
    /// </summary>
    public static double FlareTargetVsFpm(double gearAglFt, double tauSeconds, double touchdownBiasFpm)
        => -((gearAglFt / tauSeconds) * 60.0 + touchdownBiasFpm);

    /// <summary>
    /// Lateral centerline command for the AIRBORNE phases (approach + flare), in degrees.
    ///
    /// POLARITY: <b>positive = steer RIGHT</b>, fed by cross-track in the codebase's canonical
    /// sign (positive = aircraft LEFT of centerline, per <c>RunwayCenterlineTracker</c>).
    ///
    /// The rate term is not just damping — it is what makes silence achievable in a crosswind.
    /// A position-only law (or the rollout's intercept-CRAB law, which drives heading error to
    /// zero when centered) cannot represent "correctly crabbed and holding", so it would nag at a
    /// perfectly-flown approach and settle steadily downwind.
    /// </summary>
    /// <param name="crossTrackFeet">Signed cross-track: + = LEFT of centerline.</param>
    /// <param name="crossTrackRateFps">Signed rate: + = drifting further LEFT.</param>
    public static double LateralCommandDeg(
        double crossTrackFeet,
        double crossTrackRateFps,
        double degreesPerFoot,
        double degreesPerFootPerSecond,
        double maxCommandDeg)
    {
        double command = crossTrackFeet * degreesPerFoot
                       + crossTrackRateFps * degreesPerFootPerSecond;
        return Math.Clamp(command, -maxCommandDeg, maxCommandDeg);
    }

    /// <summary>
    /// Maps a guidance command onto tone units, in the shared polarity used by BOTH vertical
    /// phases: <b>positive command (high / floating) → NEGATIVE units → LOW frequency → push
    /// down</b>; negative command (low / sinking) → high frequency → pull up.
    ///
    /// The negation lives here, once, so the approach and flare phases cannot drift apart. The
    /// pitch of the tone must always point the direction the yoke needs to move — the rule the
    /// pilot learns in the flare has to survive unchanged across the 50 ft boundary.
    /// </summary>
    public static double CommandToToneUnits(double commandFpm, double fpmPerToneUnit, double unitRange)
        => Math.Clamp(-commandFpm / fpmPerToneUnit, -unitRange, unitRange);

    /// <summary>
    /// Intercept-heading offset for the APPROACH phase, in degrees relative to the runway
    /// heading — the number added to the runway heading to produce the heading spoken to the
    /// pilot ("fly heading 352").
    ///
    /// POLARITY: <b>positive = intercept from the LEFT, i.e. a heading RIGHT of the runway
    /// heading</b>, fed by cross-track in the codebase's canonical sign (positive = aircraft
    /// LEFT of centerline). Identical in sense to <see cref="LateralCommandDeg"/>, so the flare's
    /// pan tone continues the same left/right meaning the spoken headings established.
    ///
    /// GEOMETRIC, not a tuned gain: aim to rejoin the centerline after closing
    /// <paramref name="convergeFraction"/> of the distance still to run. That self-scales with
    /// how much approach is left — shallow far out, steeper as the runway closes — and washes
    /// out to zero as the offset does. A fixed degrees-per-foot gain cannot do this: it either
    /// saturates far out (where there is plenty of room) or over-steers close in (where there
    /// is none), because it has no idea how much runway is left to use.
    /// </summary>
    /// <param name="crossTrackFeet">Signed cross-track: + = LEFT of centerline.</param>
    /// <param name="alongTrackFeet">Distance still to run to the threshold along the centerline.</param>
    /// <param name="minLeadFeet">Floor on the closing distance, so the angle can't blow up as
    ///   along-track → 0 on very short final.</param>
    public static double InterceptOffsetDeg(
        double crossTrackFeet,
        double alongTrackFeet,
        double convergeFraction,
        double minLeadFeet,
        double maxOffsetDeg)
    {
        double leadFt = Math.Max(alongTrackFeet * convergeFraction, minLeadFeet);
        double deg = Math.Atan2(crossTrackFeet, leadFt) * 180.0 / Math.PI;
        return Math.Clamp(deg, -maxOffsetDeg, maxOffsetDeg);
    }

    /// <summary>
    /// Seconds remaining before the flare gate takes lateral guidance away, measured
    /// VERTICALLY — the descent from the current gear height down to the gate height at the
    /// current sink rate.
    ///
    /// Vertical, not along-track, because the gate itself is a HEIGHT: this is exact whatever
    /// the glidepath angle, groundspeed or wind, and it needs no assumption about where the
    /// threshold is. Returns <see cref="double.PositiveInfinity"/> when not descending (level
    /// segment, go-around) — the gate is not approaching, so nothing should be hurried.
    /// </summary>
    public static double SecondsToFlareGate(double gearAglFt, double gateAglFt, double verticalSpeedFpm)
    {
        double sinkFps = -verticalSpeedFpm / 60.0;      // + = descending
        if (!(sinkFps > 0.0)) return double.PositiveInfinity;

        double remainingFt = gearAglFt - gateAglFt;
        return remainingFt <= 0.0 ? 0.0 : remainingFt / sinkFps;
    }

    /// <summary>
    /// Minimum interval between spoken approach headings, scaled by the time still to run
    /// before the flare gate.
    ///
    /// A FLAT interval is the wrong shape at the end of an approach. The last stretch down to
    /// the 50 ft gate is only ~4 s at approach speed, so an 8 s timer can never fire inside it:
    /// a correction that becomes necessary in the final seconds is deferred past the gate and
    /// never spoken at all — and after the gate there is no lateral cue of any kind. Aiming for
    /// <paramref name="targetRemainingCalls"/> more calls in whatever time is left keeps a slot
    /// open on short final, while leaving the body of the approach at the unchanged
    /// <paramref name="maxIntervalSec"/> cadence.
    ///
    /// This does NOT make the guidance chatty: the caller's minimum-CHANGE gate is untouched, so
    /// a correctly-tracked centerline stays silent no matter how short the interval gets. The
    /// interval only decides how soon a genuinely different heading is allowed to be spoken.
    /// </summary>
    public static double HeadingCallIntervalSec(
        double secondsToFlareGate,
        double targetRemainingCalls,
        double minIntervalSec,
        double maxIntervalSec)
    {
        if (double.IsNaN(secondsToFlareGate) || secondsToFlareGate <= 0.0) return minIntervalSec;

        double interval = secondsToFlareGate / Math.Max(1.0, targetRemainingCalls);
        return Math.Clamp(interval, minIntervalSec, maxIntervalSec);
    }

    /// <summary>
    /// Along-track distance to the threshold, with the lateral offset taken out of the
    /// straight-line distance. Never negative.
    /// </summary>
    public static double AlongTrackFeet(double straightLineFeet, double absCrossTrackFeet)
        => Math.Sqrt(Math.Max(0.0, straightLineFeet * straightLineFeet
                                 - absCrossTrackFeet * absCrossTrackFeet));
}
