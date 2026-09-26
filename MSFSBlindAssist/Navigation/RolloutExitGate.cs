namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which steering-tone behaviour applies on a landing-rollout frame.
/// </summary>
public enum RolloutToneMode
{
    /// <summary>Tone paused — too fast for a heading cue to mean anything.</summary>
    Silent,
    /// <summary>
    /// Steer back onto the runway HEADING. Owns the long silent middle of the rollout.
    /// A heading cue only — there is no cross-track term, so an aircraft that has drifted
    /// and then re-aligned goes quiet while still laterally displaced. Deliberate; do not
    /// describe this mode as steering back to the CENTRELINE.
    /// </summary>
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

    /// <summary>Below this ground speed the runway-end countdown treats the aircraft as stopped.
    /// Shared by <c>TaxiGuidanceManager</c> and <see cref="RunwayEndCountdownGate"/>.</summary>
    public const double NoExitStoppedGroundSpeedKts = 3.0;

    /// <summary>
    /// How close to the far end of the runway counts as "at the end" for
    /// <see cref="RunwayEndCountdownGate"/> — the distance at which a stop means the pavement has
    /// run out and the pilot must turn around, rather than that they have simply stopped.
    ///
    /// <para>A GUIDANCE threshold, deliberately its own constant rather than the 500 ft / 150 m
    /// runway-end SPOKEN milestone it happens to coincide with. That table is built from the
    /// pilot's distance-unit setting (<c>DistanceMilestones</c> → <c>DistanceFormatter.IsMetres</c>),
    /// so reading it here moved this decision by ~8 ft when the pilot switched between feet and
    /// metres, and by whatever a future extra milestone would shift the positional index to.
    /// CLAUDE.md: "<c>DistanceFormatter</c> is a DISPLAY layer only — never use it for guidance
    /// thresholds; those must stay unit-native internally."</para>
    /// </summary>
    public const double NearRunwayEndFeet = 500.0;

    /// <summary>
    /// The CEILING on an exit's own turn window (<see cref="TurnWindowFeetFor"/>), the window used
    /// when no exit is targeted, and the straight-line bound in <see cref="IsVacateAwayFromPlannedExit"/>.
    /// Until 2026-09 it was the turn window for EVERY exit (how close to the exit a turn had to begin
    /// to count as taking it), sized by the worst case derived below.
    ///
    /// <para>Derived, not fitted. An exit node can sit forward of its actual pavement
    /// junction by up to <c>lateralTolerance / tan(exitAngle)</c>, where lateralTolerance is
    /// <c>halfWidth + 15 m</c> (see <c>TaxiGraph.GetLandingExits</c>). This gate can only fire
    /// for an exit the aircraft can deviate 15° onto, so exitAngle ≥ 15°; the worst case is a
    /// 200 ft runway: (30.5 + 15) / tan(15°) = 170 m = 558 ft. Add the app's own notion of
    /// "at the exit" — the 300 ft tone-arm distance plus the 150 ft "turn now" cue — for
    /// 1,008 ft, rounded to 1,000.</para>
    ///
    /// <para>Do NOT tighten this to <c>ROLLOUT_NEAR_EXIT_FT</c> (500): that would block
    /// legitimate turns at shallow-RET airports whose exits derive from hold-short nodes.</para>
    /// </summary>
    public const double TurnWindowFeet = 1000.0;

    /// <summary>
    /// Below this relative bearing an exit has no meaningful side and the direction test is
    /// skipped. Matches the existing <c>ExitAngleDegrees >= 3.0</c> gate in
    /// <c>alignedWithExit</c>: below 3° an exit is geometrically indistinguishable from
    /// straight ahead. The <c>ExitBearingTrue == 0.0</c> "unknown" sentinel is mapped into
    /// this band by <see cref="ExitRelativeBearingDeg"/> — NOT by the subtraction itself,
    /// which would place it at <c>-runwayHeadingTrue</c> and fabricate a side.
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
    /// How far BEHIND the aircraft an exit may be and still be the one vacated at.
    ///
    /// <para>Canonical here, and <c>EXIT_COVERAGE_GAP_FT</c> in
    /// <c>TaxiGraph.GetLandingExits</c> initialises from it, so the two cannot drift. The
    /// value was MEASURED there — across 266 runway directions at 39 airports, as the
    /// distance beyond which two nodes stop describing the same physical turnoff — and that
    /// provenance comment stays with the measurement. The direction of the reference is
    /// deliberate: this module promises no dependency on the graph, so the graph reads the
    /// gate rather than the reverse.</para>
    /// </summary>
    public const double EarlyVacateMaxPassedFeet = 1400.0;

    /// <summary>
    /// How far SHORT of the planned exit, measured ALONG the runway, the aircraft must be
    /// before leaving the pavement counts as vacating somewhere else.
    ///
    /// <para>Derived, not fitted. <c>TaxiGraph.GetLandingExits</c> refuses any exit node more
    /// than <c>halfWidth + 15 m</c> off the runway axis, while
    /// <see cref="IsLaterallyClearOfRunway"/> puts the pavement boundary at
    /// <c>halfWidth + <see cref="RunwayClearMarginM"/></c> (10 m) — so the node corridor
    /// extends exactly 5 m beyond the clear boundary. An aircraft that is laterally clear, on
    /// an exit path leaving the axis at angle θ, gains lateral offset at <c>tan θ</c> per unit
    /// of along-track, so its OWN exit's node can be at most <c>5 m / tan θ</c> ahead. θ is the
    /// AIRCRAFT'S OWN track angle away from the axis, NOT an exit-angle constant:
    /// <c>GetLandingExits</c> enforces no minimum exit angle for hold-short-derived nodes, and
    /// <see cref="ExitSideMinBearingDeg"/> is a side-KNOWABILITY floor, not a geometric one. At
    /// a 3° track that is 95.4 m = 313 ft; at the 15° <see cref="IsExitTurnBegun"/> needs, 61 ft.
    /// 350 rounds up the worst case.</para>
    ///
    /// <para>No empirical spacing floor between DISTINCT exits is claimed, and none should be
    /// read into <c>TaxiGraph.GetLandingExits</c>'s coverage-gap sweep — that measures the far
    /// ends of RET arcs, i.e. the SAME turnoff, not the spacing between different ones. The one
    /// real datum about close-together distinct exits is the closest same-name pair kept on the
    /// hold-short path, EGLL 09R S4E at 433 ft. The derivation above is what carries this
    /// threshold; do not raise it on the strength of a floor that was never measured.</para>
    ///
    /// <para>This does NOT contradict <see cref="TurnWindowFeet"/>. That 558 ft derivation is
    /// for an aircraft ON THE CENTRELINE, which is where <see cref="IsExitTurnBegun"/> fires;
    /// this gate only ever runs once the aircraft is OFF the pavement, which has already
    /// consumed all but 5 m of the same corridor. Two lateral states, two correct numbers.</para>
    /// </summary>
    public const double VacatedShortAlongTrackFeet = 350.0;

