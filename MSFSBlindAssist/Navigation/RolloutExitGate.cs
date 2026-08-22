namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which steering-tone behaviour applies on a landing-rollout frame.
/// </summary>
public enum RolloutToneMode
{
    /// <summary>Tone paused — too fast for a heading cue to mean anything.</summary>
    Silent,
    /// <summary>Steer back to the runway heading. Owns the long silent middle of the rollout.</summary>
    DriftCorrection,
    /// <summary>Steer at the exit junction. Owns the last 300 ft before the exit.</summary>
    ExitBearing
}

/// <summary>
/// The pure decision rules of the landing-exit rollout: when the steering tone speaks
/// and what it steers at, whether a heading deviation is the exit turn, which exit the
/// pilot actually vacated at, and whether a handoff route is one the aircraft can reach.
///
/// <para>Deliberately free of SimConnect, form and graph dependencies so the whole set is
/// unit-testable, following <see cref="LandingExitDestination"/> and
/// <see cref="RunwayVacateResolver"/>. <c>TaxiGuidanceManager</c> supplies the geometry.</para>
///
/// <para>Origin: KSEA ILS 34L, 2026-08-21. A 15.1° LEFT drift at 19.7 kt, 2,232 ft short of
/// the selected exit — on a runway whose every mapped exit is to the RIGHT — satisfied a
/// turn gate that tested only <c>Math.Abs(headingDelta) >= 15</c>. See
/// docs/design/2026-08-21-landing-exit-early-turn-design.md.</para>
/// </summary>
public static class RolloutExitGate
{
    // ---- Tone gating (values moved here from TaxiGuidanceManager so there is one source
    // ---- of truth; the private consts there now initialise from these).

    /// <summary>Above this ground speed the rollout tone is silent — crab/crosswind pan.</summary>
    public const double ToneActiveBelowGroundSpeedKts = 50.0;

    /// <summary>Distance to the exit at which the exit-bearing tone takes over.</summary>
    public const double ExitToneArmFeet = 300.0;

    // ---- Drift-correction tone thresholds.

    /// <summary>
    /// Below this heading deviation the drift tone is silent. 2.0° is the codebase's
    /// existing floor for a heading deviation that means anything — see the
    /// <c>Math.Max(2.0, ExitAngleDegrees * 0.7)</c> term in <c>alignedWithExit</c>.
    /// Cross-check against the KSEA capture: the normal rollout phase ran at 0.4–1.7°
    /// throughout, and the drift episode read 6.1° then 14.4°.
    /// </summary>
    public const double DriftToneSilentDeg = 2.0;

    /// <summary>One degree above the silent floor — the tone is fully active here.</summary>
    public const double DriftToneActivationDeg = 3.0;

    /// <summary>Full-pan saturation. Matches every other steering tone in the rollout file.</summary>
    public const double DriftToneMaxPanDeg = 15.0;

    // ---- Exit-turn gating.

    /// <summary>Heading deviation from the runway that counts as an exit turn.</summary>
    public const double TurnBegunHeadingDeg = 15.0;

    /// <summary>Above this ground speed a heading deviation is touchdown yaw, not a turn.</summary>
    public const double TurnMaxGroundSpeedKts = 90.0;

    /// <summary>
    /// How close to the exit a turn must begin to count as taking it.
    ///
    /// <para>Derived, not fitted. An exit node can sit forward of its actual pavement
    /// junction by up to <c>lateralTolerance / tan(exitAngle)</c>, where lateralTolerance is
    /// <c>halfWidth + 15 m</c> (see <c>TaxiGraph.GetLandingExits</c>). This gate can only fire
    /// for an exit the aircraft can deviate 15° onto, so exitAngle ≥ 15°; the worst case is a
    /// 200 ft runway: (30.5 + 15) / tan(15°) = 170 m = 558 ft. Add the app's own notion of
    /// "at the exit" — the 300 ft tone-arm distance plus the 150 ft "turn now" cue — for
    /// 858 ft, rounded to 1,000.</para>
    ///
    /// <para>Do NOT tighten this to <c>ROLLOUT_NEAR_EXIT_FT</c> (500): that would block
    /// legitimate turns at shallow-RET airports whose exits derive from hold-short nodes.</para>
    /// </summary>
    public const double TurnWindowFeet = 1000.0;

    /// <summary>
    /// Below this relative bearing an exit has no meaningful side and the direction test is
    /// skipped. Matches the existing <c>ExitAngleDegrees >= 3.0</c> gate in
    /// <c>alignedWithExit</c>: below 3° an exit is geometrically indistinguishable from
    /// straight ahead. <c>ExitBearingTrue == 0.0</c> — the "unknown" sentinel used throughout
    /// the rollout code — normalises into this band, which is the intended degradation.
    /// </summary>
    public const double ExitSideMinBearingDeg = 3.0;

    // ---- Early-vacate matching.

