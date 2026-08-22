using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Polarity characterization for the manual-landing guidance laws.
///
/// These are not arithmetic tests — they pin SIGN CONVENTIONS. A sign error in guidance code does
/// not fail loudly; it produces a confident cue pointing the wrong way, which for a blind pilot on
/// short final is the worst possible failure. Visual guidance shipped exactly that bug once (the
/// PID's "leading minus", masked by autopilot-flown tests that tracked too tightly to expose it).
///
/// The contract these lock in, end to end:
///   high tone  → pull up      low tone → push down      silence → on profile
///   pan right  → steer right                            silence → centered and holding
/// and critically, that the rule is IDENTICAL either side of the 50 ft approach/flare boundary.
/// </summary>
public class LandingGuidanceLawTests
{
    // The manager's shipped values, so the tests exercise the real operating point.
    private const double GP_ANGLE_DEG = 3.0;
    private const double TCH_FT = 50.0;
    private const double HEIGHT_GAIN = 6.0;
    private const double APPROACH_FPM_PER_UNIT = 100.0;
    private const double FLARE_FPM_PER_UNIT = 60.0;
    private const double UNIT_RANGE = 6.0;
    private const double FLARE_TAU = 6.0;
    private const double FLARE_BIAS = 120.0;
    private const double LAT_DEG_PER_FT = 0.05;
    private const double LAT_DEG_PER_FPS = 0.4;
    private const double LAT_MAX_DEG = 5.0;