    /// <summary>
    /// Did the pilot leave the runway somewhere OTHER than the exit they picked?
    ///
    /// <para>The caller must additionally require the aircraft to be laterally CLEAR of the
    /// runway. That conjunct is not incidental — it is what makes
    /// <see cref="VacatedShortAlongTrackFeet"/>'s derivation valid, and it is independently
    /// what keeps a legitimate turn begun 800 ft out ON the runway from reaching this rule at
    /// all.</para>
    ///
    /// <para>The straight-line clause is kept beside the along-track one and is NOT redundant:
    /// the caller places no upper bound on lateral offset, so an aircraft that has driven a
    /// long way off the side can read a small along-track distance while being nowhere near
    /// the exit.</para>
    /// </summary>
    /// <param name="pastPlannedExit">
    /// True once the aircraft is beyond the planned exit along the runway. The overshoot
    /// detector owns that case, so this rule always answers false for it.
    /// </param>
    /// <param name="signedAlongPastPlannedFeet">
    /// Along-runway distance from the planned exit to the aircraft, FEET, POSITIVE when the
    /// aircraft is PAST the exit. "Short of the exit" is therefore negative.
    /// </param>
    /// <param name="distToPlannedExitFeet">Straight-line distance to the planned exit, feet.</param>
    public static bool IsVacateAwayFromPlannedExit(
        bool pastPlannedExit,
        double signedAlongPastPlannedFeet,
        double distToPlannedExitFeet)
        => !pastPlannedExit
           && (distToPlannedExitFeet > TurnWindowFeet
               || -signedAlongPastPlannedFeet > VacatedShortAlongTrackFeet);

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

    // ---- Runway lateral clearance.

    /// <summary>
    /// Margin beyond a runway's half-width inside which the aircraft still counts as being
    /// ON the pavement.
    ///
    /// <para>Canonical here so there is exactly ONE definition of "off the runway".
    /// <c>TaxiGuidanceManager.RUNWAY_CLEAR_MARGIN_M</c> initialises from this. Before
    /// 2026-08-22 the rollout's lateral handoff trigger carried its own <c>+30 ft</c>
    /// (9.144 m) spelling of the same idea, leaving a 0.856 m band in which the handoff
    /// fired while every guard still read the aircraft as on the runway.</para>
    ///
    /// <para>One spelling of that <c>+30 ft</c> deliberately survives: <c>exitedLaterallyPH</c>
    /// in <c>TaxiGuidanceManager.UpdatePosition</c>'s post-handoff overshoot monitor. It is
    /// behaviourally inert — the overshoot branch it guards additionally requires
    /// <c>lateralFtPH &lt; 30</c> ft, which the <c>+30 ft</c> arm can never satisfy, so the
    /// 0.856 m disagreement cannot change the outcome of the branch it guards. (Strictly, it
    /// disarms the monitor 0.856 m of lateral travel earlier, which could only matter for an
    /// excursion peaking inside that band and returning to the centreline.) It was left alone
    /// rather than churn a tuned safety path for no effect. So "exactly
    /// ONE definition" is true of every site that can DECIDE anything, not of every occurrence
    /// of the number in the tree.</para>
    /// </summary>
    public const double RunwayClearMarginM = 10.0;

    /// <summary>
    /// Width assumed for a runway whose navdata carries none. Matches the long-standing
    /// fallback in the rollout code.
    /// </summary>
    public const double DefaultRunwayWidthFeet = 200.0;

    /// <summary>
    /// Has the aircraft left the runway pavement laterally?
    ///
    /// <para>The single authority for that question. Both the rollout's lateral handoff
    /// trigger and the early-vacate / reachability guards route through it, so they cannot
    /// disagree about the same aircraft position.</para>
    ///
    /// <para>Strictly greater-than, so "exactly at the margin" is still ON the runway —
    /// the conservative direction for every caller.</para>
    /// </summary>
    /// <param name="absLateralMetres">
    /// Absolute perpendicular offset from the runway axis, metres — from
    /// <c>AbsLateralFromRunwayMeters</c> measured against a point ON the centreline
    /// (the runway start), never against an exit node.
    /// </param>
    /// <param name="runwayWidthFeet">
    /// The runway's width in FEET. Zero or negative means "not recorded" and falls back to
    /// <see cref="DefaultRunwayWidthFeet"/>.
    /// </param>
    public static bool IsLaterallyClearOfRunway(double absLateralMetres, double runwayWidthFeet)
    {
        double widthFt = runwayWidthFeet > 0.0 ? runwayWidthFeet : DefaultRunwayWidthFeet;
        double halfWidthM = widthFt * 0.3048 * 0.5;
        return absLateralMetres > halfWidthM + RunwayClearMarginM;
    }