    /// <summary>
    /// How far AHEAD of the aircraft an exit node may read and still count as one the pilot
    /// has already reached. Same 558 ft node-displacement figure as
    /// <see cref="TurnWindowFeet"/>, rounded: a hold-short-marker exit node can read forward
    /// of the pavement junction the pilot actually turned at.
    /// </summary>
    public const double EarlyVacateForwardSlackFeet = 600.0;

    /// <summary>
    /// How far BEHIND the aircraft an exit may be and still be the one vacated at. This is
    /// the same value as <c>EXIT_COVERAGE_GAP_FT</c> in <c>TaxiGraph.GetLandingExits</c>,
    /// which that comment records as measured across 266 runway directions at 39 airports as
    /// the distance beyond which two nodes stop describing the same physical turnoff. That
    /// constant is method-local and cannot be referenced; keep the two in step.
    /// </summary>
    public const double EarlyVacateMaxPassedFeet = 1400.0;

    // ---- Handoff route reachability.

    /// <summary>
    /// Buffer added to a taxiway's half-width before refusing a handoff route. Reuses the
    /// same 15 m that <c>lateralToleranceM</c> in <c>TaxiGraph.GetLandingExits</c> adds to a
    /// runway half-width for "geometrically within this corridor".
    /// </summary>
    public const double HandoffReachMarginM = 15.0;

    /// <summary>
    /// Half-width assumed when a segment carries no <c>PathWidth</c>. Deliberately generous:
    /// this guard ENDS guidance, so missing navdata width must never cause a false refusal.
    /// </summary>
    public const double HandoffReachDefaultHalfWidthM = 25.0;

    /// <summary>
    /// Which steering-tone behaviour applies this frame.
    ///
    /// <para><see cref="RolloutToneMode.Silent"/> and <see cref="RolloutToneMode.ExitBearing"/>
    /// reproduce the pre-2026-08 behaviour exactly.
    /// <see cref="RolloutToneMode.DriftCorrection"/> is new and fills the gap that was silent:
    /// slowed down, but the exit is still far away.</para>
    /// </summary>
    public static RolloutToneMode SelectToneMode(double groundSpeedKts, double distToExitFeet)
    {
        if (groundSpeedKts > ToneActiveBelowGroundSpeedKts) return RolloutToneMode.Silent;
        if (distToExitFeet <= ExitToneArmFeet) return RolloutToneMode.ExitBearing;
        return RolloutToneMode.DriftCorrection;
    }

    /// <summary>
    /// Has the pilot begun the turn onto the selected exit?
    ///
    /// <para>Every argument is signed relative to the runway heading, POSITIVE = RIGHT.</para>
    ///
    /// <para>The direction and distance clauses are the 2026-08 fix. The gate used to be
    /// <c>Math.Abs(headingDelta) >= 15 &amp;&amp; gs &lt; 90</c>, which at KSEA 34L read a
    /// 15.1° LEFT deceleration drift, 2,232 ft short of an exit lying 13.6° to the RIGHT,
    /// as the exit turn. The handoff that followed pointed the steering tone at a graph node
    /// 54 m away and 17.8 m outside the runway edge.</para>
    ///
    /// <para>A genuine early turn-off at a DIFFERENT exit is not this method's job and is not
    /// lost by tightening it: <c>exitedLaterally</c> catches that from position, which no
    /// heading test can fake.</para>
    /// </summary>
    /// <param name="exitRelativeBearingDeg">
    /// <c>NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)</c>. The
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel lands inside
    /// <see cref="ExitSideMinBearingDeg"/> and disables the direction test.
    /// </param>
    public static bool IsExitTurnBegun(
        double headingDeltaSignedDeg,
        double groundSpeedKts,
        double distToExitFeet,
        bool pastExit,
        double exitRelativeBearingDeg)
    {
        if (Math.Abs(headingDeltaSignedDeg) < TurnBegunHeadingDeg) return false;
        if (groundSpeedKts >= TurnMaxGroundSpeedKts) return false;
        if (!pastExit && distToExitFeet > TurnWindowFeet) return false;
        return IsTurnTowardExit(headingDeltaSignedDeg, exitRelativeBearingDeg);
    }

    /// <summary>
    /// Is a heading deviation on the same side as the exit?
    ///
    /// <para>Exposed separately from <see cref="IsExitTurnBegun"/> because the post-handoff
    /// overshoot monitor needs the direction test WITHOUT the distance window — it runs when
    /// the aircraft is already near or past the exit, where a window would be wrong.</para>
    ///
    /// <para>Returns true when the exit has no meaningful side, so an unknown bearing degrades
    /// to the old direction-blind behaviour rather than stranding the pilot. Callers always
    /// pass a deviation of at least <see cref="TurnBegunHeadingDeg"/>, so
    /// <c>Math.Sign(headingDeltaSignedDeg)</c> is never zero here.</para>
    /// </summary>
    public static bool IsTurnTowardExit(double headingDeltaSignedDeg, double exitRelativeBearingDeg)
    {
        if (Math.Abs(exitRelativeBearingDeg) < ExitSideMinBearingDeg) return true;
        return Math.Sign(headingDeltaSignedDeg) == Math.Sign(exitRelativeBearingDeg);
    }
}