    /// <summary>Height of the 3° path at a given along-track distance — the "perfectly on path" case.</summary>
    private static double OnPathHeightFt(double alongTrackFt)
        => TCH_FT + alongTrackFt * Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0);

    /// <summary>The descent rate the 3° path requires at a given groundspeed.</summary>
    private static double OnPathVsFpm(double groundSpeedKts)
        => -(groundSpeedKts * 101.2686 * Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0));

    private static double Glidepath(double heightFt, double alongFt, double gsKts, double vsFpm)
        => LandingGuidanceLaws.GlidepathCommandFpm(
            heightFt, alongFt, gsKts, vsFpm, GP_ANGLE_DEG, TCH_FT, HEIGHT_GAIN);

    // ---- Glidepath law: positive = high / needs to go down -------------------------------

    [Fact]
    public void Glidepath_PerfectlyOnPathAndOnRate_CommandsEssentiallyNothing()
    {
        double along = 6000.0, gs = 140.0;
        double cmd = Glidepath(OnPathHeightFt(along), along, gs, OnPathVsFpm(gs));

        // Must land inside the manager's 75 fpm deadband — this is the case that must be SILENT.
        Assert.True(Math.Abs(cmd) < 1.0, $"expected ~0 command on a perfect path, got {cmd:F3}");
    }

    [Fact]
    public void Glidepath_AbovePath_CommandsPositive_MeaningGoDown()
    {
        double along = 6000.0, gs = 140.0;
        double cmd = Glidepath(OnPathHeightFt(along) + 100.0, along, gs, OnPathVsFpm(gs));

        Assert.True(cmd > 0, $"100 ft high must command positive (go down), got {cmd:F1}");
    }

    [Fact]
    public void Glidepath_BelowPath_CommandsNegative_MeaningPullUp()
    {
        double along = 6000.0, gs = 140.0;
        double cmd = Glidepath(OnPathHeightFt(along) - 100.0, along, gs, OnPathVsFpm(gs));

        Assert.True(cmd < 0, $"100 ft low must command negative (pull up), got {cmd:F1}");
    }

    [Fact]
    public void Glidepath_OnPathButSinkingTooFast_CommandsNegative_BeforeAnyHeightErrorExists()
    {
        // The rate term earning its keep: dead on the path but going through it. The cue must
        // fire NOW, not once a height error has accumulated.
        double along = 6000.0, gs = 140.0;
        double cmd = Glidepath(OnPathHeightFt(along), along, gs, OnPathVsFpm(gs) - 400.0);

        Assert.True(cmd < 0, $"on path but sinking fast must command pull-up, got {cmd:F1}");
    }

    [Fact]
    public void Glidepath_OnPathButLevellingOff_CommandsPositive_BeforeAnyHeightErrorExists()
    {
        double along = 6000.0, gs = 140.0;
        double cmd = Glidepath(OnPathHeightFt(along), along, gs, 0.0);

        Assert.True(cmd > 0, $"on path but levelling off must command push-down, got {cmd:F1}");
    }

    [Fact]
    public void Glidepath_HighButCorrectingHardEnough_FallsInsideTheSilenceBand()
    {
        // The behaviour that makes it feel like the flare rather than a proximity alarm: while
        // you are correcting at about the right rate the tone goes quiet, so you can stop the
        // correction cleanly instead of chasing it into an overshoot.
        double along = 6000.0, gs = 140.0;
        double heightErr = 40.0;
        // Height term is 40 × 6 = 240 fpm; cancel it with 240 fpm of extra sink.
        double cmd = Glidepath(OnPathHeightFt(along) + heightErr, along, gs,
                               OnPathVsFpm(gs) - heightErr * HEIGHT_GAIN);

        Assert.True(Math.Abs(cmd) < 75.0,
            $"high but correcting at the matching rate should be silent, got {cmd:F1}");
    }

    [Fact]
    public void Glidepath_RequiredDescentRateScalesWithGroundspeed()
    {
        // A faster aircraft needs a steeper fpm to hold the same 3°. If this ever inverted, the
        // guidance would be wrong in opposite directions for fast and slow aircraft.
        double along = 6000.0;
        double slowVs = OnPathVsFpm(110.0);
        double fastVs = OnPathVsFpm(170.0);

        Assert.True(fastVs < slowVs, "faster groundspeed must require a greater descent rate");
        Assert.True(Math.Abs(Glidepath(OnPathHeightFt(along), along, 110.0, slowVs)) < 1.0);
        Assert.True(Math.Abs(Glidepath(OnPathHeightFt(along), along, 170.0, fastVs)) < 1.0);
    }

    // ---- Flare law -----------------------------------------------------------------------

    [Fact]
    public void FlareTarget_IsAlwaysADescent_AndEasesTowardTouchdown()
    {
        double at50 = LandingGuidanceLaws.FlareTargetVsFpm(50.0, FLARE_TAU, FLARE_BIAS);
        double at20 = LandingGuidanceLaws.FlareTargetVsFpm(20.0, FLARE_TAU, FLARE_BIAS);
        double at0 = LandingGuidanceLaws.FlareTargetVsFpm(0.0, FLARE_TAU, FLARE_BIAS);

        Assert.True(at50 < 0 && at20 < 0 && at0 < 0, "flare target must always be a descent");
        Assert.True(at50 < at20 && at20 < at0, "sink must ease as the aircraft nears the ground");
        Assert.Equal(-120.0, at0, precision: 6);   // the touchdown bias, a firm-but-comfortable arrival
    }

    // ---- The shared boundary contract ------------------------------------------------------

    [Fact]
    public void ToneUnits_PositiveCommand_MapsLow_NegativeCommand_MapsHigh()
    {
        Assert.True(LandingGuidanceLaws.CommandToToneUnits(300.0, APPROACH_FPM_PER_UNIT, UNIT_RANGE) < 0,
            "high/floating must map LOW = push down");
        Assert.True(LandingGuidanceLaws.CommandToToneUnits(-300.0, APPROACH_FPM_PER_UNIT, UNIT_RANGE) > 0,
            "low/sinking must map HIGH = pull up");
    }

    [Fact]
    public void ToneUnits_ApproachAndFlare_AgreeOnDirection_AcrossThe50FootBoundary()
    {
        // THE invariant of the whole redesign: the rule the pilot learns in the flare — high means
        // pull up — must survive the phase change unchanged. Only the reference may differ.
        foreach (double command in new[] { -500.0, -200.0, -50.0, 50.0, 200.0, 500.0 })
        {
            double approach = LandingGuidanceLaws.CommandToToneUnits(command, APPROACH_FPM_PER_UNIT, UNIT_RANGE);
            double flare = LandingGuidanceLaws.CommandToToneUnits(command, FLARE_FPM_PER_UNIT, UNIT_RANGE);

            Assert.True(Math.Sign(approach) == Math.Sign(flare),
                $"approach and flare disagree on direction for command {command}");
        }
    }

    [Fact]
    public void ToneUnits_SaturateAtTheUnitRange_RatherThanRunningAway()
    {
        Assert.Equal(-UNIT_RANGE, LandingGuidanceLaws.CommandToToneUnits(99999.0, APPROACH_FPM_PER_UNIT, UNIT_RANGE));
        Assert.Equal(UNIT_RANGE, LandingGuidanceLaws.CommandToToneUnits(-99999.0, APPROACH_FPM_PER_UNIT, UNIT_RANGE));
    }

    // ---- Lateral law: positive = steer right, fed by + = left of centerline ------------------

    [Fact]
    public void Lateral_LeftOfCenterline_CommandsRight()
    {
        // RunwayCenterlineTracker's canonical sign: positive cross-track = aircraft LEFT.
        double cmd = LandingGuidanceLaws.LateralCommandDeg(60.0, 0.0, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG);
        Assert.True(cmd > 0, $"left of centerline must command a RIGHT turn, got {cmd:F2}");
    }

    [Fact]
    public void Lateral_RightOfCenterline_CommandsLeft()
    {
        double cmd = LandingGuidanceLaws.LateralCommandDeg(-60.0, 0.0, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG);
        Assert.True(cmd < 0, $"right of centerline must command a LEFT turn, got {cmd:F2}");
    }

    [Fact]
    public void Lateral_Centered_ButDriftingLeft_CommandsRight_BeforeAnyDisplacementExists()
    {
        // The rate term's whole purpose: the cue fires as the drift starts, not once you are
        // already off. A position-only law would sit silent here.
        double cmd = LandingGuidanceLaws.LateralCommandDeg(0.0, 8.0, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG);
        Assert.True(cmd > 0, $"drifting left from centered must command right, got {cmd:F2}");
    }

    [Fact]
    public void Lateral_OffCenterlineButClosingAtTheMatchingRate_FallsInsideTheSilenceBand()
    {
        // Correctly crabbed and converging = on profile. This is the case a crosswind produces,
        // and the case the rollout's intercept-crab law could not represent.
        double crossTrack = 40.0;                          // 40 ft left → 2.0° of position term
        double closingRate = -crossTrack * LAT_DEG_PER_FT / LAT_DEG_PER_FPS;  // cancels it exactly
        double cmd = LandingGuidanceLaws.LateralCommandDeg(
            crossTrack, closingRate, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG);

        Assert.True(Math.Abs(cmd) < 1.5,
            $"converging at the matching rate should be silent, got {cmd:F2}");
    }

    [Fact]
    public void Lateral_SaturatesAtTheCommandCeiling_SoPanCannotExceedFullScale()
    {
        Assert.Equal(LAT_MAX_DEG,
            LandingGuidanceLaws.LateralCommandDeg(5000.0, 500.0, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG));
        Assert.Equal(-LAT_MAX_DEG,
            LandingGuidanceLaws.LateralCommandDeg(-5000.0, -500.0, LAT_DEG_PER_FT, LAT_DEG_PER_FPS, LAT_MAX_DEG));
    }

    // ---- Along-track geometry ---------------------------------------------------------------

    [Fact]
    public void AlongTrack_RemovesTheLateralOffset_AndNeverGoesNegative()
    {
        Assert.Equal(4000.0, LandingGuidanceLaws.AlongTrackFeet(5000.0, 3000.0), precision: 6);
        Assert.Equal(1000.0, LandingGuidanceLaws.AlongTrackFeet(1000.0, 0.0), precision: 6);
        // Degenerate case (cross-track exceeding the straight-line distance) must clamp, not NaN.
        Assert.Equal(0.0, LandingGuidanceLaws.AlongTrackFeet(100.0, 500.0), precision: 6);
    }

    // ---- Displaced-threshold anchoring -------------------------------------------------------
    //
    // The glidepath is anchored on the PAINTED LANDING THRESHOLD. Runway.StartLat/StartLon is the
    // physical pavement edge (runway_end.lonx/laty); Runway.ThresholdOffset is the gap between
    // them. LandingFlareAssistManager.Arm() projects across that gap. These tests pin WHY, because
    // the failure is silent: anchoring on the pavement edge does not error, it just flies the
    // aircraft low — over exactly the obstacles a displaced threshold exists to clear.

    private const double LGKR_16_OFFSET_FT = 1341.0;   // real value from the shipped fs2020 navdata

    [Fact]
    public void Glidepath_AnchoredOnThePaintedThreshold_IsSilentForAnAircraftCorrectlyOnPath()
    {
        // 1000 ft on the 3 deg path (the engage point), measured to the PAINTED threshold.
        double alongToPaintedThreshold = (1000.0 - TCH_FT) / Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0);
        double gs = 140.0;

        double cmd = Glidepath(
            OnPathHeightFt(alongToPaintedThreshold), alongToPaintedThreshold, gs, OnPathVsFpm(gs));

        Assert.True(Math.Abs(cmd) < 1.0, $"correct anchoring must be silent, got {cmd:F2} fpm");
    }

    [Fact]
    public void Glidepath_AnchoredOnThePavementEdge_FalselyCommandsDescent_AndSoFliesTheAircraftLow()
    {
        double alongToPaintedThreshold = (1000.0 - TCH_FT) / Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0);
        double gs = 140.0;

        // Same aircraft, same correct position — but alongTrack measured to the pavement edge,
        // which is nearer than the painted threshold by the displacement.
        double alongToPavementEdge = alongToPaintedThreshold - LGKR_16_OFFSET_FT;

        double cmd = Glidepath(
            OnPathHeightFt(alongToPaintedThreshold), alongToPavementEdge, gs, OnPathVsFpm(gs));

        // Positive = "high, go down" — on a perfectly flown approach. Far outside the 75 fpm
        // deadband, so the tone is not merely wrong, it is loud and wrong.
        Assert.True(cmd > 75.0,
            $"pavement-edge anchoring should falsely command descent, got {cmd:F1} fpm");

        // The error is exactly the path height lost across the displacement, times the height gain.
        double expected = LGKR_16_OFFSET_FT * Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0) * HEIGHT_GAIN;
        Assert.Equal(expected, cmd, precision: 6);
    }

    [Fact]
    public void ThresholdProjection_MovesTheAnchorAlongTheRunwayByTheDisplacement()
    {
        // LGKR 16: true heading 168.7, displaced 1341 ft.
        const double lat = 39.6019, lon = 19.9117, hdg = 168.7;

        var (thrLat, thrLon) = MSFSBlindAssist.Navigation.NavigationCalculator
            .CalculateTouchdownAimPoint(lat, lon, hdg, LGKR_16_OFFSET_FT);

        double movedNm = MSFSBlindAssist.Navigation.NavigationCalculator
            .CalculateDistance(lat, lon, thrLat, thrLon);
        Assert.Equal(LGKR_16_OFFSET_FT / 6076.12, movedNm, precision: 5);

        // ...and it moves DOWN the runway, not across it: the bearing to the new anchor is the
        // runway heading. (This is why cross-track is unaffected by the fix — same line.)
        double bearing = MSFSBlindAssist.Navigation.NavigationCalculator
            .CalculateBearing(lat, lon, thrLat, thrLon);
        Assert.Equal(hdg, bearing, precision: 2);

        // A zero offset — 94.3% of runway ends, including every EGLL and KJFK end — is a no-op.
        var (noLat, noLon) = MSFSBlindAssist.Navigation.NavigationCalculator
            .CalculateTouchdownAimPoint(lat, lon, hdg, 0.0);
        Assert.Equal(lat, noLat, precision: 9);
        Assert.Equal(lon, noLon, precision: 9);
    }

    [Fact]
    public void Deviation_PavementEdgeAnchoring_ReportsAPerfectApproachAsHIGH()
    {
        // The same displacement error seen through the SPOKEN advisory rather than the fpm law.
        // This is the dangerous direction: the aircraft is exactly on the real path, the call says
        // "high", and a pilot who obeys it descends below the path across the displaced area.
        double alongToPaintedThreshold = (1000.0 - TCH_FT) / Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0);
        double onPathHeight = OnPathHeightFt(alongToPaintedThreshold);

        Assert.Equal(0.0, Deviation(onPathHeight, alongToPaintedThreshold), precision: 6);

        double wrong = Deviation(onPathHeight, alongToPaintedThreshold - LGKR_16_OFFSET_FT);
        Assert.Equal(LGKR_16_OFFSET_FT * Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0), wrong, precision: 6);

        // ~70 ft at LGKR — most of the 75 ft call band spent on an error that isn't there, and
        // over the 60 ft release threshold, so the false call would never even clear itself.
        Assert.True(wrong > 60.0, $"displacement error must be material, got {wrong:F0} ft");
    }

    // ---- Approach intercept offset: positive = left of centerline = fly RIGHT ---------------
    //
    // Same polarity contract as LateralCommandDeg, because the spoken headings hand over to the
    // flare's pan tone at 50 ft and the pilot's left/right rule must survive that boundary. A
    // sign error here is silent and confident — it would talk a blind pilot away from the runway.

    // The manager's shipped values, so the tests exercise the real operating point.
    private const double APP_CONVERGE_FRACTION = 0.6;
    private const double APP_MIN_LEAD_FT = 1500.0;
    private const double APP_MAX_DEG = 25.0;

    private static double Intercept(double crossFt, double alongFt)
        => LandingGuidanceLaws.InterceptOffsetDeg(
            crossFt, alongFt, APP_CONVERGE_FRACTION, APP_MIN_LEAD_FT, APP_MAX_DEG);

    [Fact]
    public void Intercept_OnCenterline_CommandsZero()
        => Assert.Equal(0.0, Intercept(0.0, 18000.0), precision: 9);

    [Fact]
    public void Intercept_LeftOfCenterline_IsPositive_MeaningFlyRight()
    {
        // + cross-track = LEFT of centerline ⇒ + offset ⇒ heading RIGHT of the runway heading.
        double deg = Intercept(crossFt: 400.0, alongFt: 18000.0);
        Assert.True(deg > 0, $"left of centerline must command a RIGHT intercept, got {deg:F2}");
    }

    [Fact]
    public void Intercept_RightOfCenterline_IsNegative_MeaningFlyLeft()
    {
        double deg = Intercept(crossFt: -400.0, alongFt: 18000.0);
        Assert.True(deg < 0, $"right of centerline must command a LEFT intercept, got {deg:F2}");
    }

    [Fact]
    public void Intercept_SelfScales_SteeperAsTheRunwayCloses()
    {
        // The whole reason this is geometric rather than a per-foot gain: the SAME offset must
        // command a steeper intercept when there is less distance left to close it in.
        double far = Intercept(crossFt: 300.0, alongFt: 30000.0);
        double near = Intercept(crossFt: 300.0, alongFt: 6000.0);
        Assert.True(near > far,
            $"same offset closer in must steepen the intercept: far={far:F2} near={near:F2}");
    }

    [Fact]
    public void Intercept_WashesOutAsOffsetShrinks()
    {
        double big = Intercept(crossFt: 800.0, alongFt: 18000.0);
        double small = Intercept(crossFt: 50.0, alongFt: 18000.0);
        Assert.True(small < big, $"a smaller offset must command a shallower intercept");
        Assert.True(small > 0, "…but still on the correct side while any offset remains");
    }

    [Fact]
    public void Intercept_ClampedToMax_EvenAtAbsurdOffsets()
    {
        Assert.Equal(APP_MAX_DEG, Intercept(50_000.0, 1000.0), precision: 9);
        Assert.Equal(-APP_MAX_DEG, Intercept(-50_000.0, 1000.0), precision: 9);
    }

    [Fact]
    public void Intercept_MinLeadFloor_KeepsShortFinalFromBlowingUp()
    {
        // As along-track → 0 the lead distance must floor at minLead, not collapse to zero and
        // demand a 90° turn onto the centerline over the threshold.
        double atThreshold = Intercept(crossFt: 100.0, alongFt: 0.0);
        double expected = Math.Atan2(100.0, APP_MIN_LEAD_FT) * 180.0 / Math.PI;
        Assert.Equal(expected, atThreshold, precision: 6);
        Assert.True(Math.Abs(atThreshold) < 5.0, "floored intercept must stay gentle, not vertical");
    }

    // --- Spoken-heading call cadence -------------------------------------------------
    // The manager's shipped values.
    private const double CALL_TARGET_REMAINING = 3.0;
    private const double CALL_MIN_SEC = 3.0;
    private const double CALL_MAX_SEC = 8.0;
    private const double FLARE_GATE_FT = 50.0;

    private static double CallInterval(double secondsToGate)
        => LandingGuidanceLaws.HeadingCallIntervalSec(
            secondsToGate, CALL_TARGET_REMAINING, CALL_MIN_SEC, CALL_MAX_SEC);

    [Fact]
    public void SecondsToFlareGate_MeasuresDescentToTheGateHeight()
    {
        // 1000 ft gear height at 700 fpm: 950 ft to run at 11.667 ft/s ≈ 81.4 s.
        double s = LandingGuidanceLaws.SecondsToFlareGate(1000.0, FLARE_GATE_FT, -700.0);
        Assert.Equal(950.0 / (700.0 / 60.0), s, precision: 6);
    }

    [Fact]
    public void SecondsToFlareGate_NotDescending_IsInfinite_SoNothingIsHurried()
    {
        // A level segment or a go-around is not approaching the gate — the cadence must stay
        // at its relaxed value rather than tightening toward the floor.
        Assert.True(double.IsPositiveInfinity(
            LandingGuidanceLaws.SecondsToFlareGate(400.0, FLARE_GATE_FT, 0.0)));
        Assert.True(double.IsPositiveInfinity(
            LandingGuidanceLaws.SecondsToFlareGate(400.0, FLARE_GATE_FT, +500.0)));
        Assert.Equal(CALL_MAX_SEC, CallInterval(double.PositiveInfinity), precision: 9);
    }

    [Fact]
    public void SecondsToFlareGate_AtOrBelowTheGate_IsZero()
    {
        Assert.Equal(0.0, LandingGuidanceLaws.SecondsToFlareGate(50.0, FLARE_GATE_FT, -600.0), precision: 9);
        Assert.Equal(0.0, LandingGuidanceLaws.SecondsToFlareGate(20.0, FLARE_GATE_FT, -600.0), precision: 9);
    }

    [Fact]
    public void CallInterval_BodyOfTheApproach_IsUnchangedAtTheRelaxedCadence()
    {
        // The regression this guards: the fix must not make the whole approach chattier. From
        // 1000 ft (~81 s to the gate) down to ~24 s to go, the cadence is the old flat value.
        Assert.Equal(CALL_MAX_SEC, CallInterval(81.4), precision: 9);
        Assert.Equal(CALL_MAX_SEC, CallInterval(40.0), precision: 9);
        Assert.Equal(CALL_MAX_SEC, CallInterval(24.0), precision: 9);
    }

    [Fact]
    public void CallInterval_ShortFinal_LeavesRoomForAtLeastOneMoreCall()
    {
        // THE BUG THIS FIXES: the last stretch to the 50 ft gate is only ~4 s at approach speed,
        // so a flat 8 s timer could never fire inside it — a correction that became necessary in
        // the final seconds was deferred past the gate and never spoken, and the flare that
        // follows is single-axis, so nothing downstream could catch it.
        foreach (double toGo in new[] { 12.0, 8.0, 5.0, 4.0 })
        {
            Assert.True(CallInterval(toGo) < toGo,
                $"with {toGo:F0}s to the gate the interval must still permit a call, " +
                $"got {CallInterval(toGo):F2}s");
        }
    }

    [Fact]
    public void CallInterval_LastFewSeconds_DeliberatelyStopsCalling()
    {
        // The floor is not a leftover — it is the point past which a new heading is not
        // actionable: inside ~3 s the aircraft is under ~80 ft, and a heading change cannot be
        // dialled, rolled in and flown before the flare takes over. Speaking one there buys a
        // distraction at the worst moment, not a correction. So the cadence intentionally
        // cannot fire inside the floor, and this is the documented end of lateral guidance.
        Assert.True(CallInterval(2.0) > 2.0, "inside the floor no further call may fire");
        Assert.True(CallInterval(1.0) > 1.0);
    }

    [Fact]
    public void CallInterval_NeverDropsBelowTheFloor_NoMatterHowLateOrOdd()
    {
        Assert.Equal(CALL_MIN_SEC, CallInterval(0.5), precision: 9);
        Assert.Equal(CALL_MIN_SEC, CallInterval(0.0), precision: 9);
        Assert.Equal(CALL_MIN_SEC, CallInterval(-5.0), precision: 9);   // past the gate
        Assert.Equal(CALL_MIN_SEC, CallInterval(double.NaN), precision: 9);
    }

    // ---- Glidepath DEVIATION: positive = high, path aimed at the touchdown point ----------

    private static double Deviation(double heightFt, double alongFt)
        => LandingGuidanceLaws.GlidepathDeviationFt(heightFt, alongFt, GP_ANGLE_DEG, TCH_FT);

    [Fact]
    public void Deviation_OnPath_IsZero()
    {
        Assert.Equal(0.0, Deviation(OnPathHeightFt(6000.0), 6000.0), precision: 6);
    }

    [Fact]
    public void Deviation_AbovePath_IsPositive_AndSpokenAsHIGH()
    {
        // 100 ft above the path must read +100, because the spoken word is chosen off the SIGN:
        // an inverted sign here tells a pilot who is low that they are high, on short final.
        Assert.Equal(100.0, Deviation(OnPathHeightFt(6000.0) + 100.0, 6000.0), precision: 6);
    }

    [Fact]
    public void Deviation_BelowPath_IsNegative_AndSpokenAsLOW()
    {
        Assert.Equal(-100.0, Deviation(OnPathHeightFt(6000.0) - 100.0, 6000.0), precision: 6);
    }

    [Fact]
    public void Deviation_PathAimsAtTheTouchdownPoint_NotTheThreshold()
    {
        // The reference path must reach the ground PAST the threshold (≈954 ft in at 3°/50 ft) —
        // a path aimed at the threshold itself would report "on path" for an approach set up to
        // land short of the pavement.
        double aimPointFt = TCH_FT / Math.Tan(GP_ANGLE_DEG * Math.PI / 180.0);
        Assert.True(aimPointFt > 800.0 && aimPointFt < 1100.0,
            $"aiming point should sit in the touchdown zone, got {aimPointFt:F0} ft");

        // At the threshold the path is TCH above the ground, not on it.
        Assert.Equal(TCH_FT, Deviation(TCH_FT, 0.0) + TCH_FT, precision: 6);
        Assert.Equal(0.0, Deviation(TCH_FT, 0.0), precision: 6);

        // Continuing the path down, height reaches zero exactly at the aiming point.
        Assert.Equal(0.0, Deviation(0.0, -aimPointFt), precision: 6);
    }

    [Fact]
    public void Deviation_SteepRunway_UsesItsOwnAngle()
    {
        // London City is 5.5°: on a 3° path there, an aircraft would be far low and told nothing.
        double along = 6000.0;
        double lcyOnPath = TCH_FT + along * Math.Tan(5.5 * Math.PI / 180.0);

        Assert.Equal(0.0,
            LandingGuidanceLaws.GlidepathDeviationFt(lcyOnPath, along, 5.5, TCH_FT), precision: 6);
        // The same height read against 3° would look wildly high — which is why the angle is
        // per-runway and not a constant.
        Assert.True(Deviation(lcyOnPath, along) > 200.0);
    }

    // ---- Wind correction: track command → flyable heading ---------------------------------

    private const double MAX_DRIFT = 20.0;

    private static double Corrected(double desiredTrack, double heading, double groundTrack)
        => LandingGuidanceLaws.WindCorrectedHeadingDeg(
            desiredTrack, LandingGuidanceLaws.DriftAngleDeg(heading, groundTrack), MAX_DRIFT);

    [Fact]
    public void Drift_NoWind_IsZero_AndTheHeadingEqualsTheTrack()
    {
        Assert.Equal(0.0, LandingGuidanceLaws.DriftAngleDeg(270.0, 270.0), precision: 9);
        Assert.Equal(270.0, Corrected(270.0, 270.0, 270.0), precision: 9);
    }

    [Fact]
    public void Drift_WindFromTheRight_CrabsTheNoseRIGHT_OfTheDesiredTrack()
    {
        // Nose 8° right of where the aircraft is actually going = wind from the right. To hold a
        // 270 track the pilot must fly 278 — the whole reason this exists.
        Assert.Equal(8.0, LandingGuidanceLaws.DriftAngleDeg(278.0, 270.0), precision: 9);
        Assert.Equal(278.0, Corrected(270.0, 278.0, 270.0), precision: 9);
    }

    [Fact]
    public void Drift_WindFromTheLeft_CrabsTheNoseLEFT_OfTheDesiredTrack()
    {
        Assert.Equal(-8.0, LandingGuidanceLaws.DriftAngleDeg(262.0, 270.0), precision: 9);
        Assert.Equal(262.0, Corrected(270.0, 262.0, 270.0), precision: 9);
    }

    [Fact]
    public void Drift_WrapsAcrossNorth_InsteadOfTakingTheLongWayRound()
    {
        // Heading 003, track 357: a 6° right crab, not a 354° one.
        Assert.Equal(6.0, LandingGuidanceLaws.DriftAngleDeg(3.0, 357.0), precision: 9);
        Assert.Equal(6.0, Corrected(0.0, 3.0, 357.0), precision: 9);
        // And the result comes back inside [0, 360) rather than as a negative heading.
        Assert.Equal(357.0, Corrected(0.0, 357.0, 0.0), precision: 9);
    }

    [Fact]
    public void Drift_IsClamped_SoABadTrackSampleCannotSwingTheCommandFar()
    {
        // A 90° "drift" is a bad sample or a turn, never wind. It must not be obeyed.
        Assert.Equal(270.0 + MAX_DRIFT, Corrected(270.0, 0.0, 270.0), precision: 9);
        Assert.Equal(270.0 - MAX_DRIFT, Corrected(270.0, 180.0, 270.0), precision: 9);
    }

    [Fact]
    public void Drift_PreservesTheInterceptSense_LeftOfCenterlineStillMeansSteerRight()
    {
        // The pilot's left/right rule has to survive the wind correction: the correction shifts
        // the number, it must never flip which side of the runway heading it falls on.
        double runwayTrack = 270.0;
        double intercept = LandingGuidanceLaws.InterceptOffsetDeg(
            crossTrackFeet: 400.0,          // + = LEFT of centerline
            alongTrackFeet: 12000.0, convergeFraction: 0.6, minLeadFeet: 1500.0, maxOffsetDeg: 25.0);
        Assert.True(intercept > 0.0, "left of centerline must intercept from the right");

        // In a left crosswind (nose left of track) the commanded heading still sits right of the
        // runway track once the intercept exceeds the drift.
        double commanded = Corrected(runwayTrack + intercept, 265.0, 270.0);
        Assert.Equal(runwayTrack + intercept - 5.0, commanded, precision: 9);
    }

    [Fact]
    public void CallInterval_IsMonotonic_TighteningAsTheGateCloses()
    {
        // Never a step change the pilot would hear as the calls suddenly turning urgent.
        double prev = CallInterval(90.0);
        for (double toGo = 88.0; toGo >= 1.0; toGo -= 1.0)
        {
            double now = CallInterval(toGo);
            Assert.True(now <= prev + 1e-9,
                $"cadence must not relax as the gate closes: {toGo + 1:F0}s={prev:F2} {toGo:F0}s={now:F2}");
            prev = now;
        }
    }
}