    /// <summary>
    /// Which steering-tone behaviour applies this frame.
    ///
    /// <para><see cref="RolloutToneMode.Silent"/> (the ground-speed case) and
    /// <see cref="RolloutToneMode.ExitBearing"/> reproduce the pre-2026-08 behaviour exactly.
    /// <see cref="RolloutToneMode.DriftCorrection"/> is the 2026-08 fix and fills the gap
    /// that was silent: slowed down, but the exit is still far away.</para>
    ///
    /// <para>The window-and-direction check below is a SECOND, later fix: a pilot turning
    /// off between 3° and 15° of deviation (below <see cref="TurnBegunHeadingDeg"/>, so
    /// <c>turnBegun</c> hasn't accepted the turn yet), more than <see cref="ExitToneArmFeet"/>
    /// from the exit NODE, was getting a "hold the runway heading" DriftCorrection tone
    /// that directly opposes a turn <see cref="IsExitTurnBegun"/> is about to accept — real,
    /// not hypothetical, because an exit node can read forward of its pavement junction (up to
    /// 558 ft in <see cref="TurnWindowFeet"/>'s worst case). Silence, not opposition, is correct
    /// INSIDE the window: don't fight a turn the gate is about to accept.</para>
    ///
    /// <para>The window is a PARAMETER since 2026-09: the targeted exit's own
    /// (<see cref="TurnWindowFeetFor"/>), never more than <see cref="TurnWindowFeet"/>. The fixed
    /// 1,000 ft was far too wide for an exit whose node is its centreline junction: at KMEM 36L a
    /// leftover 8° right turn 631 ft before M7, whose own window is 324 ft, went Silent instead of
    /// getting the drift tone. Beyond the exit's own window a deviation toward its side is drift.</para>
    ///
    /// <para>A KNOWN exit side is required (<see cref="HasKnownExitSide"/>) — not merely
    /// deferred to <see cref="IsTurnTowardExit"/>'s own unknown-side degradation — because at
    /// an airport where <c>ExitBearingTrue</c> is unset a drift and an exit turn are
    /// indistinguishable, and there the drift tone must keep working rather than going
    /// silent.</para>
    /// </summary>
    /// <param name="turnWindowFeet">
    /// The targeted exit's own turn window (<see cref="TurnWindowFeetFor"/>). The turn-window Silent
    /// only applies inside it; beyond it a deviation toward the exit side is drift.
    /// </param>
    /// <param name="tooFastForExit">
    /// The aircraft is too fast for the targeted exit: <see cref="IsTooFastToTurn"/> before its turn
    /// point, or the exit was declined as too fast there. Below the tone line the tone is then
    /// DriftCorrection, the runway heading: never ExitBearing, and never the turn-window Silent. A turn
    /// toward an exit the aircraft cannot make is opposed, not led or silenced — an ExitBearing pan
    /// toward an off-centreline node at 35–50 kt led a pilot into a turn <c>turnBegun</c> then handed
    /// off at speed (the KMEM 36L shape).
    /// </param>
    public static RolloutToneMode SelectToneMode(
        double groundSpeedKts,
        double distToExitFeet,
        double headingDeltaSignedDeg,
        double exitRelativeBearingDeg,
        double turnWindowFeet,
        bool tooFastForExit = false)
    {
        if (groundSpeedKts > ToneActiveBelowGroundSpeedKts) return RolloutToneMode.Silent;
        if (tooFastForExit) return RolloutToneMode.DriftCorrection;
        if (distToExitFeet <= ExitToneArmFeet) return RolloutToneMode.ExitBearing;

        if (distToExitFeet <= turnWindowFeet
            && Math.Abs(headingDeltaSignedDeg) >= DriftToneSilentDeg
            && HasKnownExitSide(exitRelativeBearingDeg)
            && IsTurnTowardExit(headingDeltaSignedDeg, exitRelativeBearingDeg))
        {
            return RolloutToneMode.Silent;
        }

        return RolloutToneMode.DriftCorrection;
    }

    /// <summary>
    /// Does the exit have a knowable side, per the same <see cref="ExitSideMinBearingDeg"/>
    /// floor <see cref="IsTurnTowardExit"/> degrades on? False for the
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel (which
    /// <see cref="ExitRelativeBearingDeg"/> maps to 0.0) and for any exit close enough to
    /// dead-ahead to be geometrically
    /// indistinguishable from straight.
    /// </summary>
    public static bool HasKnownExitSide(double exitRelativeBearingDeg)
        => Math.Abs(exitRelativeBearingDeg) >= ExitSideMinBearingDeg;

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
    ///
    /// <para>The distance clause's window is a PARAMETER since 2026-09: the targeted exit's own
    /// (<see cref="TurnWindowFeetFor"/>), never more than the fixed <see cref="TurnWindowFeet"/> it
    /// replaced. The fixed 1,000 ft accepted a leftover 15° right turn 483 ft before KMEM M7, whose
    /// own window is 324 ft, as the M7 turn — and the handoff that followed swung the tone hard left
    /// as the aircraft left the runway.</para>
    /// </summary>
    /// <param name="exitRelativeBearingDeg">
    /// From <see cref="ExitRelativeBearingDeg"/> — never a hand-written
    /// <c>NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)</c>, which does not
    /// degrade the <c>ExitBearingTrue == 0.0</c> "unknown" sentinel and would hand this
    /// method a fabricated exit side on every runway not aligned near 360°.
    /// </param>
    /// <param name="turnWindowFeet">The targeted exit's own turn window (<see cref="TurnWindowFeetFor"/>).</param>
    public static bool IsExitTurnBegun(
        double headingDeltaSignedDeg,
        double groundSpeedKts,
        double distToExitFeet,
        bool pastExit,
        double exitRelativeBearingDeg,
        double turnWindowFeet)
    {
        if (Math.Abs(headingDeltaSignedDeg) < TurnBegunHeadingDeg) return false;
        if (groundSpeedKts >= TurnMaxGroundSpeedKts) return false;
        if (!pastExit && distToExitFeet > turnWindowFeet) return false;
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
    /// to the old direction-blind behaviour rather than stranding the pilot. Both current
    /// callers — <see cref="IsExitTurnBegun"/> and the post-handoff overshoot monitor's
    /// <c>turnBegunPH</c> — guard <c>headingDeltaSignedDeg</c> to at least
    /// <see cref="TurnBegunHeadingDeg"/> before calling here, but that guard is theirs, not
    /// this method's: nothing here enforces it. Called directly with a zero delta, this method
    /// returns false whenever the exit has a real side — the safe answer, since a zero
    /// deviation cannot be a turn toward anything — and true only when the exit has no
    /// meaningful side to disagree with.</para>
    /// </summary>
    public static bool IsTurnTowardExit(double headingDeltaSignedDeg, double exitRelativeBearingDeg)
    {
        if (!HasKnownExitSide(exitRelativeBearingDeg)) return true;
        return Math.Sign(headingDeltaSignedDeg) == Math.Sign(exitRelativeBearingDeg);
    }

    /// <summary>
    /// Which exit did the pilot actually turn onto, when the handoff fires away from the
    /// planned one?
    ///
    /// <para>Returns null when nothing qualifies. The caller must then CONCLUDE exit guidance
    /// with a spoken closure — never fall back to the planned exit. At KSEA 34L that fallback
    /// produced a 1,678 m route up the parallel taxiway and back down toward the runway,
    /// because the taxi graph carries no runway edges and that is the only path between the
    /// two exits.</para>
    ///
    /// <para>Selection ranks candidates by <c>Math.Abs(passed)</c> — nearest to the aircraft,
    /// not the smallest positive "last exit reached". <see cref="EarlyVacateForwardSlackFeet"/>
    /// of tolerance allows a candidate to read slightly ahead of the aircraft (an exit node
    /// forward of its own pavement junction), and nearest-by-absolute-value is what lets that
    /// still-ahead-but-close candidate win over one that reads further behind. A candidate must
    /// also lie strictly BEHIND the planned exit along the runway — a SIGNED test, not an
    /// absolute-distance one; see the guard's own comment for the self-contradicting callout
    /// that admits a beyond-the-planned-exit candidate, and for the ordinary neighbouring-exit
    /// vacate an absolute comparison threw away.</para>
    /// </summary>
    /// <param name="signedAlongPastFeet">
    /// Along-runway distance from each exit to the aircraft, in FEET, POSITIVE when the
    /// aircraft is PAST that exit. Supplied by the caller — usually
    /// <c>SignedAlongRunwayMeters(aircraftLat, aircraftLon, exit.Latitude, exit.Longitude,
    /// runwayHeadingTrue) * METERS_TO_FEET</c>.
    ///
    /// Measured PER EXIT and never against a threshold, which is what makes this immune to
    /// displaced thresholds. <c>LandingExit.DistanceFromThresholdFeet</c> is measured from the
    /// LANDING threshold including <c>ThresholdOffset</c> (KJFK 13R 2,055 ft, KJFK 22R
    /// 3,438 ft, EGLL 27R 1,004 ft), while the natural way to compute an aircraft's
    /// along-runway position measures from the physical runway start; comparing the two picks
    /// the wrong exit at every displaced-threshold runway. Do not reintroduce it.
    /// </param>
    /// <param name="aircraftLateralSignedMetres">
    /// Signed lateral offset of the aircraft from the runway axis, POSITIVE = right of the
    /// runway direction, matching <c>LandingExit.ExitSide</c> == "Right".
    /// </param>
    public static LandingExit? MatchEarlyVacateExit(
        IReadOnlyList<LandingExit> allExits,
        LandingExit plannedExit,
        Func<LandingExit, double> signedAlongPastFeet,
        double aircraftLateralSignedMetres)
    {
        if (allExits == null || plannedExit == null || signedAlongPastFeet == null) return null;

        string side = aircraftLateralSignedMetres >= 0.0 ? "Right" : "Left";

        // A substitute must not sit BEYOND the planned exit. Without this a candidate inside
        // the forward slack can win while lying further down the runway than the planned
        // exit, and the callout contradicts itself: "Left the runway short of taxiway P. Now
        // following taxiway R." with R past P. That was unreachable while the branch was only
        // entered 1,000 ft short (any forward-slack candidate was then between the aircraft
        // and the planned exit) and became reachable when the entry moved to 350 ft.
        //
        // The test is SIGNED, deliberately. Comparing Math.Abs on both sides also threw away
        // a candidate BEHIND the aircraft whenever it lay further behind than the planned
        // exit lay ahead — which is the ordinary neighbouring-exit vacate this method exists
        // to catch: 1,500 ft exit spacing, an ~8° track, and the first laterally-clear frame
        // is ~910 ft past the exit actually taken and 590 ft short of the planned one. That
        // matched nothing and concluded with "left the runway short of Z" instead of
        // following the pilot onto Y. Only a candidate FURTHER DOWN the runway than the
        // planned exit is a contradiction; one behind the aircraft never is.
        double plannedAlongPast = signedAlongPastFeet(plannedExit);

        LandingExit? best = null;
        double bestRank = double.MaxValue;

        foreach (var candidate in allExits)
        {
            if (candidate == null) continue;
            if (candidate.NodeId == plannedExit.NodeId) continue;

            // A blank ExitSide means the graph could not determine a side, NOT that the exit
            // is on the wrong one. Rank it on distance instead of dropping it — excluding it
            // would strand the pilot at exactly the airports whose navdata is already thin.
            if (!string.IsNullOrEmpty(candidate.ExitSide)
                && !string.Equals(candidate.ExitSide, side, StringComparison.OrdinalIgnoreCase))
                continue;

            double passed = signedAlongPastFeet(candidate);
            if (passed < -EarlyVacateForwardSlackFeet) continue;  // still ahead of the aircraft
            if (passed > EarlyVacateMaxPassedFeet) continue;      // too far behind to be this turnoff

            // Smaller signedAlongPast = further down the runway. Refuse only a candidate at
            // or beyond the planned exit; see plannedAlongPast above for why this is signed.
            if (passed <= plannedAlongPast) continue;

            double rank = Math.Abs(passed);

            if (best == null || rank < bestRank)
            {
                best = candidate;
                bestRank = rank;
            }
        }

        return best;
    }

    /// <summary>
    /// Is the route the handoff just built one the aircraft can actually follow from where
    /// it is standing?
    ///
    /// <para>Refusing means CONCLUDING exit guidance with a spoken closure, which is why the
    /// test is deliberately permissive: an on-runway handoff is never refused, and a segment
    /// with no width gets a generous fallback.</para>
    ///
    /// <para>This tests proximity to the TARGET TAXIWAY, not the presence of pavement. Navdata
    /// carries only runway and taxi_path polygons and cannot prove there is asphalt underfoot.
    /// What it does guarantee is that the steering tone is never pointed at a taxiway the
    /// aircraft is not essentially already on — the KSEA 34L failure, where the tone panned
    /// 79° right at a segment 53.9 m away with the aircraft 17.8 m outside the runway edge.</para>
    /// </summary>
    /// <param name="crossTrackToFirstSegmentMetres">
    /// Distance from the aircraft to the nearest point ON the route's first segment (clamped
    /// to the segment, so endpoints count) — <c>TaxiGraph.PerpendicularDistanceMetersStatic</c>.
    /// </param>
    public static bool IsHandoffRouteReachable(
        bool aircraftOffRunway,
        double crossTrackToFirstSegmentMetres,
        double firstSegmentPathWidthFeet)
    {
        if (!aircraftOffRunway) return true;

        // Clamp to the same generous half-width a MISSING width gets: a width we do not
        // believe is treated no better than a width we do not have. Navdata reports
        // thousands of feet where an apron is mis-tagged as a taxi path, and uncapped that
        // widened the corridor until this guard could never refuse anything — including the
        // 53.9 m KSEA 34L case it was written for. 25 m of half-width is a 164 ft taxiway,
        // wider than any real one, so no genuine segment is narrowed by this.
        double halfWidthM = firstSegmentPathWidthFeet > 0.0
            ? Math.Min(firstSegmentPathWidthFeet * 0.3048 * 0.5, HandoffReachDefaultHalfWidthM)
            : HandoffReachDefaultHalfWidthM;

        return crossTrackToFirstSegmentMetres <= halfWidthM + HandoffReachMarginM;
    }

    // ---- Per-exit rules (KMEM 36L, 2026-09-26 — docs/taxi-guidance.md, "Exits measured by branch").

    /// <summary>
    /// A branch whose turn to LEAVE the runway pavement (<c>LandingExitBranch.TurnToLeaveDeg</c>: the
    /// sharpest turn from the landing heading up to and including the first node beyond the runway
    /// half-width) is more than this
    /// leaves BACKWARD: for this landing direction it is a turnaround, not an exit. Judged at the
    /// pavement edge, never at the clear line further out. The same 110° that divides "Normal" from
    /// "End" in <c>TaxiGraph.GetLandingExits</c> (<c>NORMAL_MAX_DEG</c>).
    /// </summary>
    public const double TurnaroundAboveDeg = 110.0;

    /// <summary>
    /// The angle recorded for a turnaround: <c>NORMAL_MAX_DEG + 20</c>, the value <c>GetLandingExits</c>
    /// has always forced for a backward-peeling stub, so every "&gt; 90°" filter (retarget, re-plan,
    /// undershoot, the planner default) keeps skipping it.
    /// </summary>
    public const double TurnaroundExitAngleDeg = 130.0;

    /// <summary>
    /// How far above an exit's turn-off speed (<see cref="ExitTurnOffSpeedKts"/>) "turn now" stops being
    /// a flyable instruction: 30 kt for an exit of 45° or more (or an unknown angle), 60 kt below 45°.
    /// A judgement value (KMEM 36L: "Turn right now" at 49 kt onto a 52° exit), not a measurement.
    /// </summary>
    public const double TooFastMarginKts = 10.0;

    /// <summary>A retarget sentence says "Straighten." only for at least this much heading off the runway.</summary>
    public const double StraightenMinDeviationDeg = 5.0;

    /// <summary>
    /// <see cref="TurnWindowFeet"/>'s own derivation, evaluated for ONE exit instead of its worst case:
    /// how far before the junction a <see cref="TurnBegunHeadingDeg"/> turn still stays on the runway
    /// (half-width / tan 15°), plus how far the exit node can sit FORWARD of its pavement junction (its
    /// own lateral offset / tan of the exit angle), capped at <see cref="TurnWindowFeet"/>.
    ///
    /// <para>The fixed 1,000 ft was this sum's worst case (200 ft runway, a marker 45.5 m off, a 15° exit
    /// = 930 ft). For an exit whose node IS its centerline junction the forward offset is ~0 and the
    /// window is ~300 ft: KMEM M7 = 324 ft. With the fixed window, a leftover 8–15° right turn from a
    /// missed M6 was treated as the M7 turn 483 ft out — the tone went silent, then the handoff swung it
    /// hard left while the aircraft left the runway.</para>
    /// </summary>
    /// <param name="runwayWidthFeet">Rollout runway width; ≤ 0 uses <see cref="DefaultRunwayWidthFeet"/>.</param>
    /// <param name="exitNodeLateralMetres">The exit node's lateral offset from the runway axis (sign ignored).</param>
    /// <param name="exitAngleDeg">The exit's angle; clamped to [15°, 90°] (0 = unknown counts as 15°).</param>
    public static double TurnWindowFeetFor(double runwayWidthFeet, double exitNodeLateralMetres, double exitAngleDeg)
    {
        double widthFt = runwayWidthFeet > 0.0 ? runwayWidthFeet : DefaultRunwayWidthFeet;
        double halfWidthM = widthFt * 0.3048 * 0.5;
        double turnTan = Math.Tan(TurnBegunHeadingDeg * Math.PI / 180.0);
        double angle = Math.Clamp(exitAngleDeg, TurnBegunHeadingDeg, MaxUsableExitTurnDeg);
        double angleTan = Math.Tan(angle * Math.PI / 180.0);
        double metres = halfWidthM / turnTan + Math.Abs(exitNodeLateralMetres) / angleTan;
        return Math.Min(TurnWindowFeet, metres / 0.3048);
    }

    /// <summary>The fastest ground speed at which "turn now" onto an exit of <paramref name="exitAngleDeg"/> is still said.</summary>
    public static double MaxTurnSpeedKts(double exitAngleDeg) => ExitTurnOffSpeedKts(exitAngleDeg) + TooFastMarginKts;

    /// <summary>
    /// How far past the targeted exit the rollout's overshoot handler waits before calling it missed:
    /// <paramref name="baseMarginFeet"/> (the exit-type margin), or 0 once the exit was declined as too
    /// fast at its turn point. A declined exit is not going to be taken, and every handoff that could move
    /// the aircraft on needs it short of the node or turning — so a pilot who obeyed "too fast to turn",
    /// braked and stopped just past the node got no handoff, no overshoot and no countdown: silent on an
    /// active runway. The handoffs still run first on the same frame, so a turn onto the exit is guided.
    /// </summary>
    public static double OvershootMarginFeet(double baseMarginFeet, bool tooFastDeclined)
        => tooFastDeclined ? 0.0 : baseMarginFeet;

    /// <summary>True when the aircraft is too fast to make the turn: "turn now" must not be said.</summary>
    public static bool IsTooFastToTurn(double groundSpeedKts, double exitAngleDeg)
        => groundSpeedKts > MaxTurnSpeedKts(exitAngleDeg);

    /// <summary>
    /// The ground speed above which a landing-exit callout appends "Slow down.": the exit's own
    /// <see cref="MaxTurnSpeedKts"/> (faster than the exit can be taken), except that an "End" exit never goes
    /// above <see cref="TaxiGroundSpeedKts"/>. An End exit is in the last 15% of the runway (or a turnaround),
    /// so missing it leaves little runway to stop on: its "Slow down." keeps the pre-2026-09 30 kt line even
    /// when its angle would allow 60 kt. One owner for the rollout's 500 ft callout and the two sentences that
    /// fold its "Slow down." (the crossing decline and the touchdown correction), and for the retarget
    /// sentence (<c>RetargetCallout</c>), which folds it the same way.
    /// </summary>
    public static double SlowDownAboveKts(double exitAngleDeg, string? exitType)
        => exitType == "End"
            ? Math.Min(MaxTurnSpeedKts(exitAngleDeg), TaxiGroundSpeedKts)
            : MaxTurnSpeedKts(exitAngleDeg);

    /// <summary>
    /// Should a retarget sentence tell the pilot to "Straighten."? Yes when the aircraft carries at least
    /// <see cref="StraightenMinDeviationDeg"/> of heading off the runway that the NEW exit would not accept
    /// as its own turn — toward its side AND inside its window (<see cref="TurnWindowFeetFor"/>), or past it.
    /// </summary>
    public static bool ShouldStraightenAfterRetarget(
        double headingDeltaSignedDeg,
        double newExitRelativeBearingDeg,
        double distToNewExitFeet,
        bool pastNewExit,
        double newExitTurnWindowFeet)
    {
        if (Math.Abs(headingDeltaSignedDeg) < StraightenMinDeviationDeg) return false;
        bool insideWindow = pastNewExit || distToNewExitFeet <= newExitTurnWindowFeet;
        return !(insideWindow && IsTurnTowardExit(headingDeltaSignedDeg, newExitRelativeBearingDeg));
    }

    /// <summary>
    /// May the tone steer to <paramref name="exitBearingTrue"/>? Only a KNOWN bearing (0 is the unknown
    /// sentinel) within <see cref="TurnaroundAboveDeg"/> of the runway heading. KMEM M6 (2026-09-26) had
    /// 127° true on a 359° runway — the tone demanded a hairpin at 49 kt.
    /// </summary>
    public static bool IsPlausibleExitBearing(double exitBearingTrue, double runwayHeadingTrue)
        => exitBearingTrue != 0.0
           && Math.Abs(NormalizeAngle(exitBearingTrue - runwayHeadingTrue)) <= TurnaroundAboveDeg;

    /// <summary>
    /// Decode a <c>LandingExit.ExitBearingTrue</c> into a bearing relative to the runway,
    /// POSITIVE = RIGHT, handling the <c>0.0</c> "unknown" sentinel.
    ///
    /// <para>The ONE owner of that sentinel. A plain
    /// <c>NormalizeAngle(exitBearingTrue - runwayHeadingTrue)</c> does NOT degrade safely:
    /// for the sentinel it yields <c>NormalizeAngle(-runwayHeadingTrue)</c> — −20° on a 020°
    /// runway, +90° on a 270° runway — which <see cref="HasKnownExitSide"/> accepts as a real
    /// side, so the direction test then compares a live heading against a fabricated one.
    /// Returning 0.0 puts it inside <see cref="ExitSideMinBearingDeg"/>, which is what
    /// actually disables the direction test.</para>
    /// </summary>
    public static double ExitRelativeBearingDeg(double exitBearingTrue, double runwayHeadingTrue)
        => exitBearingTrue != 0.0
            ? NormalizeAngle(exitBearingTrue - runwayHeadingTrue)
            : 0.0;

    /// <summary>
    /// Beyond this turn angle an "exit" is a turnaround, not a way off the runway.
    /// <see cref="MSFSBlindAssist.Navigation.TaxiGraph.GetLandingExits"/> encodes a
    /// backward-peeling stub by forcing its angle to 130, so the same number screens both a
    /// genuinely obtuse turnoff and a stub aimed back at the approach end.
    /// </summary>
    public const double MaxUsableExitTurnDeg = 90.0;

    /// <summary>
    /// Floor of how far ahead of the aircraft an exit must be to count as takeable at the current
    /// speed. Shared by the rollout's undershoot retarget scan and the touchdown re-plan
    /// (<see cref="LandingExitReplan"/>), so the two can never disagree about reachability. The
    /// values were tuned on the undershoot scan (YSSY 16R: an exit 79 ft ahead at 52 kt was picked
    /// and could not be made), which runs only below 50 kt. At touchdown speed this floor asks for
    /// 4-6 m/s² of braking, so the re-plan tries <see cref="ComfortableExitLeadFeet"/> first and
    /// uses this only in its fallback pass.
    /// </summary>
    public const double ExitLeadMinFeet = 200.0;

    /// <summary>Speed-proportional part of <see cref="ExitLeadFeet"/>, feet per knot.</summary>
    public const double ExitLeadFeetPerKnot = 11.0;

    /// <summary>The lead an exit needs ahead of the aircraft at <paramref name="groundSpeedKts"/>.</summary>
    public static double ExitLeadFeet(double groundSpeedKts)
        => Math.Max(ExitLeadMinFeet, groundSpeedKts * ExitLeadFeetPerKnot);

    /// <summary>
    /// Turn-off speed for a rapid exit, below <see cref="SteepExitAngleDeg"/>. The three turn-off values
    /// are shared with the rollout's undershoot retarget scan, whose ROLLOUT_UNDERSHOOT_* constants alias
    /// them.
    /// </summary>
    public const double ShallowExitTurnOffSpeedKts = 50.0;

    /// <summary>At or above this turn an exit is steep and taken at <see cref="SteepExitTurnOffSpeedKts"/>.</summary>
    public const double SteepExitAngleDeg = 45.0;

    /// <summary>Turn-off speed for a steep exit: the tighter turn demands more braking margin.</summary>
    public const double SteepExitTurnOffSpeedKts = 20.0;

    /// <summary>
    /// Braking the touchdown re-plan assumes a pilot is comfortable with, after
    /// <see cref="BrakingTransitionSeconds"/> at touchdown speed. A stated assumption, not aircraft
    /// performance (repo owner, 2026-09-15). 1.5 and 2.5 m/s² were measured as the bounds: 1.5 lengthened
    /// the median re-planned rollout by another ~600 ft, and 2.5 left a pilot braking at 2 m/s² hearing a
    /// retarget in about a quarter of re-planned landings.
    /// </summary>
    public const double ComfortableDecelerationMps2 = 2.0;

    /// <summary>Seconds at touchdown speed before braking builds (spoilers, brake onset).</summary>
    public const double BrakingTransitionSeconds = 2.0;

    /// <summary>
    /// The speed a landing rollout is braking TOWARD, not through: below it the aircraft is at
    /// normal taxi speed and is no longer shedding energy hard.
    /// <c>TaxiGuidanceManager.ROLLOUT_TAXI_GS_KTS</c>, the 30 kt of the rollout's taxi-speed handoff
    /// to ordinary taxi guidance, aliases it.
    ///
    /// <para>It is no longer the rollout's "Slow down." line for every exit: since 2026-09 that line is
    /// <see cref="SlowDownAboveKts"/> — the exit's own <see cref="MaxTurnSpeedKts"/> — and this 30 kt
    /// survives there only as an "End" exit's line. It is also the speed at or above which
    /// ground-traffic callouts stay silent on a landing-exit route (<c>GroundTrafficSuppression</c>).</para>
    ///
    /// <para>Used by <see cref="RolloutCalloutSupersession.ReachFeet"/> so that assuming braking
    /// does not run away at the slow end: at 22 kt an aircraft is not decelerating at
    /// <see cref="ComfortableDecelerationMps2"/>, it is taxiing.</para>
    /// </summary>
    public const double TaxiGroundSpeedKts = 30.0;

    private const double FeetPerSecondPerKnot = 1.6878;
    private const double FeetPerMetre = 1.0 / 0.3048;

    /// <summary>
    /// Turn-off speed for an exit of <paramref name="exitAngleDeg"/>. The unmeasured angle 0 counts as
    /// steep: the re-plan must not assume a rapid exit it cannot see.
    /// </summary>
    public static double ExitTurnOffSpeedKts(double exitAngleDeg)
        => exitAngleDeg > 0.0 && exitAngleDeg < SteepExitAngleDeg ? ShallowExitTurnOffSpeedKts : SteepExitTurnOffSpeedKts;

    /// <summary>
    /// How far ahead an exit must be for the aircraft to reach its turn-off speed with comfortable braking:
    /// <see cref="BrakingTransitionSeconds"/> at <paramref name="groundSpeedKts"/>, then
    /// <see cref="ComfortableDecelerationMps2"/> down to <see cref="ExitTurnOffSpeedKts"/>, and never less
    /// than <see cref="ExitLeadFeet"/>. At 140 kt: about 4,640 ft for a 90° exit and 4,184 ft for a 30°
    /// rapid exit, where <see cref="ExitLeadFeet"/> gives 1,540 ft.
    /// </summary>
    public static double ComfortableExitLeadFeet(double groundSpeedKts, double exitAngleDeg)
    {
        double v = groundSpeedKts * FeetPerSecondPerKnot;
        double vTurnOff = Math.Min(ExitTurnOffSpeedKts(exitAngleDeg), groundSpeedKts) * FeetPerSecondPerKnot;
        double deceleration = ComfortableDecelerationMps2 * FeetPerMetre;
        double kinematic = v * BrakingTransitionSeconds + (v * v - vTurnOff * vTurnOff) / (2.0 * deceleration);
        return Math.Max(ExitLeadFeet(groundSpeedKts), kinematic);
    }

    /// <summary>
    /// Where "downfield" starts when looking for the exit to retarget to after an overshoot.
    ///
    /// <para>Measured from whichever is further along the runway - the missed exit, or the
    /// AIRCRAFT - plus <paramref name="marginFeet"/>. Measuring from the exit alone is wrong
    /// for a high-speed exit, which is only declared missed once the aircraft is up to
    /// ROLLOUT_HIGHSPEED_OVERSHOOT_FT (500 ft) past it, because a rapid-exit turn can still be
    /// started late. From the exit, a turnoff 200 ft beyond it reads as "downfield" while
    /// sitting 250 ft BEHIND the wing, and the retarget would pan the tone back at it.</para>
    ///
    /// <para>The aircraft is a FLOOR, not an extra margin on top of one: the exit-relative
    /// cutoff is kept exactly as it was and simply never allowed to fall behind the wing. So
    /// in the ordinary case - a normal exit missed by the 100 ft overshoot margin - the cutoff
    /// is unchanged, and the only candidates this can ever remove are ones the aircraft has
    /// already rolled past, which were never takeable.</para>
    /// </summary>
    public static double DownfieldCutoffFeet(
        double plannedExitDistanceFromThresholdFeet,
        double signedAlongPastExitFeet,
        double marginFeet)
        => Math.Max(
               plannedExitDistanceFromThresholdFeet + marginFeet,
               plannedExitDistanceFromThresholdFeet + signedAlongPastExitFeet);

    /// <summary>
    /// The nearest exit in <paramref name="exits"/> lying beyond
    /// <paramref name="afterDistanceFromThresholdFeet"/> that the aircraft could actually
    /// take. Null when none does.
    ///
    /// <para>The caller passes the missed exit's distance PLUS the overshoot margin, so an
    /// entry at or below the cutoff is the exit just rolled past, or another node of the same
    /// arc; offering either would turn the aircraft round on the runway.</para>
    ///
    /// <para>An angle of 0 is GetLandingExits' "no bearing found" sentinel rather than a real
    /// measurement, so an exit whose geometry could not be measured stays eligible - it is
    /// still a way off the runway, and the alternative on offer is a backtrack.</para>
    ///
    /// <para>Assumes <paramref name="exits"/> is nearest-first, which every producer
    /// guarantees (GetLandingExits sorts, FindDownfieldExits sorts, MergeRescueExits sorts).</para>
    /// </summary>
    public static LandingExit? FirstSuitableDownfieldExit(
        IReadOnlyList<LandingExit>? exits,
        double afterDistanceFromThresholdFeet)
    {
        if (exits == null) return null;
        foreach (var e in exits)
        {
            if (e == null) continue;
            if (e.DistanceFromThresholdFeet <= afterDistanceFromThresholdFeet) continue;
            if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > MaxUsableExitTurnDeg) continue;
            return e;
        }
        return null;
    }

    /// <summary>
    /// The nearest exit in <paramref name="exits"/> beyond <paramref name="afterDistanceFromThresholdFeet"/>,
    /// suitable by <see cref="FirstSuitableDownfieldExit"/>'s own rules, that is at least
    /// <see cref="ComfortableExitLeadFeet"/> ahead of the aircraft for its OWN angle — the touchdown re-plan's
    /// comfortable pass (<c>LandingExitReplan.LeadFeet</c>, <c>LandingExitLeadTier.Comfortable</c>). Null when
    /// none is. The too-fast alternative prefers this: <see cref="ExitLeadFeet"/> was tuned below 50 kt, so above
    /// about 60 kt it can offer an exit that is itself too fast at its own turn point, one retarget after another.
    /// </summary>
    /// <param name="aircraftFromThresholdFeet">The aircraft's own along-track distance from the threshold.</param>
    public static LandingExit? FirstComfortableDownfieldExit(
        IReadOnlyList<LandingExit>? exits,
        double afterDistanceFromThresholdFeet,
        double aircraftFromThresholdFeet,
        double groundSpeedKts)
    {
        if (exits == null) return null;
        foreach (var e in exits)
        {
            if (e == null) continue;
            if (e.DistanceFromThresholdFeet <= afterDistanceFromThresholdFeet) continue;
            if (e.ExitAngleDegrees > 0.0 && e.ExitAngleDegrees > MaxUsableExitTurnDeg) continue;
            if (e.DistanceFromThresholdFeet - aircraftFromThresholdFeet
                    >= ComfortableExitLeadFeet(groundSpeedKts, e.ExitAngleDegrees))
                return e;
        }
        return null;
    }

    /// <summary>
    /// Folds the rescue scan's findings into the rollout's working exit list, nearest-first.
    ///
    /// <para>The rescue scan
    /// (<see cref="MSFSBlindAssist.Navigation.TaxiGraph.FindDownfieldExits"/>) knows nothing
    /// about the planner list, so it can rediscover an exit already in it. Two entries for one
    /// turnoff would let the fall-forward on a failed route retarget to the same place it just
    /// failed to reach, so a candidate within <see cref="EarlyVacateMaxPassedFeet"/> of a
    /// known exit sharing its name is dropped and the known one kept.</para>
    ///
    /// <para>Everything downstream of the retarget - the fall-forward, the undershoot scan,
    /// the early-vacate matcher - reads this one list and assumes nearest-first ordering.</para>
    /// </summary>
    public static List<LandingExit> MergeRescueExits(
        IReadOnlyList<LandingExit>? known,
        IReadOnlyList<LandingExit>? rescued)
    {
        var merged = new List<LandingExit>();
        if (known != null)
            foreach (var e in known) { if (e != null) merged.Add(e); }

        if (rescued != null)
        {
            foreach (var r in rescued)
            {
                if (r == null) continue;
                bool duplicate = false;
                foreach (var e in merged)
                {
                    if (!string.Equals(e.TaxiwayName, r.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Math.Abs(e.DistanceFromThresholdFeet - r.DistanceFromThresholdFeet)
                        <= EarlyVacateMaxPassedFeet)
                    { duplicate = true; break; }
                }
                if (!duplicate) merged.Add(r);
            }
        }

        merged.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));
        return merged;
    }

    /// <summary>
    /// Fold an angle into [−180, 180]. Private to keep this module free of the Services and
    /// graph dependencies its class doc promises — the same choice
    /// <see cref="LandingExitDestination"/> and <see cref="RunwayVacateResolver"/> make.
    /// </summary>
    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}
